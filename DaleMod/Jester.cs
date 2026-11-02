using System;
using GameManagement;
using GameManagement.StateMachine;
using HarmonyLib;
using Mirror;
using Player.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace DaleMod;

// Sous-rôle caché "Jester" : son but est de se faire VIRER par le manager.
// - Pour le jeu, c'est un Specialist normal (tâches bleues et rouges, vu comme spécialiste par tous). Personne ne sait
//   qui c'est, ni le manager ni l'hôte : seul le serveur le sait en interne, rien n'est affiché ni logué avec un nom.
// - Il a ceil(limite de licenciements / 2) "tours" : chaque licenciement prononcé (manager remplacé compris) en consomme un.
// - Viré à temps : écran de renvoi d'un spécialiste ("était l'un des meilleurs…"), il devient Janitor et a gagné.
//   Tours épuisés : il redevient un simple spécialiste (raté).
// - Fin de partie : Jester gagnant = seul vainqueur (écran de résultat rose, son nom) ; sinon son rôle est révélé.
// - Tirage par l'hôte au début de la partie, à partir de 5 joueurs ; forçable via le menu F7 (Debug).
// - Chez le Jester : aide en maintenant F1 (règles + licenciements restants), indication sous le nom de la pièce.
static class Jester
{
    const int MinPlayers = 5, MaxTurns = 40;
    // Signaux (NetSignal) : "tu es le Jester, N tours restants" = YouBase - N ; puis gagné / raté ; révélation finale.
    const float YouBase = -9100f, WonSignal = -9150f, FailedSignal = -9151f, RevealWon = -9160f, RevealLost = -9161f;
    static readonly Color Pink = new(1f, 0f, 1f);

    static string T(string fr, string en) => CustomTaskPatches.Text(fr, en);

    internal static void Register()
    {
        for (int n = 0; n <= MaxTurns; n++)
        {
            int turns = n;
            NetSignal.OnLocalPlayer(YouBase - n, () => OnYou(turns));
        }
        NetSignal.OnLocalPlayer(WonSignal, () =>
        {
            mine = true; status = Status.Won;
            Hud.Show(T("Viré ! Bien joué, Jester : tu as gagné. Tu continues en Janitor, motus jusqu'à la fin.",
                       "Fired! Well played, Jester: you won. You carry on as a Janitor, keep quiet until the end."), 6f);
        });
        NetSignal.OnLocalPlayer(FailedSignal, () =>
        {
            mine = true; status = Status.Failed; turnsLeft = 0;
            Hud.Show(T("Raté… plus de licenciements pour toi. Tu es maintenant un simple spécialiste.",
                       "Missed… no firings left for you. You're now a plain specialist."), 6f);
        });
        NetSignal.On(RevealWon, it => Reveal(it, true));
        NetSignal.On(RevealLost, it => Reveal(it, false));
    }

    // ===================== Serveur (hôte) =====================
    static uint jesterId;          // netId du LobbyPlayer du Jester (0 : pas de Jester)
    static int serverTurns;
    static bool serverWon, serverFailed, drawn, revealed;

    static void ResetServer() { jesterId = 0; serverTurns = 0; serverWon = serverFailed = drawn = revealed = false; }

    static void ServerTick(GameManager gm)
    {
        if (gm.IsState(GameStates.WaitingForPlayerConnections)) { if (drawn || jesterId != 0 || revealed) ResetServer(); return; }
        // Tirage dès que le jeu a distribué les rôles (début de partie), pour que la carte de rôle du Jester s'affiche ;
        // au plus tard au passage en jeu
        if (!drawn && (gm.IsState(GameStates.InGame) || (gm.IsState(GameStates.StartGame) && RolesAssigned())))
        {
            drawn = true;
            if (jesterId == 0) Draw(gm);
        }
        if (gm.IsState(GameStates.GameFinished) && !revealed)
        {
            revealed = true;
            if (jesterId == 0) return;
            var id = PlayerIdOf(LobbyById(jesterId));
            var pi = NetSignal.PlayerInteractableOf(id);
            if (pi != null) NetSignal.Send(pi, serverWon ? RevealWon : RevealLost);
            Plugin.L.LogInfo($"[JESTER] fin de partie : Jester {(serverWon ? "gagnant" : "perdant")}");
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

    static void Draw(GameManager gm)
    {
        if (!SettingEnabled()) { Plugin.L.LogInfo("[JESTER] désactivé dans les réglages du lobby"); return; }
        var players = Object.FindObjectsOfType<LobbyPlayer>();
        int count = 0;
        var candidates = new System.Collections.Generic.List<LobbyPlayer>();
        foreach (var lp in players)
        {
            if (lp == null || lp.playerRole == PlayerRole.None) continue;
            count++;
            if (lp.playerRole == PlayerRole.Specialist && lp.subRole == SubRole.None && !lp.isFired) candidates.Add(lp);
        }
        if (count < MinPlayers || candidates.Count == 0) { Plugin.L.LogInfo($"[JESTER] pas de Jester ({count} joueurs)"); return; }
        Make(candidates[new System.Random().Next(candidates.Count)], gm);
    }

    // Menu F7 (hôte) : rend ce joueur Jester, sans rien afficher
    internal static void Force(LobbyPlayer lp)
    {
        if (!NetworkServer.active || lp == null || GameManager.instance == null) return;
        drawn = true;
        Make(lp, GameManager.instance);
    }

    static void Make(LobbyPlayer lp, GameManager gm)
    {
        jesterId = lp.netId;
        serverTurns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(1, gm.fireLimit) / 2f), 1, MaxTurns);
        serverWon = serverFailed = false;
        NetSignal.SendToPlayer(PlayerIdOf(lp), YouBase - serverTurns);
        Plugin.L.LogInfo($"[JESTER] Jester tiré ({serverTurns} licenciements pour réussir)");   // jamais de nom
    }

    // Licenciement : le Jester viré à temps devient Janitor au lieu de partir ; tout autre licenciement consomme un tour
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ServerFirePlayer))]
    [HarmonyPrefix]
    static bool BeforeFire(GameManager __instance, NetworkIdentity player, bool onlyDemoteOrFire, out bool __state)
    {
        __state = false;
        var lp = LobbyOf(player);
        Plugin.L.LogInfo($"[JESTER] licenciement (rétrograder={onlyDemoteOrFire})");
        if (lp == null || jesterId == 0 || lp.netId != jesterId || serverWon || serverFailed) return true;
        __state = true;
        serverWon = true;
        try
        {
            // Écran de renvoi d'un spécialiste chez tous, sans passage en spectateur ; puis Janitor
            lp.RpcFirePlayer(true, PlayerRole.Specialist, PlayerRole.Janitor, false);
            lp.ServerSetPlayerRole(PlayerRole.Janitor);
        }
        catch (Exception e) { Plugin.L.LogError("[JESTER] renvoi : " + e); }
        // Ce que le vrai renvoi fait aussi (on l'a sauté) : comptes des équipes, fin de la réunion
        try { __instance.latestFiredPlayer = lp; __instance.UpdateRolesLeft(); } catch (Exception e) { Plugin.L.LogWarning("[JESTER] équipes : " + e.Message); }
        try { if (__instance.IsState(GameStates.Meeting)) { __instance.ServerFinnishMeeting(); Plugin.L.LogInfo("[JESTER] réunion terminée"); } }
        catch (Exception e) { Plugin.L.LogError("[JESTER] fin de réunion : " + e); }
        NetSignal.SendToPlayer(PlayerIdOf(lp), WonSignal);
        Plugin.L.LogInfo("[JESTER] le Jester s'est fait virer : gagné");
        return false;   // pas de vrai renvoi
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ServerFirePlayer))]
    [HarmonyPostfix]
    static void AfterFire(bool __state)
    {
        if (__state || jesterId == 0 || serverWon || serverFailed) return;
        serverTurns--;
        var id = PlayerIdOf(LobbyById(jesterId));
        if (serverTurns <= 0) { serverFailed = true; NetSignal.SendToPlayer(id, FailedSignal); }
        else NetSignal.SendToPlayer(id, YouBase - serverTurns);
        Plugin.L.LogInfo($"[JESTER] tour consommé ({Mathf.Max(0, serverTurns)} restants)");
    }

    static LobbyPlayer LobbyOf(NetworkIdentity id) =>
        id == null ? null : id.GetComponent<LobbyPlayer>() ?? id.GetComponent<Player.PlayerController>()?.lobbyPlayer;

    static LobbyPlayer LobbyById(uint netId)
    {
        foreach (var lp in Object.FindObjectsOfType<LobbyPlayer>()) if (lp.netId == netId) return lp;
        return null;
    }

    static NetworkIdentity PlayerIdOf(LobbyPlayer lp)
    {
        if (lp == null) return null;
        foreach (var pc in Object.FindObjectsOfType<Player.PlayerController>())
            if (pc.lobbyPlayer != null && pc.lobbyPlayer.Pointer == lp.Pointer) return pc.netIdentity;
        return null;
    }

    // ===================== Client : le Jester lui-même =====================
    enum Status { Playing, Won, Failed }
    static bool mine;
    static int turnsLeft;
    static Status status;

    static void OnYou(int turns)
    {
        bool first = !mine || status != Status.Playing;
        mine = true; status = Status.Playing; turnsLeft = turns;
        foreach (var tab in Object.FindObjectsOfType<ShowRoleTab>()) ApplyRoleTab(tab);
        if (first)
            Hud.Show(T($"Tu es le JESTER ! Fais-toi virer par le manager en {turns} licenciement(s) maximum. Reste discret… (F1 : aide)",
                       $"You are the JESTER! Get fired by the manager within {turns} firing(s). Stay subtle… (F1: help)"), 8f);
        else
            Hud.Show(T($"Jester : encore {turns} licenciement(s) pour te faire virer.", $"Jester: {turns} firing(s) left to get fired."), 4f);
    }

    // Carte de rôle du jeu (début de partie, ou ouverte plus tard) : version Jester, en rose
    [HarmonyPatch(typeof(ShowRoleTab), nameof(ShowRoleTab.UpdateTab))]
    [HarmonyPostfix]
    static void AfterRoleTab(ShowRoleTab __instance) => ApplyRoleTab(__instance);

    static bool roleColorsLogged;
    static void ApplyRoleTab(ShowRoleTab t)
    {
        if (t == null || !mine) return;
        try
        {
            if (!roleColorsLogged)
            {
                roleColorsLogged = true;
                Plugin.L.LogInfo($"[JESTER] carte de rôle : barres {t.cardTopBar?.color} / {t.cardBottomBar?.color}, fond {t.backgroundImage?.color}");
            }
            if (t.roleText != null) t.roleText.text = status == Status.Won ? T("Jester (gagné !)", "Jester (won!)") : T("Vous êtes le Jester !", "You are the Jester!");
            if (t.roleDescriptionText != null)
                t.roleDescriptionText.text = status switch
                {
                    Status.Won => T("Tu t'es fait virer : tu as gagné ! Tu continues en Janitor, garde le secret jusqu'à la fin.",
                                    "You got fired: you won! You carry on as a Janitor, keep it secret until the end."),
                    Status.Failed => T("Raté : plus de licenciements. Tu es maintenant un simple spécialiste.",
                                       "Missed: no firings left. You're now a plain specialist."),
                    _ => T($"Rôle caché : pour tout le monde, tu es un spécialiste. Ton but : te faire virer par le manager en {turnsLeft} licenciement(s) maximum. Maintiens F1 pour l'aide.",
                           $"Hidden role: to everyone you are a specialist. Your goal: get fired by the manager within {turnsLeft} firing(s). Hold F1 for help."),
                };
            if (t.cardTopBar != null) t.cardTopBar.color = Pink;
            if (t.cardBottomBar != null) t.cardBottomBar.color = Pink;
        }
        catch (Exception e) { Plugin.L.LogError("[JESTER] carte de rôle : " + e); }
    }

    static IntPtr gmPtr;
    static void ResetClient() { mine = false; turnsLeft = 0; status = Status.Playing; }

    // ===================== Tous : écran de fin =====================
    static string revealName;
    static bool revealWon;

    static void Reveal(Props.Scripts.Interactable it, bool won)
    {
        revealName = NameOf(it) ?? "?";
        revealWon = won;
        Plugin.L.LogInfo($"[JESTER] révélation reçue (Jester {(won ? "gagnant" : "perdant")}), écran de fin {(ResultTab.instance == null ? "pas encore ouvert" : "ouvert")}");
        ApplyResult();
    }

    // Nom du joueur derrière l'objet qui a porté le signal (son PlayerInteractable) : même identité réseau que son
    // PlayerController, qui n'est pas forcément sur le même GameObject
    static string NameOf(Props.Scripts.Interactable it)
    {
        if (it == null) return null;
        var pc = it.GetComponent<Player.PlayerController>() ?? it.GetComponentInParent<Player.PlayerController>();
        if (pc == null && it.netIdentity != null)
            foreach (var x in Object.FindObjectsOfType<Player.PlayerController>())
                if (x.netIdentity != null && x.netIdentity.Pointer == it.netIdentity.Pointer) { pc = x; break; }
        var name = pc?.lobbyPlayer?.username;
        if (string.IsNullOrEmpty(name)) Plugin.L.LogWarning($"[JESTER] nom introuvable (objet '{it.name}', joueur {(pc == null ? "absent" : "sans LobbyPlayer")})");
        return string.IsNullOrEmpty(name) ? null : name;
    }

    [HarmonyPatch(typeof(ResultTab), nameof(ResultTab.SetResult))]
    [HarmonyPostfix]
    static void AfterSetResult() => ApplyResult();

    static void ApplyResult()
    {
        var rt = ResultTab.instance;
        if (rt == null || revealName == null) return;
        try
        {
            if (revealWon)
            {
                if (rt.resultText != null) rt.resultText.text = T("Le Jester gagne !", "The Jester wins!");
                if (rt.descriptionText != null) rt.descriptionText.text = T($"{revealName} était le Jester… et s'est fait virer !", $"{revealName} was the Jester… and got fired!");
                if (rt.tabBarColorImage != null) rt.tabBarColorImage.color = Pink;
                // Fond de l'écran (bleu ou rouge selon l'équipe) : rose, même transparence
                // (l'animation d'ouverture de l'onglet repart de backgroundColor : on change les deux)
                var bg = rt.backgroundColor;
                rt.backgroundColor = new Color(Pink.r, Pink.g, Pink.b, bg.a);
                if (rt.background != null) { var a = rt.background.color.a; rt.background.color = new Color(Pink.r, Pink.g, Pink.b, a); }
                if (rt.specialistCrown != null) rt.specialistCrown.SetActive(false);
                if (rt.slackerCrown != null) rt.slackerCrown.SetActive(false);
            }
            else if (rt.descriptionText != null && !rt.descriptionText.text.Contains(revealName + " "))
                rt.descriptionText.text += "\n" + T($"{revealName} était le Jester (raté !)", $"{revealName} was the Jester (missed!)");
        }
        catch (Exception e) { Plugin.L.LogError("[JESTER] écran de fin : " + e); }
    }

    // ===================== Réglage du lobby "Jester : Désactivé / Activé" =====================
    // Ajouté à la catégorie "Rôles" des réglages du lobby, sur le modèle de "Use Analyst" (mêmes choix Désactivé/Activé).
    // Libellés : clés "dalemod.settings.jester.*", traduites par CustomTaskPatches.LocalizePatch (Localize ci-dessous).
    const string SettingKey = "ZoomerMod Jester";
    static float nextSettingCheck;

    internal static string Localize(string key) => key switch
    {
        "dalemod.settings.jester.label" => T("Jester (ZoomerMod)", "Jester (ZoomerMod)"),
        "dalemod.settings.jester.hint" => T("Si activé, un spécialiste devient en secret le Jester (à partir de 5 joueurs) : il gagne seul s'il se fait virer par le manager.",
                                            "If enabled, a specialist secretly becomes the Jester (5+ players): they win alone if they get fired by the manager."),
        "dalemod.settings.jester.sub" => "Jester",
        _ => null,
    };

    static Setting FindSetting(string key)
    {
        var m = GameRulesSettingsManager.instance;
        if (m?.categories == null) return null;
        foreach (var c in m.categories)
            if (c?.Settings != null)
                foreach (var st in c.Settings) if (st != null && st.Key == key) return st;
        return null;
    }

    static void EnsureSetting()
    {
        if (Time.unscaledTime < nextSettingCheck) return;
        nextSettingCheck = Time.unscaledTime + 2f;
        var m = GameRulesSettingsManager.instance;
        if (m?.categories == null || FindSetting(SettingKey) != null) return;
        try
        {
            var model = FindSetting("Use Analyst");
            SettingCategory roles = null;
            foreach (var c in m.categories) if (c != null && c.CategoryName == "lobbySettings.roles.title") roles = c;
            if (model == null || roles?.Settings == null) return;
            var st = new Setting
            {
                Key = SettingKey,
                label = "dalemod.settings.jester.label",
                hint = "dalemod.settings.jester.hint",
                subCategory = "dalemod.settings.jester.sub",
                subCategoryColor = Pink,
                type = model.type,
                alternatives = model.alternatives,   // Désactivé / Activé
                Value = 1f,
                roundTo = model.roundTo,
                additionalInfo = model.additionalInfo,
                presetName = model.presetName,
            };
            roles.Settings.Add(st);
            Plugin.L.LogInfo("[JESTER] réglage ajouté aux réglages du lobby (catégorie Rôles)");
        }
        catch (Exception e) { Plugin.L.LogWarning("[JESTER] réglage du lobby : " + e.Message); }
    }

    // Activé par défaut : sans réglage trouvé, on garde le Jester
    static bool SettingEnabled()
    {
        var st = FindSetting(SettingKey);
        return st == null || st.Value >= 0.5f;
    }

    // ===================== Chaque frame (DebugMenu.Update) =====================
    internal static void Tick()
    {
        EnsureSetting();
        var gm = GameManager.instance;
        if (gm == null) { ResetServer(); HideUi(); return; }
        if (NetworkServer.active) ServerTick(gm);
        // Chez les clients, l'état de la partie (IsState) n'est pas fiable : seul le serveur fait tourner la machine
        // d'états. Nouvelle partie = nouveau GameManager (scène rechargée) : on oublie alors le Jester et la révélation.
        if (gm.Pointer != gmPtr) { gmPtr = gm.Pointer; ResetClient(); revealName = null; }
        UpdateHelp();
        UpdateHint();
    }

    // --- Aide (F1 maintenue) ---
    static TextMeshProUGUI help;

    static void UpdateHelp()
    {
        var kb = Keyboard.current;
        bool show = mine && kb != null && kb.f1Key.isPressed;
        if (!show) { if (help != null) help.gameObject.SetActive(false); return; }
        if (help == null)
        {
            help = GameUi.NewText("DaleMod_JesterHelp");
            var rt = help.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            help.alignment = TextAlignmentOptions.Center;
            help.color = Color.white;
        }
        GameUi.Style(help);
        help.rectTransform.sizeDelta = new Vector2(Screen.width * 0.55f, Screen.height * 0.7f);
        help.fontSize = Mathf.Max(18f, Screen.height / 42f);
        string state = status switch
        {
            Status.Won => T("<color=#FF00FF>Gagné !</color> Tu t'es fait virer. Garde le secret jusqu'à la fin.", "<color=#FF00FF>Won!</color> You got fired. Keep it secret until the end."),
            Status.Failed => T("Raté : plus de licenciements. Tu es un simple spécialiste.", "Missed: no firings left. You're a plain specialist."),
            _ => T($"Licenciements restants : <size=180%><color=#FF00FF>{turnsLeft}</color></size>", $"Firings left: <size=180%><color=#FF00FF>{turnsLeft}</color></size>"),
        };
        help.text =
            "<size=170%><color=#FF00FF>JESTER</color></size>\n\n" + state + "\n\n" +
            T("Ton but : te faire <b>virer par le manager</b>.\n" +
              "Pour tout le monde, tu es un spécialiste : fais tes tâches (bleues et rouges) comme eux.\n" +
              "Chaque licenciement d'un autre joueur te coûte un tour. Plus de tours : tu redeviens un simple spécialiste.\n" +
              "Viré à temps : tu deviens Janitor, et à la fin de la partie <b>tu es le seul gagnant</b>.\n" +
              "Rends-toi suspect… mais pas trop : un Jester trop évident, le manager ne le virera pas.",
              "Your goal: get <b>fired by the manager</b>.\n" +
              "To everyone you are a specialist: do your tasks (blue and red) like them.\n" +
              "Each firing of another player costs you a turn. Out of turns: you become a plain specialist.\n" +
              "Fired in time: you become a Janitor, and at the end of the game <b>you are the only winner</b>.\n" +
              "Look suspicious… but not too much: an obvious Jester won't get fired.");
        help.gameObject.SetActive(true);
    }

    // --- Indication sous le nom de la pièce (HUD du jeu) ---
    static TextMeshProUGUI hint;

    static void UpdateHint()
    {
        if (!mine) { if (hint != null) hint.gameObject.SetActive(false); return; }
        if (hint == null)
        {
            var info = Object.FindObjectOfType<RoomInformation>();
            var room = info?.roomName;
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
