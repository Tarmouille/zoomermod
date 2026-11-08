using System;
using System.Collections.Generic;
using GameManagement;
using GameManagement.StateMachine;
using HarmonyLib;
using Mirror;
using Player.Lobby;
using Props.Scripts;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace DaleMod;

// Un rôle bonus ZoomerMod (sous-rôle caché ajouté par-dessus le rôle du jeu : Jester, Syndicaliste…).
// Chaque rôle remplit ces champs ; RoleKit fait le reste (tirage, réglage du lobby, carte de rôle, aide F1, écran de fin).
sealed class BonusRole
{
    public string Id;               // "jester", "syndic" : clés de traduction "dalemod.settings.<Id>.*"
    public string Name;             // nom affiché (écran de fin, log, menu F7)
    public string SettingKey;       // clé du réglage du lobby
    public Color Color;
    public bool TeamGradient;       // dégradé du rouge fainéant au bleu spécialiste du jeu (carte, intro, textes) au lieu de Color
    public float SignalBase;        // signaux de RoleKit : identité = base, gagné = base - 1, raté = base - 2

    // --- Serveur ---
    public Func<List<LobbyPlayer>, HashSet<uint>, bool, LobbyPlayer> Pick;   // (joueurs, déjà pris, forcé) -> élu ou null
    public Action<LobbyPlayer, GameManager> Assign;
    public Action<GameManager> AfterDraw;   // après le tirage de tous les rôles (ex. cible du Rival, qui ne peut pas être le Jester)
    public Action ServerReveal;             // fin de partie, avant l'envoi du résultat (infos en plus du nom du titulaire)
    public Action<GameManager> ServerTick;
    public Action ServerReset;
    public Func<bool?> ServerOutcome;   // null : pas de titulaire cette partie ; sinon gagné / raté (envoyé à la fin)
    public Func<uint> ServerHolder;     // netId du LobbyPlayer titulaire (0 : aucun)

    // --- Client ---
    public Action ClientReset;
    public Func<bool> LocalIsHolder;            // le joueur local a ce rôle (carte de rôle, intro)
    public Func<bool> LocalHasHelp;             // aide F1 + indication sous la pièce (titulaire, ou initié)
    public Func<string> HelpText;
    public Action<ShowRoleTab> RoleCard;        // textes de la carte de rôle (la teinte est faite par RoleKit)
    public Action<string> OnIdentity;           // chez tous : nom du titulaire reçu (le rôle décide s'il affiche)
    public Action<ResultTab, bool> ResultExtra; // écran de fin : effets en plus de la ligne (ex. titre du Jester gagnant)
    public Action<ResultTab, PlayerRole> LocalResult;   // écran de fin du titulaire seul (camp gagnant donné), si ni Jester ni Rival n'ont gagné
    public Func<bool, string> ResultLine;       // (gagné) -> texte de la ligne de l'écran de fin, {0} = nom

    // État client commun (rempli par les signaux de RoleKit)
    public string HolderName;
    public bool? Outcome;
}

static class RoleKit
{
    internal const int MinPlayers = 5;
    internal static readonly List<BonusRole> Roles = new();
    internal static string T(string fr, string en) => CustomTaskPatches.Text(fr, en);

    // ===================== Menu F7 : rôles bonus au prochain tirage =====================
    internal enum DrawMode { Normal, Random, All, Only }
    internal static DrawMode Mode = DrawMode.Normal;
    internal static int OnlyIndex;   // rôle choisi en mode Only
    internal static string ModeLabel() => Mode switch
    {
        DrawMode.Random => "Aléatoire (1, même à moins de 5)",
        DrawMode.All => "Tous",
        DrawMode.Only => Roles.Count > 0 ? Roles[OnlyIndex % Roles.Count].Name : "?",
        _ => "Normal (règles du jeu)",
    };
    internal static void NextMode()
    {
        if (Mode == DrawMode.Only && OnlyIndex + 1 < Roles.Count) { OnlyIndex++; return; }
        Mode = Mode switch { DrawMode.Normal => DrawMode.Random, DrawMode.Random => DrawMode.All, DrawMode.All => DrawMode.Only, _ => DrawMode.Normal };
        OnlyIndex = 0;
    }

    internal static void Add(BonusRole r)
    {
        Roles.Add(r);
        NetSignal.On(r.SignalBase, it =>
        {
            r.HolderName = NameOf(it) ?? r.HolderName;
            try { r.OnIdentity?.Invoke(r.HolderName); } catch (Exception e) { Plugin.L.LogError($"[ROLES] {r.Name} : {e}"); }
            ApplyResult();
        });
        NetSignal.On(r.SignalBase - 1, _ => { r.Outcome = true; Plugin.L.LogInfo("[ROLES] résultat reçu"); ApplyResult(); });
        NetSignal.On(r.SignalBase - 2, _ => { r.Outcome = false; Plugin.L.LogInfo("[ROLES] résultat reçu"); ApplyResult(); });
    }

    // ===================== Serveur =====================
    // Identité du titulaire à tous (rien n'est affiché ni logué avec le nom ; chaque rôle décide qui en profite) :
    // envoyée par son PlayerInteractable, tant qu'il existe (un joueur viré devient spectateur)
    internal static void Announce(BonusRole r, LobbyPlayer lp)
    {
        var pi = NetSignal.PlayerInteractableOf(PlayerIdOf(lp));
        if (pi != null) NetSignal.Send(pi, r.SignalBase);
    }

    // Résultat à tous : par un objet de la carte qui existe toujours (le tableau blanc), pas par le joueur
    internal static void SendOutcome(BonusRole r, bool won)
    {
        Interactable via = WhiteBoardController.instance;
        if (via == null) via = Object.FindObjectOfType<Interactable>();
        if (via != null) NetSignal.Send(via, won ? r.SignalBase - 1 : r.SignalBase - 2);
    }

    static bool drawn, revealed;

    static void ServerTick(GameManager gm)
    {
        Meetings.ServerTick(gm);
        foreach (var r in Roles) try { r.ServerTick?.Invoke(gm); } catch (Exception e) { Plugin.L.LogError($"[ROLES] {r.Name} : {e}"); }
        if (gm.IsState(GameStates.WaitingForPlayerConnections))
        {
            if (drawn || revealed) { drawn = revealed = false; foreach (var r in Roles) r.ServerReset?.Invoke(); }
            return;
        }
        // Tirage dès que le jeu a distribué les rôles (pour la carte de rôle du début), au plus tard au passage en jeu
        if (!drawn && (gm.IsState(GameStates.InGame) || (gm.IsState(GameStates.StartGame) && RolesAssigned())))
        {
            drawn = true;
            try { Draw(gm); } catch (Exception e) { Plugin.L.LogError("[ROLES] tirage : " + e); }
        }
        if (gm.IsState(GameStates.GameFinished) && !revealed)
        {
            revealed = true;
            foreach (var r in Roles)
            {
                var outcome = r.ServerOutcome?.Invoke();
                if (outcome == null) continue;
                var lp = LobbyById(r.ServerHolder());
                if (lp != null) Announce(r, lp);   // si le titulaire est encore là, son nom repart (filet de sécurité)
                try { r.ServerReveal?.Invoke(); } catch (Exception e) { Plugin.L.LogError($"[ROLES] {r.Name} : {e}"); }
                SendOutcome(r, outcome.Value);
                Plugin.L.LogInfo($"[ROLES] fin de partie : {r.Name} {(outcome.Value ? "gagné" : "raté")}");
            }
        }
    }

    static bool RolesAssigned()
    {
        bool any = false;
        foreach (var lp in Object.FindObjectsOfType<LobbyPlayer>())
        {
            if (lp == null) continue;
            if (lp.playerRole == PlayerRole.None) return false;
            any = true;
        }
        return any;
    }

    // Places : 1 + (joueurs - 5) / 2 à partir de 5 joueurs (5-6 : 1, 7-8 : 2, 9-10 : 3…), chaque rôle au plus une fois
    static void Draw(GameManager gm)
    {
        var players = new List<LobbyPlayer>();
        foreach (var lp in Object.FindObjectsOfType<LobbyPlayer>())
            if (lp != null && lp.playerRole != PlayerRole.None) players.Add(lp);
        int count = players.Count;
        bool forced = Mode != DrawMode.Normal;
        int slots = Mode switch
        {
            DrawMode.Random => 1,
            DrawMode.All => Roles.Count,
            DrawMode.Only => 1,
            _ => count < MinPlayers ? 0 : 1 + (count - MinPlayers) / 2,
        };
        var order = new List<BonusRole>(Roles);
        var rng = new System.Random();
        for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        if (Mode == DrawMode.Only && Roles.Count > 0) order = new List<BonusRole> { Roles[OnlyIndex % Roles.Count] };

        var taken = new HashSet<uint>();
        var drawnNames = new List<string>();
        foreach (var r in order)
        {
            if (drawnNames.Count >= slots) break;
            if (!forced && !SettingEnabled(r)) continue;
            var lp = r.Pick(players, taken, forced);
            if (lp == null) continue;
            taken.Add(lp.netId);
            r.Assign(lp, gm);
            Announce(r, lp);
            drawnNames.Add(r.Name);
        }
        foreach (var r in Roles) try { r.AfterDraw?.Invoke(gm); } catch (Exception e) { Plugin.L.LogError($"[ROLES] {r.Name} : {e}"); }
        Plugin.L.LogInfo($"[ROLES] {count} joueurs, {slots} place(s) ({Mode}) : {(drawnNames.Count == 0 ? "aucun rôle bonus" : string.Join(", ", drawnNames))}");
    }

    // Candidat au hasard parmi les joueurs de ce rôle du jeu, sans sous-rôle de préférence, pas déjà pris
    internal static LobbyPlayer PickOf(List<LobbyPlayer> players, HashSet<uint> taken, PlayerRole role)
    {
        var plain = new List<LobbyPlayer>();
        var any = new List<LobbyPlayer>();
        foreach (var lp in players)
        {
            if (lp.playerRole != role || lp.isFired || taken.Contains(lp.netId)) continue;
            any.Add(lp);
            if (lp.subRole == SubRole.None) plain.Add(lp);
        }
        var c = plain.Count > 0 ? plain : any;
        return c.Count == 0 ? null : c[new System.Random().Next(c.Count)];
    }

    // Joueurs encore en jeu (non virés) et nombre de fainéants parmi eux
    internal static (int players, int slackers) InGameCounts()
    {
        int n = 0, s = 0;
        foreach (var lp in Object.FindObjectsOfType<LobbyPlayer>())
        {
            if (lp == null || lp.playerRole == PlayerRole.None || lp.isFired) continue;
            n++;
            if (lp.playerRole == PlayerRole.Slacker) s++;
        }
        return (n, s);
    }

    // ===================== Helpers joueurs =====================
    internal static LobbyPlayer LobbyOf(NetworkIdentity id) =>
        id == null ? null : id.GetComponent<LobbyPlayer>() ?? id.GetComponent<Player.PlayerController>()?.lobbyPlayer;

    internal static LobbyPlayer LobbyById(uint netId)
    {
        if (netId == 0) return null;
        foreach (var lp in Object.FindObjectsOfType<LobbyPlayer>()) if (lp.netId == netId) return lp;
        return null;
    }

    internal static NetworkIdentity PlayerIdOf(LobbyPlayer lp)
    {
        if (lp == null) return null;
        foreach (var pc in Object.FindObjectsOfType<Player.PlayerController>())
            if (pc.lobbyPlayer != null && pc.lobbyPlayer.Pointer == lp.Pointer) return pc.netIdentity;
        return null;
    }

    // Nom du joueur derrière l'objet qui a porté le signal (son PlayerInteractable, même identité réseau que son
    // PlayerController, pas forcément sur le même GameObject)
    internal static string NameOf(Interactable it)
    {
        if (it == null) return null;
        var pc = it.GetComponent<Player.PlayerController>() ?? it.GetComponentInParent<Player.PlayerController>();
        if (pc == null && it.netIdentity != null)
            foreach (var x in Object.FindObjectsOfType<Player.PlayerController>())
                if (x.netIdentity != null && x.netIdentity.Pointer == it.netIdentity.Pointer) { pc = x; break; }
        var name = pc?.lobbyPlayer?.username;
        return string.IsNullOrEmpty(name) ? null : name;
    }

    internal static LobbyPlayer LocalLobby() => LobbyManager.instance?.GetLocalPlayer();

    // ===================== Réglages du lobby (onglet Rôles) =====================
    static float nextSettingCheck;

    internal static string Localize(string key)
    {
        foreach (var r in Roles)
        {
            if (key == $"dalemod.settings.{r.Id}.label") return $"{r.Name} (ZoomerMod)";
            if (key == $"dalemod.settings.{r.Id}.sub") return r.Name;
            if (key == $"dalemod.settings.{r.Id}.hint") return SettingHint(r);
        }
        return null;
    }

    static string SettingHint(BonusRole r) => r.Id switch
    {
        "jester" => T("Si activé, un spécialiste peut devenir en secret le Jester : il gagne seul s'il se fait virer par le manager.",
                      "If enabled, a specialist may secretly become the Jester: they win alone if they get fired by the manager."),
        "syndic" => T("Si activé, un fainéant peut devenir le Syndicaliste (connu des fainéants) : il doit faire renverser le manager par un vote.",
                      "If enabled, a slacker may become the Union Rep (known to slackers): they must get the manager voted out."),
        "rival" => T("Si activé, un joueur (spécialiste ou fainéant) peut devenir en secret le Rival : neutre, il gagne si sa cible (un spécialiste) se fait virer.",
                     "If enabled, a player (specialist or slacker) may secretly become the Rival: neutral, they win if their target (a specialist) gets fired."),
        "pistonne" => T("Si activé (4 licenciements ou plus), un spécialiste ou fainéant peut devenir en secret le Pistonné : son premier renvoi échoue ; le 2e coûte un licenciement de plus (spécialiste) ou rapporte 10 % de productivité (fainéant).",
                        "If enabled (4+ firings), a specialist or slacker may secretly become the Nepo Baby: their first firing fails; the 2nd costs one more firing (specialist) or gives 10% productivity (slacker)."),
        "chanteur" => T("Si activé, un fainéant peut devenir en secret le Maître chanteur : il choisit un joueur qui ne pourra ni parler ni écrire pendant la réunion suivante.",
                        "If enabled, a slacker may secretly become the Blackmailer: they pick a player who can neither speak nor write during the next meeting."),
        "usurpateur" => T("Si activé, un fainéant peut devenir en secret l'Usurpateur : il mémorise l'apparence d'un joueur et peut la prendre (nom et couleur compris), avec 2 min d'attente après chaque déguisement.",
                          "If enabled, a slacker may secretly become the Disguiser: they memorize a player's look and can take it (name and colour included), with a 2 min wait after each disguise."),
        "costkiller" => T("Si activé, un spécialiste peut devenir en secret le Cost Killer : une fois par partie, après la première réunion, il vire lui-même un joueur. Un spécialiste viré par erreur l'emporte avec lui.",
                          "If enabled, a specialist may secretly become the Cost Killer: once per game, after the first meeting, they fire a player themselves. Firing a specialist by mistake takes them down too."),
        _ => r.Name,
    } + T(" Rôles bonus : 1 à 5-6 joueurs, 2 à 7-8, 3 à 9-10…", " Bonus roles: 1 at 5-6 players, 2 at 7-8, 3 at 9-10…");

    internal static Setting FindSetting(string key)
    {
        var m = GameRulesSettingsManager.instance;
        if (m?.categories == null) return null;
        foreach (var c in m.categories)
            if (c?.Settings != null)
                foreach (var st in c.Settings) if (st != null && st.Key == key) return st;
        return null;
    }

    static void EnsureSettings()
    {
        if (Time.unscaledTime < nextSettingCheck) return;
        nextSettingCheck = Time.unscaledTime + 2f;
        var m = GameRulesSettingsManager.instance;
        if (m?.categories == null) return;
        var model = FindSetting("Use Analyst");
        SettingCategory roles = null;
        foreach (var c in m.categories) if (c != null && c.CategoryName == "lobbySettings.roles.title") roles = c;
        if (model == null || roles?.Settings == null) return;
        foreach (var r in Roles)
        {
            if (FindSetting(r.SettingKey) != null) continue;
            try
            {
                roles.Settings.Add(new Setting
                {
                    Key = r.SettingKey,
                    label = $"dalemod.settings.{r.Id}.label",
                    hint = $"dalemod.settings.{r.Id}.hint",
                    subCategory = $"dalemod.settings.{r.Id}.sub",
                    subCategoryColor = r.Color,
                    type = model.type,
                    alternatives = model.alternatives,   // Désactivé / Activé
                    Value = 1f,
                    roundTo = model.roundTo,
                    additionalInfo = model.additionalInfo,
                    presetName = model.presetName,
                });
                Plugin.L.LogInfo($"[ROLES] réglage '{r.Name}' ajouté aux réglages du lobby (catégorie Rôles)");
            }
            catch (Exception e) { Plugin.L.LogWarning($"[ROLES] réglage du lobby {r.Name} : {e.Message}"); }
        }
    }

    // Activé par défaut : sans réglage trouvé, le rôle reste possible
    static bool SettingEnabled(BonusRole r)
    {
        var st = FindSetting(r.SettingKey);
        return st == null || st.Value >= 0.5f;
    }

    // ===================== Nouveau manager (vote de remplacement) =====================
    // Le jeu choisit un spécialiste ; un Jester ou un Rival en cours de mission ne l'est qu'en dernier recours (sinon sa
    // mission, tirée au hasard, raterait d'office) : on prend un autre spécialiste au hasard s'il y en a un.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.FindNewManager))]
    [HarmonyPostfix]
    static void AfterFindNewManager(ref LobbyPlayer __result)
    {
        findNewManagerSeen = true;
        try
        {
            if (__result == null || !HoldsMission(__result.netId)) return;
            var others = new List<LobbyPlayer>();
            foreach (var lp in Object.FindObjectsOfType<LobbyPlayer>())
                if (lp != null && lp.playerRole == PlayerRole.Specialist && !lp.isFired && !HoldsMission(lp.netId)) others.Add(lp);
            if (others.Count == 0) { Plugin.L.LogInfo("[ROLES] nouveau manager : rôle bonus gardé (dernier recours)"); return; }
            __result = others[new System.Random().Next(others.Count)];
            Plugin.L.LogInfo("[ROLES] nouveau manager : rôle bonus évité, autre spécialiste choisi");   // jamais de nom
        }
        catch (Exception e) { Plugin.L.LogError("[ROLES] nouveau manager : " + e); }
    }

    // Contrôle : si FindNewManager était inliné par IL2CPP, le postfix ci-dessus ne passerait jamais
    static bool findNewManagerSeen;

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ReplaceManager))]
    [HarmonyPrefix]
    static void BeforeReplaceManager() => findNewManagerSeen = false;

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ReplaceManager))]
    [HarmonyPostfix]
    static void AfterReplaceManager()
    {
        if (!findNewManagerSeen) Plugin.L.LogWarning("[ROLES] ReplaceManager sans passer par FindNewManager : choix du nouveau manager non filtré");
    }

    static bool HoldsMission(uint netId) => Jester.IsJester(netId) || Rival.IsActive(netId);

    // ===================== Carte de rôle (début de partie) =====================
    [HarmonyPatch(typeof(ShowRoleTab), nameof(ShowRoleTab.UpdateTab))]
    [HarmonyPostfix]
    static void AfterRoleTab(ShowRoleTab __instance) => ApplyRoleTab(__instance);

    internal static void RefreshRoleTabs()
    {
        foreach (var tab in Object.FindObjectsOfType<ShowRoleTab>()) ApplyRoleTab(tab);
    }

    static void ApplyRoleTab(ShowRoleTab t)
    {
        if (t == null) return;
        LearnTeamColors(t);
        // La carte resservira à la partie suivante : dégradés retirés tant qu'un rôle ne les remet pas
        RemoveGradient(t.cardTopBar); RemoveGradient(t.cardBottomBar); RemoveGradient(t.backgroundImage); RemoveGradient(t.background);
        foreach (var r in Roles)
        {
            if (r.LocalIsHolder?.Invoke() != true) continue;
            try
            {
                r.RoleCard?.Invoke(t);
                if (r.TeamGradient)
                {
                    var (a, b) = TeamColors();
                    var mid = Color.Lerp(a, b, 0.5f);
                    if (t.cardTopBar != null) t.cardTopBar.color = mid;
                    if (t.cardBottomBar != null) t.cardBottomBar.color = mid;
                    Tint(t.backgroundImage, mid);
                    Tint(t.background, mid);
                    t.backgroundColor = WithAlpha(mid, t.backgroundColor.a);
                    ApplyGradient(t.cardTopBar, a, b); ApplyGradient(t.cardBottomBar, a, b);
                    ApplyGradient(t.backgroundImage, a, b); ApplyGradient(t.background, a, b);
                    if (t.roleText != null) t.roleText.text = GradientText(t.roleText.text, a, b);
                    return;
                }
                if (t.cardTopBar != null) t.cardTopBar.color = r.Color;
                if (t.cardBottomBar != null) t.cardBottomBar.color = r.Color;
                Tint(t.backgroundImage, r.Color);
                Tint(t.background, r.Color);
                t.backgroundColor = WithAlpha(r.Color, t.backgroundColor.a);
            }
            catch (Exception e) { Plugin.L.LogError($"[ROLES] carte de rôle {r.Name} : {e}"); }
            return;
        }
    }

    internal static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

    // ===================== Dégradé fainéant -> spécialiste (couleurs du jeu) =====================
    static Color slackerColor = new(0.85f, 0.2f, 0.2f), specialistColor = new(0.2f, 0.45f, 0.9f);
    static bool teamColorsKnown;

    static void LearnTeamColors(ShowRoleTab t)
    {
        if (teamColorsKnown || t == null) return;
        teamColorsKnown = true;
        slackerColor = WithAlpha(t.slackerColor, 1f);
        specialistColor = WithAlpha(t.specialistColor, 1f);
        Plugin.L.LogInfo($"[ROLES] couleurs du jeu : fainéant #{ColorUtility.ToHtmlStringRGB(slackerColor)}, spécialiste #{ColorUtility.ToHtmlStringRGB(specialistColor)}");
    }

    internal static (Color slacker, Color specialist) TeamColors()
    {
        if (!teamColorsKnown)
            foreach (var t in Resources.FindObjectsOfTypeAll<ShowRoleTab>()) { LearnTeamColors(t); break; }
        return (slackerColor, specialistColor);
    }

    // Texte coloré lettre par lettre (balises de texte riche laissées telles quelles)
    internal static string GradientText(string s, Color a, Color b)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var plain = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>", "");
        int n = 0;
        foreach (var ch in plain) if (!char.IsWhiteSpace(ch)) n++;
        var sb = new System.Text.StringBuilder();
        int k = 0;
        foreach (var ch in plain)
        {
            if (char.IsWhiteSpace(ch)) { sb.Append(ch); continue; }
            var c = Color.Lerp(a, b, n <= 1 ? 0f : k++ / (float)(n - 1));
            sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{ch}</color>");
        }
        return sb.ToString();
    }

    internal static string GradientText(string s) { var (a, b) = TeamColors(); return GradientText(s, a, b); }

    const string GradientName = "DaleMod_Gradient";
    static readonly Dictionary<(Color, Color), Texture2D> gradientTextures = new();

    static Texture2D GradientTexture(Color a, Color b)
    {
        if (gradientTextures.TryGetValue((a, b), out var tex) && tex != null) return tex;
        tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int x = 0; x < 64; x++) tex.SetPixel(x, 0, Color.Lerp(a, b, x / 63f));
        tex.Apply();
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        gradientTextures[(a, b)] = tex;
        return tex;
    }

    // Dégradé horizontal par-dessus une image du jeu, découpé à sa forme (Mask sur l'image, qui reste invisible)
    internal static void ApplyGradient(UnityEngine.UI.Image img, Color a, Color b)
    {
        if (img == null) return;
        var tr = img.transform.Find(GradientName);
        UnityEngine.UI.RawImage raw;
        if (tr == null)
        {
            var go = new GameObject(GradientName);
            go.transform.SetParent(img.transform, false);
            go.transform.SetAsFirstSibling();   // sous les textes de la carte
            raw = go.AddComponent<UnityEngine.UI.RawImage>();
            raw.raycastTarget = false;
            var rt = raw.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        else raw = tr.GetComponent<UnityEngine.UI.RawImage>();
        raw.texture = GradientTexture(a, b);
        raw.color = new Color(1f, 1f, 1f, img.color.a);
        var mask = img.GetComponent<UnityEngine.UI.Mask>() ?? img.gameObject.AddComponent<UnityEngine.UI.Mask>();
        mask.showMaskGraphic = false;
        mask.enabled = true;
    }

    static void RemoveGradient(UnityEngine.UI.Image img)
    {
        if (img == null) return;
        var tr = img.transform.Find(GradientName);
        if (tr == null) return;
        Object.Destroy(tr.gameObject);
        var mask = img.GetComponent<UnityEngine.UI.Mask>();
        if (mask != null) Object.Destroy(mask);   // ajouté par ApplyGradient
    }
    static void Tint(UnityEngine.UI.Image img, Color c) { if (img != null) img.color = WithAlpha(c, img.color.a); }

    // Écran d'intro qui précède la carte de rôle : à la couleur du rôle du joueur local
    static void TintIntro()
    {
        foreach (var r in Roles)
        {
            if (r.LocalIsHolder?.Invoke() != true) continue;
            foreach (var intro in SceneCache.All<ShowRoleIntroTab>())
            {
                if (intro == null) continue;
                var c = r.Color;
                if (r.TeamGradient) { var (a, b) = TeamColors(); c = Color.Lerp(a, b, 0.5f); ApplyGradient(intro.background, a, b); }
                intro.backgroundColor = WithAlpha(c, intro.backgroundColor.a);
                Tint(intro.background, c);
            }
            return;
        }
    }

    // ===================== Écran de fin : tous les rôles bonus de la partie =====================
    // Finalizer (et pas Postfix) : s'applique même si l'écran de résultat du jeu lève une exception en route
    [HarmonyPatch(typeof(ResultTab), nameof(ResultTab.SetResult))]
    [HarmonyFinalizer]
    static void AfterSetResult(PlayerRole winner) { lastWinner = winner; ApplyResult(); }

    static PlayerRole lastWinner = PlayerRole.None;

    // Statistiques de fin du jeu : plantent chez certains invités (joueur absent de leur dictionnaire, sans doute après un
    // changement de rôle en cours de partie) et interrompent l'écran de résultat. Erreur neutralisée : stats non enregistrées.
    [HarmonyPatch(typeof(Stats.LobbyStatisticsManager), nameof(Stats.LobbyStatisticsManager.SaveToGameStats))]
    [HarmonyFinalizer]
    static Exception StatsSafe(Exception __exception)
    {
        if (__exception != null) Plugin.L.LogWarning("[ROLES] statistiques de fin non enregistrées : " + __exception.GetType().Name);
        return null;
    }

    // Écran de fin aux couleurs d'un rôle : barre de titre et fond (bleu ou rouge selon l'équipe), même transparence ;
    // l'animation d'ouverture de l'onglet repart de backgroundColor : on change les deux
    internal static void TintResultGradient(ResultTab rt)
    {
        var (a, b) = TeamColors();
        TintResult(rt, Color.Lerp(a, b, 0.5f));
        ApplyGradient(rt.tabBarColorImage, a, b);
        ApplyGradient(rt.background, a, b);
    }

    internal static void TintResult(ResultTab rt, Color c)
    {
        if (rt.tabBarColorImage != null) rt.tabBarColorImage.color = c;
        rt.backgroundColor = WithAlpha(c, rt.backgroundColor.a);
        if (rt.background != null) rt.background.color = WithAlpha(c, rt.background.color.a);
    }

    const string LinesMark ="\n​";   // début de nos lignes dans la description (pour les remplacer sans doublon)

    internal static void ApplyResult()
    {
        var rt = ResultTab.instance;
        if (rt == null) return;
        RemoveGradient(rt.tabBarColorImage); RemoveGradient(rt.background);
        try
        {
            var lines = new List<string>();
            foreach (var r in Roles)
            {
                if (r.HolderName == null || r.Outcome == null) continue;
                r.ResultExtra?.Invoke(rt, r.Outcome.Value);
                var line = string.Format(r.ResultLine(r.Outcome.Value), r.HolderName);
                lines.Add(r.TeamGradient ? GradientText(line) : $"<color=#{ColorUtility.ToHtmlStringRGB(r.Color)}>{line}</color>");
            }
            // Couleur perso du titulaire (Usurpateur, Syndicaliste, Pistonné), sauf si le Jester ou le Rival colorent tout
            if (lastWinner != PlayerRole.None && Jester.Role?.Outcome != true && Rival.Role?.Outcome != true)
                foreach (var r in Roles)
                    if (r.LocalIsHolder?.Invoke() == true) r.LocalResult?.Invoke(rt, lastWinner);
            if (lines.Count == 0 || rt.descriptionText == null) return;
            var text = rt.descriptionText.text ?? "";
            int i = text.IndexOf(LinesMark, StringComparison.Ordinal);
            if (i >= 0) text = text.Substring(0, i);
            rt.descriptionText.text = text + LinesMark + string.Join("\n", lines);
        }
        catch (Exception e) { Plugin.L.LogError("[ROLES] écran de fin : " + e); }
    }

    // ===================== Chaque frame (DebugMenu.Update) =====================
    static IntPtr gmPtr;

    internal static void Tick()
    {
        EnsureSettings();
        var gm = GameManager.instance;
        if (gm == null || !NetworkClient.active)   // menu principal (le GameManager peut survivre au retour au menu)
        {
            HideUi();
            // On vient de quitter la partie : rôles oubliés et textes du mod masqués (bâillon, bandeaux, aide…)
            if (gmPtr != IntPtr.Zero)
            {
                gmPtr = IntPtr.Zero;
                foreach (var r in Roles) { r.HolderName = null; r.Outcome = null; try { r.ClientReset?.Invoke(); } catch (Exception e) { Plugin.L.LogError("[ROLES] sortie : " + e); } }
                GameUi.HideAll();
                Plugin.L.LogInfo("[ROLES] partie quittée : textes du mod masqués");
            }
            return;
        }
        if (NetworkServer.active) ServerTick(gm);
        // Chez les clients, l'état de la partie (IsState) n'est pas fiable : seul le serveur fait tourner la machine
        // d'états. Nouvelle partie = nouveau GameManager (scène rechargée) : on oublie les rôles et les résultats.
        if (gm.Pointer != gmPtr)
        {
            gmPtr = gm.Pointer;
            foreach (var r in Roles) { r.HolderName = null; r.Outcome = null; r.ClientReset?.Invoke(); }
            lastWinner = PlayerRole.None;
            cardSeen = cardDone = false; waited = 0f;   // la file d'annonces est gardée (signal reçu pendant le chargement)
        }
        TintIntro();
        UpdatePending();
        UpdateHelp();
        UpdateHint();
    }

    // --- Annonces de rôle : attendent la fermeture de la carte de rôle du début de partie ---
    // Sinon le bandeau s'affiche pendant le chargement et disparaît sous la carte. Rôle donné en cours de partie
    // (carte déjà passée, ou jamais affichée) : tout de suite, ou après un délai de sécurité.
    const float CardTimeout = 30f;
    static readonly List<(string msg, float sec)> pending = new();
    static bool cardSeen, cardDone;
    static float waited;

    internal static void ShowAfterCard(string message, float seconds)
    {
        if (cardDone) { Hud.Show(message, seconds); return; }
        pending.Add((message, seconds));
    }

    static bool AnyOpened<T>() where T : UMUI.UiTab
    {
        foreach (var t in SceneCache.All<T>())
            if (t != null && t.isOpened && t.gameObject.activeInHierarchy) return true;
        return false;
    }

    static void UpdatePending()
    {
        bool card = AnyOpened<ShowRoleTab>();
        if (card) cardSeen = true;
        else if (cardSeen) cardDone = true;   // carte vue puis fermée
        bool busy = card || AnyOpened<ShowRoleIntroTab>();
        if (!busy) waited += Time.unscaledDeltaTime;
        if (pending.Count == 0) return;
        if (!cardDone && waited < CardTimeout) return;
        cardDone = true;
        var msgs = new List<string>();
        float sec = 0f;
        foreach (var (m, s) in pending) { msgs.Add(m); sec = Mathf.Max(sec, s); }
        pending.Clear();
        Hud.Show(string.Join("\n\n", msgs), sec);
    }

    // --- Aide (F1 maintenue) : celle du ou des rôles du joueur local ---
    static TextMeshProUGUI help;

    static void UpdateHelp()
    {
        var kb = Keyboard.current;
        var texts = new List<string>();
        if (kb != null && kb.f1Key.isPressed)
            foreach (var r in Roles) if (r.LocalHasHelp?.Invoke() == true) texts.Add(r.HelpText());
        if (texts.Count == 0) { if (help != null) help.gameObject.SetActive(false); return; }
        if (help == null)
        {
            help = GameUi.NewText("DaleMod_RoleHelp");
            var rt = help.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            help.alignment = TextAlignmentOptions.Center;
            help.color = Color.white;
        }
        GameUi.Style(help);
        help.rectTransform.sizeDelta = new Vector2(Screen.width * 0.55f, Screen.height * 0.75f);
        help.fontSize = Mathf.Max(18f, Screen.height / 42f);
        help.text = string.Join("\n\n\n", texts);
        help.gameObject.SetActive(true);
    }

    // --- Indication sous le nom de la pièce (HUD du jeu) ---
    static TextMeshProUGUI hint;

    static void UpdateHint()
    {
        bool any = false;
        foreach (var r in Roles) if (r.LocalHasHelp?.Invoke() == true) { any = true; break; }
        if (!any) { if (hint != null) hint.gameObject.SetActive(false); return; }
        if (hint == null)
        {
            var room = Object.FindObjectOfType<RoomInformation>()?.roomName;
            if (room == null) return;
            var go = new GameObject("DaleMod_RoleHint");
            go.transform.SetParent(room.transform, false);
            hint = go.AddComponent<TextMeshProUGUI>();
            hint.raycastTarget = false;
            hint.font = room.font;
            hint.fontSharedMaterial = room.fontSharedMaterial;
            hint.alignment = room.alignment;
            hint.enableWordWrapping = false;
            var rt = hint.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -2f);
            rt.sizeDelta = new Vector2(0f, room.fontSize * 1.2f);
            hint.fontSize = Mathf.Max(12f, room.fontSize * 0.5f);
            hint.color = new Color(room.color.r, room.color.g, room.color.b, room.color.a * 0.85f);
            hint.text = T("Appuie sur F1 pour l'aide de ton rôle", "Hold F1 for your role's help");
        }
        hint.gameObject.SetActive(true);
    }

    static void HideUi()
    {
        if (help != null) help.gameObject.SetActive(false);
        if (hint != null) hint.gameObject.SetActive(false);
    }
}
