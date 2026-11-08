using System;
using System.Collections.Generic;
using GameManagement;
using GameManagement.StateMachine;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Player.Lobby;
using Player.Scripts;
using Props.Scripts;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace DaleMod;

// Rôle bonus "Usurpateur" (Disguiser, RoleKit) : un FAINÉANT qui prend l'apparence des autres.
// - Mémoriser : viser un joueur à 1 m maximum et maintenir E 2 s sans bouger. Une nouvelle mémorisation remplace
//   l'ancienne.
// - Se déguiser / se changer : maintenir E 2 s sans viser de joueur. Il prend l'apparence, le nom et la couleur de la
//   victime, chez tous les joueurs. Aucune condition de solitude : faire attention fait partie du rôle.
// - Déguisements illimités, 2 min de délai après chaque déguisement (compté dès que le costume est enlevé) ; le délai
//   est en pause pendant la réunion elle-même (pas pendant les 15 s pour s'y rendre).
// - Réunion appelée : alerte avec compte à rebours ; à la fin du compte à rebours, le costume tombe tout seul.
// - Gagne avec les fainéants, pas de mission.
static class Usurpateur
{
    // Signaux : à lui seul (YouSignal, réunion), à tous via son PlayerInteractable (apparence), au serveur (demandes)
    const float YouSignal = -9600f, RestoreSignal = -9601f, MeetingCalledSignal = -9602f, MeetingStartedSignal = -9603f,
                MeetingEndedSignal = -9604f;
    const float LookBase = -9610f;       // - index du joueur copié (0..31)
    const float RandomBase = -9700f;     // - graine d'une apparence au hasard (debug, 0..29)
    const float ReqLookBase = -9750f;    // client -> serveur : - index
    const float ReqRandomBase = -9800f;  // client -> serveur : - graine
    const float ReqRestore = -9791f;
    const float PresetBase = -9840f;     // - n° d'un personnage enregistré de l'hôte (mannequin, debug, 0..9)
    const float ReqPresetBase = -9850f;  // client -> serveur : - n°
    const int MaxPresets = 10;
    const int MaxPlayers = 32, Seeds = 30;

    static readonly Color Purple = new(0.482f, 0.247f, 0.749f);   // #7B3FBF : couleur de repli (tout est en dégradé fainéant -> spécialiste)
    const float HoldSeconds = 2f, Cooldown = 120f, MemorizeDistance = 1f, MemorizeSeconds = 2f;
    static string T(string fr, string en) => RoleKit.T(fr, en);

    internal static BonusRole Role;

    internal static void Register()
    {
        Role = new BonusRole
        {
            Id = "usurpateur", Name = "Usurpateur", SettingKey = "ZoomerMod Usurpateur", Color = Purple, TeamGradient = true, SignalBase = -9670f,
            Pick = (players, taken, _) => RoleKit.PickOf(players, taken, PlayerRole.Slacker),
            Assign = (lp, _) => Make(lp),
            ServerTick = ServerTick,
            ServerReset = () => { usurpId = 0; ServerUndisguise(null); },
            ServerOutcome = () => usurpId == 0 ? null : GameManager.instance != null && GameManager.instance.GetWinner() == PlayerRole.Slacker,
            ServerHolder = () => usurpId,
            ClientReset = ClientReset,
            LocalIsHolder = () => mine,
            LocalHasHelp = () => mine,
            HelpText = HelpText,
            RoleCard = RoleCard,
            // Les fainéants gagnent : écran rouge pour tous, dégradé chez l'Usurpateur seul
            LocalResult = (rt, winner) => { if (winner == PlayerRole.Slacker) RoleKit.TintResultGradient(rt); },
            ResultLine = won => won ? T("Usurpateur : {0}, gagne avec les fainéants", "Disguiser: {0}, wins with the slackers")
                                    : T("Usurpateur : {0}", "Disguiser: {0}"),
        };
        RoleKit.Add(Role);

        NetSignal.OnLocalPlayer(YouSignal, () =>
        {
            mine = true;
            RoleKit.RefreshRoleTabs();
            RoleKit.ShowAfterCard(T("Tu es l'USURPATEUR ! Mémorise l'apparence d'un joueur, puis prends-la pour le faire accuser. (F1 : aide)",
                       "You are the DISGUISER! Memorize a player's look, then take it to get them blamed. (F1: help)"), 8f);
        });
        NetSignal.OnLocalPlayer(MeetingCalledSignal, () => meetingDropAt = Time.time + Meetings.Countdown(GameManager.instance));
        Meetings.Called += _ => { if (usurpId != 0) SendToUsurp(MeetingCalledSignal); };
        Meetings.Started += _ => { if (usurpId == 0) return; ServerUndisguise(null); SendToUsurp(MeetingStartedSignal); };
        Meetings.Ended += _ => { if (usurpId != 0) SendToUsurp(MeetingEndedSignal); };
        NetSignal.OnLocalPlayer(MeetingStartedSignal, () => { meetingDropAt = -1f; inMeeting = true; });
        NetSignal.OnLocalPlayer(MeetingEndedSignal, () => { meetingDropAt = -1f; inMeeting = false; });

        // Apparences, chez tous
        for (int i = 0; i < MaxPlayers; i++)
        {
            int idx = i;
            NetSignal.On(LookBase - idx, it => ApplyLookOf(it, idx));
            NetSignal.OnServer(ReqLookBase - idx, (_, sender) => ServerDisguise(sender, idx, -1));
        }
        for (int s = 0; s < Seeds; s++)
        {
            int seed = s;
            NetSignal.On(RandomBase - seed, it => ApplyRandom(it, seed));
            NetSignal.OnServer(ReqRandomBase - seed, (_, sender) => ServerDisguise(sender, -1, seed));
        }
        for (int n = 0; n < MaxPresets; n++)
        {
            int preset = n;
            NetSignal.On(PresetBase - preset, it => ApplyPreset(it, preset));
            NetSignal.OnServer(ReqPresetBase - preset, (_, sender) => ServerDisguise(sender, -1, -1, preset));
        }
        NetSignal.On(RestoreSignal, Restore);
        NetSignal.OnServer(ReqRestore, (_, sender) => { if (sender != null && RoleKit.LobbyOf(sender)?.netId == usurpId) ServerUndisguise(sender); });
    }

    // Joueurs dans un ordre commun au serveur et aux clients (netId croissant)
    static List<LobbyPlayer> Players()
    {
        var l = new List<LobbyPlayer>();
        foreach (var lp in Object.FindObjectsOfType<LobbyPlayer>()) if (lp != null) l.Add(lp);
        l.Sort((a, b) => a.netId.CompareTo(b.netId));
        return l;
    }

    static Player.PlayerController ControllerOf(LobbyPlayer lp)
    {
        if (lp == null) return null;
        foreach (var pc in Object.FindObjectsOfType<Player.PlayerController>())
            if (pc.lobbyPlayer != null && pc.lobbyPlayer.Pointer == lp.Pointer) return pc;
        return null;
    }

    static Player.PlayerController ControllerOf(Interactable it)
    {
        if (it == null || it.netIdentity == null) return null;
        foreach (var pc in Object.FindObjectsOfType<Player.PlayerController>())
            if (pc.netIdentity != null && pc.netIdentity.Pointer == it.netIdentity.Pointer) return pc;
        return null;
    }

    // ===================== Serveur (hôte) =====================
    static uint usurpId;
    static uint disguisedId;     // joueur actuellement déguisé (0 : personne)
    static int originalColor = -1;

    static void Make(LobbyPlayer lp)
    {
        usurpId = lp.netId;
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), YouSignal);
        Plugin.L.LogInfo("[USURP] Usurpateur tiré");   // jamais de nom
    }

    // Menu F7 (hôte) : rend ce joueur Usurpateur
    internal static string Force(LobbyPlayer lp)
    {
        if (!NetworkServer.active || lp == null) return "Hôte uniquement";
        if (lp.playerRole != PlayerRole.Slacker) return "L'Usurpateur doit être un fainéant (Slacker)";
        Make(lp);
        RoleKit.Announce(Role, lp);
        return null;
    }

    static void ServerDisguise(NetworkIdentity sender, int idx, int seed, int preset = -1)
    {
        var lp = RoleKit.LobbyOf(sender);
        if (lp == null || lp.netId != usurpId || lp.isFired) return;
        var pi = NetSignal.PlayerInteractableOf(sender);
        if (pi == null) return;
        if (disguisedId != 0 && disguisedId != lp.netId) ServerUndisguise(null);
        if (originalColor < 0) originalColor = lp.playerColorIndex;
        if (idx >= 0)
        {
            var players = Players();
            if (idx >= players.Count) return;
            var victim = players[idx];
            lp.NetworkplayerColorIndex = victim.playerColorIndex;   // SyncVar : la couleur suit chez tous
            NetSignal.Send(pi, LookBase - idx);
        }
        else if (preset >= 0) NetSignal.Send(pi, PresetBase - preset);
        else NetSignal.Send(pi, RandomBase - seed);
        disguisedId = lp.netId;
        Plugin.L.LogInfo($"[USURP] déguisement ({(idx >= 0 ? "joueur n°" + idx : preset >= 0 ? "personnage enregistré n°" + preset : "au hasard, graine " + seed)})");
    }

    // sender null : le joueur déguisé, quel qu'il soit (réunion, fin de partie)
    static void ServerUndisguise(NetworkIdentity sender)
    {
        if (disguisedId == 0) return;
        var lp = RoleKit.LobbyById(disguisedId);
        disguisedId = 0;
        if (lp != null)
        {
            if (originalColor >= 0) lp.NetworkplayerColorIndex = originalColor;
            var id = sender ?? RoleKit.PlayerIdOf(lp);
            var pi = NetSignal.PlayerInteractableOf(id);
            if (pi != null) NetSignal.Send(pi, RestoreSignal);
        }
        originalColor = -1;
        Plugin.L.LogInfo("[USURP] fin du déguisement");
    }

    // Réunion (Meetings) : alerte à l'appel, costume enlevé au vrai début, délai en pause jusqu'à la fin
    static void SendToUsurp(float signal)
    {
        var id = RoleKit.PlayerIdOf(RoleKit.LobbyById(usurpId));
        if (id != null) NetSignal.SendToPlayer(id, signal);
    }

    static void ServerTick(GameManager gm)
    {
        if (usurpId != 0 && gm.IsState(GameStates.GameFinished)) ServerUndisguise(null);
    }

    // ===================== Chez tous : apparences =====================
    // Joueur déguisé -> victime copiée (null : apparence inventée, nom dans Name). Ses étiquettes de nom sont réécrites
    // chaque image.
    sealed class Disguise { public Player.PlayerController Pc; public LobbyPlayer Victim; public string Name; }
    static readonly Dictionary<IntPtr, Disguise> disguises = new();

    static void ApplyLookOf(Interactable it, int idx)
    {
        var pc = ControllerOf(it);
        var players = Players();
        if (pc == null || idx >= players.Count) return;
        var victim = players[idx];
        Customize(pc, victim.playerCustomization, victim.playerRole);
        disguises[pc.Pointer] = new Disguise { Pc = pc, Victim = victim };
        if (pc.isLocalPlayer) OnLocalDisguised(victim.username, victim.playerColorIndex);
    }

    static readonly string[] FirstNames = { "Gérard", "Sandrine", "Kevin", "Brigitte", "Jean-Mi", "Nadine", "Thierry", "Josiane", "Dylan", "Martine" };
    static readonly string[] LastNames = { "Dupont", "Lemoine", "Fournier", "Bertrand", "Roussel", "Garnier", "Perrin", "Masson", "Chevalier", "Morel" };

    static void ApplyRandom(Interactable it, int seed)
    {
        var pc = ControllerOf(it);
        var own = pc?.lobbyPlayer;
        if (own?.playerCustomization == null) return;
        var rng = new System.Random(seed * 7919 + 13);
        var look = PlayerCustomization.FromJson(own.playerCustomization.ToJson());
        var customizer = pc.GetComponentInChildren<PlayerCustomizer>(true);
        if (customizer?.customizationParameters != null && look.customizationParameterSelection != null)
            foreach (var sel in look.customizationParameterSelection)
            {
                if (sel == null || sel.parameterIndex < 0 || sel.parameterIndex >= customizer.customizationParameters.Count) continue;
                var p = customizer.customizationParameters[sel.parameterIndex];
                int models = p.customizationItems?.Count ?? 0, colors = p.colorItems?.Count ?? 0;
                for (int tries = 0; tries < 10; tries++)
                {
                    int m = models > 0 ? rng.Next(models) : sel.modelIndex, c = colors > 0 ? rng.Next(colors) : sel.colorIndex;
                    if (!p.IsCorrectCombination(m, c, sel)) continue;
                    sel.modelIndex = m; sel.colorIndex = c;
                    break;
                }
            }
        string name = FirstNames[rng.Next(FirstNames.Length)] + " " + LastNames[rng.Next(LastNames.Length)];
        look.firstName = name.Split(' ')[0];
        look.lastName = name.Split(' ')[1];
        Customize(pc, look, own.playerRole);
        disguises[pc.Pointer] = new Disguise { Pc = pc, Name = name };
        if (pc.isLocalPlayer) OnLocalDisguised(name, -1);
    }

    // Personnages enregistrés dans le menu de personnalisation (PlayerPrefs du PC qui les lit) : sert au mannequin, en
    // test solo. Chez un autre joueur, ce numéro désigne SES personnages : ne sert qu'à tester seul.
    static string PresetJson(int n)
    {
        try { return PlayerPrefs.GetString("playerCustomizationJson_" + n, ""); }
        catch (Exception e) { Plugin.L.LogWarning("[USURP] personnage enregistré : " + e.Message); return ""; }
    }

    static int CurrentPreset()
    {
        try { return PlayerPrefs.GetInt("currentCustomizationIndex", 0); } catch { return 0; }
    }

    static string PresetName(string json)
    {
        try { var look = PlayerCustomization.FromJson(json); return $"{look.firstName} {look.lastName}".Trim(); }
        catch { return null; }
    }

    static void ApplyPreset(Interactable it, int n)
    {
        var pc = ControllerOf(it);
        var own = pc?.lobbyPlayer;
        string json = PresetJson(n);
        if (own == null || string.IsNullOrEmpty(json)) { if (own != null) ApplyRandom(it, n); return; }
        var look = PlayerCustomization.FromJson(json);
        string name = $"{look.firstName} {look.lastName}".Trim();
        Customize(pc, look, own.playerRole);
        disguises[pc.Pointer] = new Disguise { Pc = pc, Name = name };
        if (pc.isLocalPlayer) OnLocalDisguised(name, -1);
    }

    static void Restore(Interactable it)
    {
        var pc = ControllerOf(it);
        if (pc == null) return;
        var own = pc.lobbyPlayer;
        if (own?.playerCustomization != null) Customize(pc, own.playerCustomization, own.playerRole);
        if (disguises.Remove(pc.Pointer)) RestoreNameTags(pc);
        if (pc.isLocalPlayer) OnLocalRestored();
    }

    static void Customize(Player.PlayerController pc, PlayerCustomization look, PlayerRole role)
    {
        var customizer = pc.GetComponentInChildren<PlayerCustomizer>(true);
        if (customizer == null || look == null) { Plugin.L.LogWarning("[USURP] PlayerCustomizer introuvable"); return; }
        try { customizer.Customize(look, role, false, false); }
        catch (Exception e) { Plugin.L.LogError("[USURP] Customize : " + e); }
    }

    // Étiquettes de nom : celle du monde (NameTag2 : nom du personnage, nom Steam, poste) et celle du HUD (NameTag).
    // Réécrites chaque image ET juste après leur propre mise à jour (le jeu les réécrit lui aussi), textes d'origine
    // gardés pour la fin. Les textes de la victime sont recomposés d'après ses données : chez la victime elle-même, il
    // n'y a pas d'étiquette visible à recopier (on ne voit pas son propre nom).
    static readonly Dictionary<IntPtr, string> savedTexts = new();
    static readonly HashSet<IntPtr> loggedTags = new();
    static NameTag2[] tags = new NameTag2[0];
    static UI.Tabs.HUDTab.NameTags.NameTag[] hudTags = new UI.Tabs.HUDTab.NameTags.NameTag[0];
    static float nextTagScan;

    // Étiquettes (y compris masquées), relevées au plus une fois par seconde
    static void ScanTags()
    {
        if (Time.unscaledTime < nextTagScan) return;
        nextTagScan = Time.unscaledTime + 1f;
        tags = Object.FindObjectsOfType<NameTag2>(true);
        hudTags = Object.FindObjectsOfType<UI.Tabs.HUDTab.NameTags.NameTag>(true);
    }

    static void ApplyNameTags()
    {
        if (disguises.Count == 0) return;
        ScanTags();
        foreach (var tag in tags) ApplyTag(tag);
        foreach (var tag in hudTags) ApplyHudTag(tag);
    }

    static Disguise DisguiseOf(GameObject go)
    {
        if (go == null) return null;
        foreach (var (_, x) in disguises)
            if (x.Pc != null && x.Pc.gameObject.Pointer == go.Pointer) return x;
        return null;
    }

    // Étiquette du monde d'un joueur (même masquée), sauf celle d'un joueur déguisé (ses textes sont les nôtres)
    static NameTag2 TagOfLobby(LobbyPlayer lp)
    {
        if (lp == null) return null;
        foreach (var tag in tags)
            if (tag != null && tag.playerController != null && tag.playerController.lobbyPlayer != null && tag.playerController.lobbyPlayer.Pointer == lp.Pointer
                && DisguiseOf(tag.playerController.gameObject) == null) return tag;
        return null;
    }

    // Nom du personnage tel que le jeu l'affiche : prénom nom, dans la couleur du joueur
    static string CharacterName(LobbyPlayer v)
    {
        var look = v.playerCustomization;
        string n = look != null ? $"{look.firstName} {look.lastName}".Trim() : v.username;
        var colors = LobbyManager.instance?.playerColors;
        return colors != null && v.playerColorIndex >= 0 && v.playerColorIndex < colors.Count
            ? $"<color=#{ColorUtility.ToHtmlStringRGB(colors[v.playerColorIndex])}>{n}</color>" : n;
    }

    static void ApplyTag(NameTag2 tag)
    {
        if (tag == null || tag.playerController == null) return;
        var d = DisguiseOf(tag.playerController.gameObject);
        if (d == null) return;
        if (loggedTags.Add(tag.Pointer))
            Plugin.L.LogInfo($"[USURP] étiquette : nom '{tag.nameText?.text}', nom Steam '{tag.steamNameText?.text}', poste '{tag.roleLabel?.text}'");
        if (d.Victim == null)
        {
            // Apparence inventée : nom inventé, ni nom Steam ni poste
            Set(tag.nameText, d.Name);
            Set(tag.steamNameText, "");
            Set(tag.roleLabel, "");
            return;
        }
        var v = d.Victim;
        var vt = TagOfLobby(v);
        bool vtShown = vt != null && vt.gameObject.activeInHierarchy && !string.IsNullOrEmpty(vt.nameText?.text);
        Set(tag.nameText, vtShown ? vt.nameText.text : CharacterName(v));
        Set(tag.steamNameText, vtShown && vt.steamNameText != null ? vt.steamNameText.text : v.username);
        // Poste : celui de la victime s'il est connu ici (même étiquette masquée), sinon rien plutôt que le vrai
        Set(tag.roleLabel, vt != null && !string.IsNullOrEmpty(vt.roleLabel?.text) ? vt.roleLabel.text : "");
    }

    static void ApplyHudTag(UI.Tabs.HUDTab.NameTags.NameTag tag)
    {
        if (tag?.lobbyPlayer == null || disguises.Count == 0) return;
        foreach (var (_, d) in disguises)
        {
            if (d.Pc?.lobbyPlayer == null || d.Pc.lobbyPlayer.Pointer != tag.lobbyPlayer.Pointer) continue;
            string text = d.Name;
            if (d.Victim != null)
            {
                text = d.Victim.username;
                foreach (var h in hudTags)
                    if (h != null && h.Pointer != tag.Pointer && h.lobbyPlayer != null && h.lobbyPlayer.Pointer == d.Victim.Pointer && !string.IsNullOrEmpty(h.nameText?.text))
                    { text = h.nameText.text; break; }
            }
            Set(tag.nameText, text);
            return;
        }
    }

    static void Set(TMPro.TMP_Text t, string text)
    {
        if (t == null || text == null) return;
        if (!savedTexts.ContainsKey(t.Pointer)) savedTexts[t.Pointer] = t.text;
        if (t.text != text) t.text = text;
    }

    [HarmonyPatch(typeof(NameTag2), nameof(NameTag2.Update))]
    [HarmonyPostfix]
    static void AfterNameTag2(NameTag2 __instance) { if (disguises.Count > 0) ApplyTag(__instance); }

    [HarmonyPatch(typeof(UI.Tabs.HUDTab.NameTags.NameTag), nameof(UI.Tabs.HUDTab.NameTags.NameTag.Update))]
    [HarmonyPostfix]
    static void AfterHudTag(UI.Tabs.HUDTab.NameTags.NameTag __instance) { if (disguises.Count > 0) ApplyHudTag(__instance); }

    static void RestoreNameTags(Player.PlayerController pc)
    {
        foreach (var tag in Object.FindObjectsOfType<NameTag2>(true))
            if (tag?.playerController != null && tag.playerController.gameObject.Pointer == pc.gameObject.Pointer)
            { Restore(tag.nameText); Restore(tag.steamNameText); Restore(tag.roleLabel); loggedTags.Remove(tag.Pointer); }
        foreach (var tag in Object.FindObjectsOfType<UI.Tabs.HUDTab.NameTags.NameTag>(true))
            if (tag?.lobbyPlayer != null && pc.lobbyPlayer != null && tag.lobbyPlayer.Pointer == pc.lobbyPlayer.Pointer) Restore(tag.nameText);
    }

    static void Restore(TMPro.TMP_Text t)
    {
        if (t != null && savedTexts.Remove(t.Pointer, out var old)) t.text = old;
    }

    // ===================== Client de l'Usurpateur =====================
    static bool mine, disguisedLocal, inMeeting;
    static int memorizedIdx = -1, memorizedSeed = -1, memorizedPreset = -1;
    static string memorizedName, disguisedAs;
    static float cooldownLeft, holdStart = -1f, meetingDropAt = -1f, stillSince;
    static Vector3 lastPos;
    static TextMeshProUGUI banner;

    static void ClientReset()
    {
        mine = disguisedLocal = inMeeting = false;
        memorizedIdx = memorizedSeed = memorizedPreset = -1;
        memorizedName = disguisedAs = null;
        cooldownLeft = 0f; holdStart = meetingDropAt = -1f;
        foreach (var (_, d) in disguises) if (d.Pc != null) RestoreNameTags(d.Pc);
        disguises.Clear();
        savedTexts.Clear();
        ShowBanner(null);
        RoleKey.Bar(0f, 0f);
    }

    static void OnLocalDisguised(string name, int colorIndex)
    {
        disguisedLocal = true;
        disguisedAs = colorIndex >= 0 ? Colored(name, colorIndex) : name;
    }

    static void OnLocalRestored()
    {
        if (disguisedLocal) cooldownLeft = Cooldown;   // le délai part quand le costume est enlevé (lui, ou la réunion)
        disguisedLocal = false;
        disguisedAs = null;
        meetingDropAt = -1f;
    }

    static string Colored(string name, int colorIndex)
    {
        var colors = LobbyManager.instance?.playerColors;
        if (colors == null || colorIndex < 0 || colorIndex >= colors.Count) return name;
        return $"<color=#{ColorUtility.ToHtmlStringRGB(colors[colorIndex])}>{name}</color>";
    }

    static Interactable LocalInteractable()
    {
        foreach (var p in Object.FindObjectsOfType<PlayerInteractable>())
            if (p.netIdentity != null && p.netIdentity == NetworkClient.localPlayer) return p;
        return null;
    }

    // Appelé par DebugMenu.Update chez tous. Touche E maintenue 2 s : en visant un joueur à 1 m -> mémoriser son
    // apparence (immobile) ; sinon -> se déguiser / se changer.
    internal static void Tick()
    {
        ApplyNameTags();
        if (!mine) { ShowBanner(null); RoleKey.Bar(0f, 0f); return; }

        var me = NetworkClient.localPlayer;
        if (me != null)
        {
            var pos = me.transform.position;
            if ((pos - lastPos).sqrMagnitude > 0.0004f) stillSince = Time.time;
            lastPos = pos;
        }
        if (!inMeeting && cooldownLeft > 0f) cooldownLeft = Mathf.Max(0f, cooldownLeft - Time.deltaTime);

        aimed = RoleKey.AimedPlayer(MemorizeDistance);
        float progress = 0f;
        aimedDummy = aimed == null && RoleKey.AimedDummy(MemorizeDistance);
        IntPtr mode = aimed != null ? aimed.Pointer : aimedDummy ? new IntPtr(-1) : IntPtr.Zero;   // cible de l'appui (Zero : déguisement)
        bool canAct = aimed != null || aimedDummy || disguisedLocal || (cooldownLeft <= 0f && Memorized && !inMeeting);
        if (RoleKey.Held && !canAct)
            RoleKey.Trace($"Usurpateur : rien à faire (mémorisé {Memorized}, délai {cooldownLeft:0}, réunion {inMeeting})");
        if (RoleKey.Held && canAct)
        {
            var here = me != null ? me.transform.position : Vector3.zero;
            if (holdStart < 0f || holdMode != mode) { holdStart = Time.time; holdMode = mode; holdPos = here; }
            // Mémoriser : sans bouger (plus de 15 cm depuis le début de l'appui : on recommence)
            if ((aimed != null || aimedDummy) && (here - holdPos).sqrMagnitude > 0.0225f) { holdStart = Time.time; holdPos = here; }
            progress = Mathf.Clamp01((Time.time - holdStart) / HoldSeconds);
            if (progress >= 1f)
            {
                holdStart = -1f; progress = 0f;
                if (aimed != null) Memorize(aimed);
                else if (aimedDummy) MemorizeDummy();
                else Act();
            }
        }
        else holdStart = -1f;

        RoleKey.Bar(progress, Screen.height * 0.09f - 8f);
        ShowBanner(BannerText());
    }

    static Player.PlayerController aimed;
    static bool aimedDummy;
    static IntPtr holdMode;
    static Vector3 holdPos;

    static bool Memorized => memorizedIdx >= 0 || memorizedSeed >= 0 || memorizedPreset >= 0;

    // Mannequin de test (Debug) : un autre de tes personnages enregistrés (à chaque fois le suivant), sinon une
    // apparence au hasard
    static void MemorizeDummy()
    {
        int current = CurrentPreset();
        int start = memorizedPreset >= 0 ? memorizedPreset : current;
        for (int k = 1; k <= MaxPresets; k++)
        {
            int n = (start + k) % MaxPresets;
            if (n == current) continue;
            var name = PresetName(PresetJson(n));
            if (string.IsNullOrEmpty(name)) continue;
            memorizedPreset = n; memorizedIdx = memorizedSeed = -1;
            memorizedName = name;
            Hud.Show(T($"Apparence de {memorizedName} mémorisée (mannequin).", $"{memorizedName}'s look memorized (dummy)."), 4f);
            Plugin.L.LogInfo($"[USURP] mannequin : personnage enregistré n°{n} ({name})");
            return;
        }
        memorizedSeed = new System.Random().Next(Seeds);
        memorizedIdx = memorizedPreset = -1;
        memorizedName = T("le mannequin (apparence au hasard)", "the dummy (random look)");
        Hud.Show(T($"Apparence de {memorizedName} mémorisée.", $"{memorizedName}'s look memorized."), 4f);
    }

    static void Act()
    {
        var via = LocalInteractable();
        if (via == null) return;
        if (disguisedLocal) NetSignal.SendToServer(via, ReqRestore);
        else if (memorizedIdx >= 0) NetSignal.SendToServer(via, ReqLookBase - memorizedIdx);
        else if (memorizedPreset >= 0) NetSignal.SendToServer(via, ReqPresetBase - memorizedPreset);
        else if (memorizedSeed >= 0) NetSignal.SendToServer(via, ReqRandomBase - memorizedSeed);
    }

    static string BannerText()
    {
        if (aimedDummy) return T("Maintiens E pendant 2s, sans bouger, pour mémoriser le mannequin\n", "Hold E for 2s, standing still, to memorize the dummy\n") + BaseText();
        string aim = aimed?.lobbyPlayer != null
            ? T($"Maintiens E pendant 2s, sans bouger, pour mémoriser {Colored(aimed.lobbyPlayer.username, aimed.lobbyPlayer.playerColorIndex)}\n",
                $"Hold E for 2s, standing still, to memorize {Colored(aimed.lobbyPlayer.username, aimed.lobbyPlayer.playerColorIndex)}\n")
            : "";
        return aim + BaseText();
    }

    static string BaseText()
    {
        if (disguisedLocal)
        {
            string s = $"{RoleKit.GradientText(T("Déguisé en", "Disguised as"))} {disguisedAs}\n";
            if (meetingDropAt > 0f)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(meetingDropAt - Time.time));
                return s + $"<color=#FF4040>{T($"Réunion ! Ton costume tombe dans {left} s : change-toi !", $"Meeting! Your costume drops in {left} s: change now!")}</color>\n" +
                       T("Maintiens E pendant 2s pour te changer", "Hold E for 2s to change back");
            }
            return s + T("Maintiens E pendant 2s pour te changer", "Hold E for 2s to change back");
        }
        if (inMeeting && cooldownLeft > 0f) return T($"Déguisement disponible dans {Clock(cooldownLeft)} (en pause pendant la réunion)", $"Disguise available in {Clock(cooldownLeft)} (paused during the meeting)");
        if (cooldownLeft > 0f) return T($"Déguisement disponible dans {Clock(cooldownLeft)}", $"Disguise available in {Clock(cooldownLeft)}");
        if (memorizedName == null) return T("Mémorise l'apparence d'un joueur : vise-le à 1 m et maintiens E pendant 2s sans bouger", "Memorize a player's look: aim at them within 1 m and hold E for 2s standing still");
        if (inMeeting) return T($"Apparence mémorisée : {memorizedName}", $"Memorized look: {memorizedName}");
        return T($"Apparence mémorisée : {memorizedName}\nMaintiens E pendant 2s pour te déguiser", $"Memorized look: {memorizedName}\nHold E for 2s to disguise");
    }

    static string Clock(float s) { int t = Mathf.CeilToInt(s); return $"{t / 60}:{t % 60:00}"; }

    // Bandeau en bas de l'écran, dans la police du jeu, qui "respire" tant qu'il est déguisé
    static void ShowBanner(string text)
    {
        if (text == null)
        {
            if (banner != null) banner.gameObject.SetActive(false);
            return;
        }
        if (banner == null)
        {
            banner = GameUi.NewText("DaleMod_Usurpateur");
            var rt = banner.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, Screen.height * 0.09f);
            rt.sizeDelta = new Vector2(Screen.width * 0.6f, 140f);
            banner.alignment = TextAlignmentOptions.Bottom;
        }
        GameUi.Style(banner);
        banner.fontSize = Mathf.Max(18f, Screen.height / 42f);
        banner.text = text;
        banner.gameObject.SetActive(true);
        float a = disguisedLocal ? 0.7f + 0.3f * Mathf.Sin(Time.time * 2.5f) : 0.85f;
        banner.color = new Color(1f, 1f, 1f, a);
    }

    // ===================== Mémoriser (touche E, chez l'Usurpateur seulement) =====================
    static void Memorize(Player.PlayerController target)
    {
        var lp = target?.lobbyPlayer;
        int idx = lp == null ? -1 : Players().FindIndex(p => p.Pointer == lp.Pointer);
        if (idx < 0 || idx >= MaxPlayers) return;
        memorizedIdx = idx;
        memorizedSeed = memorizedPreset = -1;
        memorizedName = Colored(lp.username, lp.playerColorIndex);
        Hud.Show(T($"Apparence de {memorizedName} mémorisée.", $"{memorizedName}'s look memorized."), 4f);
    }

    // Debug (F7) : mémorise une apparence au hasard pour tester seul
    internal static string DebugMemorizeRandom()
    {
        if (!mine) return "Il faut d'abord être Usurpateur";
        memorizedSeed = new System.Random().Next(Seeds);
        memorizedIdx = memorizedPreset = -1;
        memorizedName = T("une apparence au hasard", "a random look");
        return "Apparence au hasard mémorisée : maintiens E 2 s";
    }

    // ===================== Carte et aide =====================
    static void RoleCard(ShowRoleTab t)
    {
        if (t.roleText != null) t.roleText.text = T("Vous êtes l'Usurpateur !", "You are the Disguiser!");
        if (t.roleDescriptionText != null)
            t.roleDescriptionText.text = T(
                "Fainéant. Mémorise l'apparence d'un joueur, puis prends son visage, son nom et sa couleur pour le faire accuser. Maintiens F1 pour l'aide.",
                "Slacker. Memorize a player's look, then take their face, name and colour to get them blamed. Hold F1 for help.");
    }

    static string HelpText()
    {
        string title = $"<size=170%>{RoleKit.GradientText(T("USURPATEUR", "DISGUISER"))}</size>\n\n";
        return title + T(
            "Tu es fainéant et tu gagnes avec les fainéants.\n" +
            "<b>Mémoriser</b> : vise un joueur à 1 m maximum et maintiens E pendant 2s, sans bouger.\n" +
            "<b>Se déguiser / se changer</b> : sans viser de joueur, maintiens E pendant 2s. Tu prends son apparence, son nom et sa couleur.\n" +
            "Après chaque déguisement, attends 2 min (comptées dès que tu enlèves le costume, en pause pendant les réunions).\n" +
            "<b>Réunion</b> : ton costume tombe tout seul à la fin du compte à rebours. Change-toi avant, à l'abri des regards !\n" +
            "Attention : ta voix ne change pas, et ta victime peut croiser ton double.",
            "You're a slacker and win with the slackers.\n" +
            "<b>Memorize</b>: aim at a player within 1 m and hold E for 2s, standing still.\n" +
            "<b>Disguise / change back</b>: not aiming at a player, hold E for 2s. You take their look, name and colour.\n" +
            "After each disguise, wait 2 min (counted from when you take the costume off, paused during meetings).\n" +
            "<b>Meeting</b>: your costume drops on its own when the countdown ends. Change before that, out of sight!\n" +
            "Careful: your voice doesn't change, and your victim may run into your double.");
    }
}
