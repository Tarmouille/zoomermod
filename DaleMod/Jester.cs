using System;
using GameManagement;
using GameManagement.StateMachine;
using HarmonyLib;
using Mirror;
using Player.Lobby;
using UnityEngine;

namespace DaleMod;

// Rôle bonus caché "Jester" (RoleKit) : son but est de se faire VIRER par le manager.
// - Pour le jeu, c'est un Specialist normal (tâches bleues et rouges, vu comme spécialiste par tous). Personne ne sait
//   qui c'est, ni le manager ni l'hôte : seul le serveur le sait en interne, rien n'est affiché ni logué avec un nom.
// - Il a ceil(limite de licenciements / 2) "tours" : chaque licenciement prononcé (manager remplacé compris) en consomme un.
// - Viré à temps : écran de renvoi d'un spécialiste ("était l'un des meilleurs…"), il devient Janitor et a gagné.
//   Tours épuisés : il redevient un simple spécialiste (raté). Son rôle n'est jamais transmis à un autre joueur.
// - Fin de partie : Jester gagnant = seul vainqueur (titre et fond roses) ; sa ligne apparaît dans tous les cas.
// - Chez le Jester : carte de rôle rose, aide en maintenant F1 (règles + licenciements restants).
static class Jester
{
    const int MaxTurns = 40;
    // Signaux propres au Jester : "tu es le Jester, N tours restants" = YouBase - N ; gagné / raté (au Jester seul)
    const float YouBase = -9100f, WonSignal = -9150f, FailedSignal = -9151f;
    static readonly Color Pink = new(1f, 0f, 1f);
    static string T(string fr, string en) => RoleKit.T(fr, en);

    internal static BonusRole Role;

    internal static void Register()
    {
        Role = new BonusRole
        {
            Id = "jester", Name = "Jester", SettingKey = "ZoomerMod Jester", Color = Pink, SignalBase = -9170f,
            Pick = (players, taken, _) => RoleKit.PickOf(players, taken, PlayerRole.Specialist),
            Assign = (lp, gm) => Make(lp, gm),
            ServerTick = ServerTick,
            ServerReset = () => { janitorAt = -1f; jesterId = 0; serverTurns = 0; serverWon = serverFailed = false; },
            ServerOutcome = () => jesterId == 0 ? null : serverWon,
            ServerHolder = () => jesterId,
            ClientReset = () => { mine = false; turnsLeft = 0; status = Status.Playing; },
            LocalIsHolder = () => mine,
            LocalHasHelp = () => mine,
            HelpText = HelpText,
            RoleCard = RoleCard,
            ResultExtra = (rt, won) =>
            {
                if (!won) return;
                if (rt.resultText != null) rt.resultText.text = T("Le Jester gagne !", "The Jester wins!");
                if (rt.tabBarColorImage != null) rt.tabBarColorImage.color = Pink;
                // Fond de l'écran (bleu ou rouge selon l'équipe) : rose, même transparence ; l'animation d'ouverture
                // de l'onglet repart de backgroundColor : on change les deux
                rt.backgroundColor = RoleKit.WithAlpha(Pink, rt.backgroundColor.a);
                if (rt.background != null) rt.background.color = RoleKit.WithAlpha(Pink, rt.background.color.a);
                if (rt.specialistCrown != null) rt.specialistCrown.SetActive(false);
                if (rt.slackerCrown != null) rt.slackerCrown.SetActive(false);
            },
            ResultLine = won => won ? T("🃏 Jester : {0}, viré à temps : seul gagnant !", "🃏 Jester: {0}, fired in time: sole winner!")
                                    : T("🃏 Jester : {0} (raté)", "🃏 Jester: {0} (missed)"),
        };
        RoleKit.Add(Role);

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
    }

    // ===================== Serveur (hôte) =====================
    static uint jesterId;          // netId du LobbyPlayer du Jester (0 : pas de Jester)
    static int serverTurns;
    static bool serverWon, serverFailed;

    const float JanitorDelay = 6f;
    static float janitorAt = -1f;

    static void ServerTick(GameManager gm)
    {
        if (janitorAt > 0f && Time.time >= janitorAt)
        {
            janitorAt = -1f;
            try { RoleKit.LobbyById(jesterId)?.ServerSetPlayerRole(PlayerRole.Janitor); Plugin.L.LogInfo("[JESTER] passé Janitor"); }
            catch (Exception e) { Plugin.L.LogError("[JESTER] Janitor : " + e); }
        }
    }

    // Menu F7 (hôte) : rend ce joueur Jester, sans rien afficher
    internal static void Force(LobbyPlayer lp)
    {
        if (!NetworkServer.active || lp == null || GameManager.instance == null) return;
        Make(lp, GameManager.instance);
        RoleKit.Announce(Role, lp);
    }

    static void Make(LobbyPlayer lp, GameManager gm)
    {
        jesterId = lp.netId;
        serverTurns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(1, gm.fireLimit) / 2f), 1, MaxTurns);
        serverWon = serverFailed = false;
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), YouBase - serverTurns);
        Plugin.L.LogInfo($"[JESTER] Jester tiré ({serverTurns} licenciements pour réussir)");   // jamais de nom
    }

    // Licenciement : le Jester viré à temps devient Janitor au lieu de partir ; tout autre licenciement consomme un tour
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ServerFirePlayer))]
    [HarmonyPrefix]
    static bool BeforeFire(GameManager __instance, NetworkIdentity player, bool onlyDemoteOrFire, out bool __state)
    {
        __state = false;
        var lp = RoleKit.LobbyOf(player);
        Plugin.L.LogInfo($"[ROLES] licenciement (rétrograder={onlyDemoteOrFire})");
        if (lp == null || jesterId == 0 || lp.netId != jesterId || serverWon || serverFailed) return true;
        __state = true;
        serverWon = true;
        try
        {
            // Écran de renvoi d'un spécialiste chez tous ("était l'un des meilleurs…"), sans passage en spectateur.
            // L'écran affiche le rôle passé ici : Specialist. Le passage en Janitor vient après, une fois l'écran
            // affiché chez tous (ServerTick), sinon l'écran annoncerait un Janitor.
            lp.RpcFirePlayer(true, PlayerRole.Specialist, PlayerRole.Specialist, false);
            janitorAt = Time.time + JanitorDelay;
        }
        catch (Exception e) { Plugin.L.LogError("[JESTER] renvoi : " + e); }
        // Ce que le vrai renvoi fait aussi (on l'a sauté) : comptes des équipes, fin de la réunion
        try { __instance.latestFiredPlayer = lp; __instance.UpdateRolesLeft(); } catch (Exception e) { Plugin.L.LogWarning("[JESTER] équipes : " + e.Message); }
        try { if (__instance.IsState(GameStates.Meeting)) { __instance.ServerFinnishMeeting(); Plugin.L.LogInfo("[JESTER] réunion terminée"); } }
        catch (Exception e) { Plugin.L.LogError("[JESTER] fin de réunion : " + e); }
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), WonSignal);
        // Prévient déjà tout le monde (rien ne s'affiche avant l'écran de fin)
        RoleKit.Announce(Role, lp);
        RoleKit.SendOutcome(Role, true);
        Plugin.L.LogInfo("[JESTER] le Jester s'est fait virer : gagné");
        return false;   // pas de vrai renvoi
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ServerFirePlayer))]
    [HarmonyPostfix]
    static void AfterFire(bool __state)
    {
        if (__state || jesterId == 0 || serverWon || serverFailed) return;
        serverTurns--;
        var id = RoleKit.PlayerIdOf(RoleKit.LobbyById(jesterId));
        if (serverTurns <= 0) { serverFailed = true; NetSignal.SendToPlayer(id, FailedSignal); }
        else NetSignal.SendToPlayer(id, YouBase - serverTurns);
        Plugin.L.LogInfo($"[JESTER] tour consommé ({Mathf.Max(0, serverTurns)} restants)");
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
        RoleKit.RefreshRoleTabs();
        if (first)
            Hud.Show(T($"Tu es le JESTER ! Fais-toi virer par le manager en {turns} licenciement(s) maximum. Reste discret… (F1 : aide)",
                       $"You are the JESTER! Get fired by the manager within {turns} firing(s). Stay subtle… (F1: help)"), 8f);
        else
            Hud.Show(T($"Jester : encore {turns} licenciement(s) pour te faire virer.", $"Jester: {turns} firing(s) left to get fired."), 4f);
    }

    static void RoleCard(ShowRoleTab t)
    {
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
    }

    static string HelpText()
    {
        string state = status switch
        {
            Status.Won => T("<color=#FF00FF>Gagné !</color> Tu t'es fait virer. Garde le secret jusqu'à la fin.", "<color=#FF00FF>Won!</color> You got fired. Keep it secret until the end."),
            Status.Failed => T("Raté : plus de licenciements. Tu es un simple spécialiste.", "Missed: no firings left. You're a plain specialist."),
            _ => T($"Licenciements restants : <size=180%><color=#FF00FF>{turnsLeft}</color></size>", $"Firings left: <size=180%><color=#FF00FF>{turnsLeft}</color></size>"),
        };
        return "<size=170%><color=#FF00FF>JESTER</color></size>\n\n" + state + "\n\n" +
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
    }
}
