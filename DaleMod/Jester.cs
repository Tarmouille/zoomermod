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
    // Devenu manager (vote de remplacement) : mission ratée ; au Jester seul / à tous à la fin (écran de fin)
    const float PromotedSignal = -9152f, PromotedAllSignal = -9153f;
    // Viré par le Cost Killer (pas par le manager) : raté ; au Jester seul
    const float CostKilledSignal = -9154f;
    static readonly Color Pink = new(0.961f, 0.651f, 0.831f);   // #F5A6D4
    const string Hex = "#F5A6D4";
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
            ServerReset = () => jesters.Clear(),
            ServerReveal = () => { if (Drawn?.Promoted == true && WhiteBoardController.instance != null) NetSignal.Send(WhiteBoardController.instance, PromotedAllSignal); },
            ServerOutcome = () => Drawn?.Won,
            ServerHolder = () => Drawn?.Id ?? 0,
            ClientReset = () => { mine = false; turnsLeft = 0; status = Status.Playing; promotedSeen = false; },
            LocalIsHolder = () => mine,
            LocalHasHelp = () => mine,
            HelpText = HelpText,
            RoleCard = RoleCard,
            ResultExtra = (rt, won) => { if (won) WinScreen(rt); },
            ResultLine = won => won ? T("Jester : {0}, viré à temps : seul gagnant !", "Jester: {0}, fired in time: sole winner!")
                                    : promotedSeen ? T("Jester : {0} (raté, devenu manager)", "Jester: {0} (missed, became manager)")
                                    : T("Jester : {0} (raté)", "Jester: {0} (missed)"),
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
        NetSignal.OnLocalPlayer(PromotedSignal, () =>
        {
            mine = false; status = Status.Failed; turnsLeft = 0;   // plus de carte ni d'aide du Jester
            Hud.Show(T("Promu manager : ta mission de Jester s'arrête là. Joue le manager normalement.",
                       "Promoted to manager: your Jester mission ends here. Play the manager normally."), 6f);
        });
        NetSignal.On(PromotedAllSignal, _ => { promotedSeen = true; RoleKit.ApplyResult(); });
        // Chez tous : le poste du Jester passé Janitor s'éteint, comme celui d'un vrai viré
        NetSignal.On(DeskOffSignal, it =>
        {
            var ws = it?.TryCast<Objects.Scripts.WorkStationController>();
            if (ws == null) return;
            ws.DisableWorkStation();
            Plugin.L.LogInfo("[JESTER] poste éteint");
        });
        NetSignal.OnLocalPlayer(CostKilledSignal, () =>
        {
            mine = true; status = Status.Failed; turnsLeft = 0;
            Hud.Show(T("Viré… mais pas par le manager : ça ne compte pas, ta mission de Jester est ratée.",
                       "Fired… but not by the manager: it doesn't count, your Jester mission failed."), 6f);
        });
        NetSignal.OnLocalPlayer(FailedSignal, () =>
        {
            mine = true; status = Status.Failed; turnsLeft = 0;
            Hud.Show(T($"Raté… plus de licenciements pour toi. Tu es maintenant un simple {BaseName()}.",
                       $"Missed… no firings left for you. You're now a plain {BaseName()}."), 6f);
        });
    }

    // ===================== Serveur (hôte) =====================
    // Un Jester tiré au début (au plus un) + éventuellement un ex-Rival devenu Jester en cours de partie (Rival.cs) :
    // seul cas où il y a deux Jesters. Chacun a ses tours ; l'ex-Rival est compté dans le résultat du Rival.
    sealed class JState { public uint Id; public int Turns; public bool Won, Failed, ExRival, Promoted; public float JanitorAt = -1f; }
    static readonly System.Collections.Generic.List<JState> jesters = new();
    static JState Drawn => jesters.Find(j => !j.ExRival);

    const float JanitorDelay = 1f;
    const float DeskOffSignal = -92.6f;   // serveur -> tous : éteindre le poste du Jester (le vrai renvoi est sauté)

    internal static bool IsJester(uint netId) => netId != 0 && jesters.Exists(j => j.Id == netId && !j.Failed);
    internal static bool? OutcomeOf(uint netId) => jesters.Find(j => j.Id == netId)?.Won;

    // Minuteur de licenciement du manager : le vrai renvoi (sauté pour le Jester) le relance. Sens du décompte appris
    // en l'observant (il est à l'arrêt pendant les réunions, où le Jester se fait virer) : +1 monte, -1 descend.
    static float lastTimer = float.NaN;
    static int timerDir;

    static void WatchTimer(GameManager gm)
    {
        float t = gm.terminationTimer;
        if (!float.IsNaN(lastTimer) && Mathf.Abs(t - lastTimer) > 1e-4f && Mathf.Abs(t - lastTimer) < 1f) timerDir = t > lastTimer ? 1 : -1;
        lastTimer = t;
    }

    static void ResetTimer(GameManager gm)
    {
        float before = gm.terminationTimer, reset = timerDir > 0 ? 0f : gm.terminationMaxTime;
        if (timerDir == 0) Plugin.L.LogWarning("[JESTER] sens du minuteur inconnu : supposé décroissant");
        gm.terminationTimer = reset;
        gm.RpcResetTerminationTimer(reset);
        lastTimer = float.NaN;
        Plugin.L.LogInfo($"[JESTER] minuteur de licenciement relancé : {before:0.0} -> {reset:0.0} (max {gm.terminationMaxTime:0.0}, sens {timerDir})");
    }

    static void ServerTick(GameManager gm)
    {
        try { WatchTimer(gm); } catch { }
        foreach (var j in jesters)
        {
            // Devenu manager (vote de remplacement) : il ne peut plus se faire virer, mission ratée
            if (!j.Won && !j.Failed && RoleKit.LobbyById(j.Id)?.playerRole == PlayerRole.Manager)
            {
                j.Failed = j.Promoted = true;
                NetSignal.SendToPlayer(RoleKit.PlayerIdOf(RoleKit.LobbyById(j.Id)), PromotedSignal);
                Plugin.L.LogInfo("[JESTER] devenu manager : raté");
            }
            if (j.JanitorAt <= 0f || Time.time < j.JanitorAt) continue;
            j.JanitorAt = -1f;
            var lp = RoleKit.LobbyById(j.Id);
            try { lp?.ServerSetPlayerRole(PlayerRole.Janitor); Plugin.L.LogInfo("[JESTER] passé Janitor"); }
            catch (Exception e) { Plugin.L.LogError("[JESTER] Janitor : " + e); }
            try { var ws = lp?.workStationController; if (ws != null) NetSignal.Send(ws, DeskOffSignal); }
            catch (Exception e) { Plugin.L.LogError("[JESTER] poste : " + e); }
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
        jesters.RemoveAll(j => !j.ExRival || j.Id == lp.netId);
        var st = new JState { Id = lp.netId, Turns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(1, gm.fireLimit) / 2f), 1, MaxTurns) };
        jesters.Add(st);
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), YouBase - st.Turns);
        Plugin.L.LogInfo($"[JESTER] Jester tiré ({st.Turns} licenciements pour réussir)");   // jamais de nom
    }

    // Rival dont la cible a échappé (partie quittée, devenue manager) : Jester avec la moitié des licenciements
    // RESTANTS (arrondie au supérieur), au moins 2
    internal static void MakeConverted(LobbyPlayer lp, GameManager gm)
    {
        jesters.RemoveAll(j => j.Id == lp.netId);
        int left = 0;
        try { left = Mathf.Max(0, gm.fireLimit - gm.GetNumberOfFiredEmployees()); } catch (Exception e) { Plugin.L.LogWarning("[JESTER] licenciements restants : " + e.Message); }
        var st = new JState { Id = lp.netId, ExRival = true, Turns = Mathf.Clamp(Mathf.CeilToInt(left / 2f), 2, MaxTurns) };
        jesters.Add(st);
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), YouBase - st.Turns);
        Plugin.L.LogInfo($"[JESTER] ex-Rival devenu Jester ({st.Turns} licenciements pour réussir, {left} restants)");
    }

    // Licenciement : un Jester viré à temps devient Janitor au lieu de partir ; tout autre licenciement consomme un tour
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ServerFirePlayer))]
    [HarmonyPrefix]
    static bool BeforeFire(GameManager __instance, NetworkIdentity player, bool onlyDemoteOrFire)
    {
        var lp = RoleKit.LobbyOf(player);
        Plugin.L.LogInfo($"[ROLES] licenciement (rétrograder={onlyDemoteOrFire})");
        var j = lp == null ? null : jesters.Find(x => x.Id == lp.netId && !x.Won && !x.Failed);
        if (j == null) return true;
        if (CostKiller.FiringNow)
        {
            // Viré par le Cost Killer : vrai renvoi, mais le Jester ne gagne pas (seul le manager compte)
            j.Failed = true;
            NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), CostKilledSignal);
            Plugin.L.LogInfo("[JESTER] viré par le Cost Killer : raté");
            return true;
        }
        j.Won = true;
        try
        {
            // Écran de renvoi de son rôle de base chez tous (spécialiste : "était l'un des meilleurs…" ; fainéant pour un
            // ex-Rival fainéant), sans passage en spectateur. L'écran affiche le rôle passé ici ; le passage en Janitor
            // suit 1 s après (ServerTick), le temps que l'écran soit affiché chez tous.
            var shown = lp.playerRole == PlayerRole.Slacker ? PlayerRole.Slacker : PlayerRole.Specialist;
            lp.RpcFirePlayer(true, shown, shown, false);
            j.JanitorAt = Time.time + JanitorDelay;
        }
        catch (Exception e) { Plugin.L.LogError("[JESTER] renvoi : " + e); }
        // Ce que le vrai renvoi fait aussi (on l'a sauté) : comptes des équipes, minuteur du manager, fin de la réunion
        try { __instance.latestFiredPlayer = lp; __instance.UpdateRolesLeft(); } catch (Exception e) { Plugin.L.LogWarning("[JESTER] équipes : " + e.Message); }
        try { ResetTimer(__instance); } catch (Exception e) { Plugin.L.LogError("[JESTER] minuteur : " + e); }
        try { if (__instance.IsState(GameStates.Meeting)) { __instance.ServerFinnishMeeting(); Plugin.L.LogInfo("[JESTER] réunion terminée"); } }
        catch (Exception e) { Plugin.L.LogError("[JESTER] fin de réunion : " + e); }
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), WonSignal);
        // Prévient déjà tout le monde (rien ne s'affiche avant l'écran de fin) ; l'ex-Rival passe par le Rival
        if (!j.ExRival) { RoleKit.Announce(Role, lp); RoleKit.SendOutcome(Role, true); }
        Plugin.L.LogInfo("[JESTER] un Jester s'est fait virer : gagné");
        return false;   // pas de vrai renvoi
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ServerFirePlayer))]
    [HarmonyPostfix]
    static void AfterFire()
    {
        if (Pistonne.BlockedFrame == Time.frameCount) return;   // renvoi du Pistonné bloqué : personne n'est viré
        if (CostKiller.Resigning) return;                       // démission du Cost Killer : ne compte pas
        foreach (var j in jesters)
        {
            if (j.Won || j.Failed) continue;   // dont celui qui vient de se faire virer
            j.Turns--;
            var id = RoleKit.PlayerIdOf(RoleKit.LobbyById(j.Id));
            if (j.Turns <= 0) { j.Failed = true; NetSignal.SendToPlayer(id, FailedSignal); }
            else NetSignal.SendToPlayer(id, YouBase - j.Turns);
            Plugin.L.LogInfo($"[JESTER] tour consommé ({Mathf.Max(0, j.Turns)} restants)");
        }
    }

    // ===================== Client : le Jester lui-même =====================
    enum Status { Playing, Won, Failed }
    static bool mine;
    static int turnsLeft;
    static Status status;
    static bool promotedSeen;   // chez tous, à la fin : le Jester tiré est devenu manager

    static void OnYou(int turns)
    {
        bool first = !mine || status != Status.Playing;
        mine = true; status = Status.Playing; turnsLeft = turns;
        RoleKit.RefreshRoleTabs();
        if (first && Rival.ConvertedLocal)
            Hud.Show(T($"Ta cible t'a échappé : tu deviens JESTER ! Fais-toi virer par le manager en {turns} licenciement(s) maximum. (F1 : aide)",
                       $"Your target got away: you become a JESTER! Get fired by the manager within {turns} firing(s). (F1: help)"), 8f);
        else if (first)
            RoleKit.ShowAfterCard(T($"Tu es le JESTER ! Fais-toi virer par le manager en {turns} licenciement(s) maximum. Reste discret… (F1 : aide)",
                       $"You are the JESTER! Get fired by the manager within {turns} firing(s). Stay subtle… (F1: help)"), 8f);
        else
            Hud.Show(T($"Jester : encore {turns} licenciement(s) pour te faire virer.", $"Jester: {turns} firing(s) left to get fired."), 4f);
    }

    // Rôle de base du joueur local (un ex-Rival devenu Jester peut être fainéant)
    static string BaseName() => RoleKit.LocalLobby()?.playerRole == PlayerRole.Slacker ? T("fainéant", "slacker") : T("spécialiste", "specialist");

    // Écran de fin d'un Jester gagnant : seul vainqueur (titre et fond roses, couronnes masquées)
    internal static void WinScreen(ResultTab rt)
    {
        if (rt.resultText != null) rt.resultText.text = T("Le Jester gagne !", "The Jester wins!");
        RoleKit.TintResult(rt, Pink);
        if (rt.specialistCrown != null) rt.specialistCrown.SetActive(false);
        if (rt.slackerCrown != null) rt.slackerCrown.SetActive(false);
    }

    static void RoleCard(ShowRoleTab t)
    {
        if (t.roleText != null) t.roleText.text = status == Status.Won ? T("Jester (gagné !)", "Jester (won!)") : T("Vous êtes le Jester !", "You are the Jester!");
        if (t.roleDescriptionText != null)
            t.roleDescriptionText.text = status switch
            {
                Status.Won => T("Tu t'es fait virer : tu as gagné ! Tu continues en Janitor, garde le secret jusqu'à la fin.",
                                "You got fired: you won! You carry on as a Janitor, keep it secret until the end."),
                Status.Failed => T($"Raté : plus de licenciements. Tu es maintenant un simple {BaseName()}.",
                                   $"Missed: no firings left. You're now a plain {BaseName()}."),
                _ => T($"Rôle caché : pour tout le monde, tu es un {BaseName()}. Ton but : te faire virer par le manager en {turnsLeft} licenciement(s) maximum. Maintiens F1 pour l'aide.",
                       $"Hidden role: to everyone you are a {BaseName()}. Your goal: get fired by the manager within {turnsLeft} firing(s). Hold F1 for help."),
            };
    }

    static string HelpText()
    {
        string state = status switch
        {
            Status.Won => T($"<color={Hex}>Gagné !</color> Tu t'es fait virer. Garde le secret jusqu'à la fin.", $"<color={Hex}>Won!</color> You got fired. Keep it secret until the end."),
            Status.Failed => T($"Raté : plus de licenciements. Tu es un simple {BaseName()}.", $"Missed: no firings left. You're a plain {BaseName()}."),
            _ => T($"Licenciements restants : <size=180%><color={Hex}>{turnsLeft}</color></size>", $"Firings left: <size=180%><color={Hex}>{turnsLeft}</color></size>"),
        };
        return $"<size=170%><color={Hex}>JESTER</color></size>\n\n" + state + "\n\n" +
            T("Ton but : te faire <b>virer par le manager</b>.\n" +
              $"Pour tout le monde, tu es un {BaseName()} : fais tes tâches comme les autres.\n" +
              $"Chaque licenciement d'un autre joueur te coûte un tour. Plus de tours : tu redeviens un simple {BaseName()}.\n" +
              "Viré à temps : tu deviens Janitor, et à la fin de la partie <b>tu es le seul gagnant</b>.\n" +
              "Si tu deviens manager (vote de remplacement), ta mission est ratée.\n" +
              "Rends-toi suspect… mais pas trop : un Jester trop évident, le manager ne le virera pas.",
              "Your goal: get <b>fired by the manager</b>.\n" +
              $"To everyone you are a {BaseName()}: do your tasks like the others.\n" +
              $"Each firing of another player costs you a turn. Out of turns: you become a plain {BaseName()}.\n" +
              "Fired in time: you become a Janitor, and at the end of the game <b>you are the only winner</b>.\n" +
              "If you become manager (replacement vote), your mission fails.\n" +
              "Look suspicious… but not too much: an obvious Jester won't get fired.");
    }
}
