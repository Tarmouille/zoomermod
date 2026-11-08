using System;
using System.Collections.Generic;
using GameManagement;
using GameManagement.StateMachine;
using Mirror;
using Player.Lobby;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Rôle bonus caché "Rival" (RoleKit), neutre (inspiré de l'Exécuteur de Town of Salem) : un spécialiste ou un fainéant
// qui reçoit une cible au hasard ; il gagne si sa cible se fait virer, que son camp gagne ou perde.
// - Caché de tous (cible comprise) ; il garde son rôle du jeu et ses tâches pour passer inaperçu.
// - Cible : toujours un SPÉCIALISTE (camp majoritaire, comme dans Town of Salem : un Rival fainéant ne peut pas
//   affaiblir son camp minoritaire), jamais le Jester ni lui-même ; choisie après le tirage des autres rôles.
// - Cible qui quitte la partie ou devient manager (vote) : le Rival devient Jester (moitié des licenciements restants,
//   au moins 2) ; seul cas où il peut y avoir deux Jesters.
// - Viré avant : raté. Le rôle n'est jamais transmis.
static class Rival
{
    // Signaux propres au Rival : au Rival seul (tu es / gagné / viré / converti) ; à tous (cible, converti à la fin)
    const float YouSignal = -9300f, WonSignal = -9301f, FailedSignal = -9302f, ConvertSignal = -9303f;
    const float TargetSignal = -9310f, ConvertedAllSignal = -9311f;
    // Devenu manager (il pourrait virer sa cible lui-même) : raté ; au Rival seul / à tous à la fin
    const float PromotedSignal = -9304f, PromotedAllSignal = -9312f;
    static readonly Color Grey = new(0.580f, 0.592f, 0.592f);   // #949797
    const string Hex = "#949797";
    static string T(string fr, string en) => RoleKit.T(fr, en);

    internal static BonusRole Role;

    internal static void Register()
    {
        Role = new BonusRole
        {
            Id = "rival", Name = "Rival", SettingKey = "ZoomerMod Rival", Color = Grey, SignalBase = -9370f,
            Pick = (players, taken, _) => PickRival(players, taken),
            Assign = (lp, _) => { Reset(); rivalId = lp.netId; },   // cible choisie après le tirage (pas le Jester)
            AfterDraw = _ => { if (rivalId != 0 && targetId == 0 && !ChooseTarget()) rivalId = 0; },
            ServerTick = ServerTick,
            ServerReset = Reset,
            ServerOutcome = () => rivalId == 0 || targetId == 0 ? null : converted ? Jester.OutcomeOf(rivalId) ?? false : won,
            ServerHolder = () => rivalId,
            ServerReveal = () =>
            {
                AnnounceTarget();
                if (converted && WhiteBoardController.instance != null) NetSignal.Send(WhiteBoardController.instance, ConvertedAllSignal);
                if (promoted && WhiteBoardController.instance != null) NetSignal.Send(WhiteBoardController.instance, PromotedAllSignal);
            },
            ClientReset = () => { mine = wonLocal = lostLocal = ConvertedLocal = introShown = convertedSeen = promotedSeen = false; targetName = null; },
            LocalIsHolder = () => mine && !ConvertedLocal,   // devenu Jester : carte et aide du Jester
            LocalHasHelp = () => mine && !ConvertedLocal,
            HelpText = HelpText,
            RoleCard = RoleCard,
            // Gagné en Jester : écran du Jester ; gagné en Rival : fond gris, texte du camp gagnant gardé (Jester prioritaire)
            ResultExtra = (rt, won) =>
            {
                if (!won) return;
                if (convertedSeen || ConvertedLocal) Jester.WinScreen(rt);
                else if (Jester.Role?.Outcome != true) RoleKit.TintResult(rt, Grey);
            },
            ResultLine = ResultLine,
        };
        RoleKit.Add(Role);

        NetSignal.OnLocalPlayer(YouSignal, () => { mine = true; RoleKit.RefreshRoleTabs(); TryIntro(); });
        NetSignal.On(TargetSignal, it =>
        {
            // Reçu par tous (pour l'écran de fin) ; rien n'est affiché ni logué avec le nom, sauf chez le Rival
            targetName = RoleKit.NameOf(it) ?? targetName;
            RoleKit.RefreshRoleTabs();
            TryIntro();
            RoleKit.ApplyResult();
        });
        NetSignal.OnLocalPlayer(WonSignal, () =>
        {
            wonLocal = true;
            Hud.Show(T($"{targetName ?? "Ta cible"} est viré(e) : tu as gagné, Rival ! Garde le secret jusqu'à la fin.",
                       $"{targetName ?? "Your target"} got fired: you won, Rival! Keep it secret until the end."), 6f);
        });
        NetSignal.OnLocalPlayer(FailedSignal, () =>
        {
            lostLocal = true;
            Hud.Show(T("Viré… ta rivalité s'arrête là.", "Fired… your rivalry ends here."), 6f);
        });
        // Suivi du signal "tu es Jester" (Jester.OnYou affiche le message de conversion)
        NetSignal.OnLocalPlayer(ConvertSignal, () => { ConvertedLocal = true; RoleKit.RefreshRoleTabs(); });
        NetSignal.On(ConvertedAllSignal, _ => { convertedSeen = true; RoleKit.ApplyResult(); });
        NetSignal.OnLocalPlayer(PromotedSignal, () =>
        {
            mine = false;   // plus de carte ni d'aide du Rival
            Hud.Show(T("Promu manager : ta mission de Rival s'arrête là. Joue le manager normalement.",
                       "Promoted to manager: your Rival mission ends here. Play the manager normally."), 6f);
        });
        NetSignal.On(PromotedAllSignal, _ => { promotedSeen = true; RoleKit.ApplyResult(); });
    }

    // ===================== Serveur (hôte) =====================
    static uint rivalId, targetId;
    static bool won, failed, converted, promoted;

    // Rival en cours de mission (pour l'élection d'un nouveau manager : RoleKit)
    internal static bool IsActive(uint netId) => netId != 0 && netId == rivalId && !won && !failed && !converted && !promoted;

    static void Reset() { rivalId = targetId = 0; won = failed = converted = promoted = false; }

    static bool Playable(LobbyPlayer lp) =>
        lp != null && !lp.isFired && (lp.playerRole == PlayerRole.Specialist || lp.playerRole == PlayerRole.Slacker);

    // Spécialiste ou fainéant, sans sous-rôle de préférence, et au moins une cible possible (un autre spécialiste)
    static LobbyPlayer PickRival(List<LobbyPlayer> players, HashSet<uint> taken)
    {
        var plain = new List<LobbyPlayer>();
        var any = new List<LobbyPlayer>();
        foreach (var lp in players)
        {
            // Sans cible possible (ex. à 3 : le seul spécialiste), il n'est pas candidat : un autre peut l'être
            if (!Playable(lp) || taken.Contains(lp.netId) || Targets(lp.netId).Count == 0) continue;
            any.Add(lp);
            if (lp.subRole == SubRole.None) plain.Add(lp);
        }
        var c = plain.Count > 0 ? plain : any;
        return c.Count == 0 ? null : c[new System.Random().Next(c.Count)];
    }

    static List<LobbyPlayer> Targets(uint rival)
    {
        var l = new List<LobbyPlayer>();
        foreach (var lp in Object.FindObjectsOfType<LobbyPlayer>())
            if (Playable(lp) && lp.playerRole == PlayerRole.Specialist && lp.netId != rival && !Jester.IsJester(lp.netId)) l.Add(lp);
        return l;
    }

    static bool ChooseTarget()
    {
        var l = Targets(rivalId);
        var me = RoleKit.LobbyById(rivalId);
        if (l.Count == 0 || me == null) { Plugin.L.LogInfo("[RIVAL] pas de cible possible : pas de Rival"); return false; }
        targetId = l[new System.Random().Next(l.Count)].netId;
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(me), YouSignal);
        AnnounceTarget();
        Plugin.L.LogInfo("[RIVAL] Rival tiré, cible choisie");   // jamais de nom
        return true;
    }

    // Nom de la cible à tous, par son PlayerInteractable (seul le Rival l'affiche avant la fin)
    static void AnnounceTarget()
    {
        var pi = NetSignal.PlayerInteractableOf(RoleKit.PlayerIdOf(RoleKit.LobbyById(targetId)));
        if (pi != null) NetSignal.Send(pi, TargetSignal);
    }

    // Menu F7 (hôte) : rend ce joueur Rival, cible au hasard
    internal static string Force(LobbyPlayer lp)
    {
        if (!NetworkServer.active || lp == null) return "Hôte uniquement";
        if (!Playable(lp)) return "Le Rival doit être spécialiste ou fainéant";
        Reset();
        rivalId = lp.netId;
        if (!ChooseTarget()) { rivalId = 0; return "Pas de cible possible (il faut un autre spécialiste, hors Jester)"; }
        RoleKit.Announce(Role, lp);
        return null;
    }

    // Menu F7 (hôte) : simule la fuite de la cible (test solo de la conversion en Jester)
    internal static string ForceConvert()
    {
        if (!NetworkServer.active || GameManager.instance == null) return "Hôte uniquement";
        var me = RoleKit.LobbyById(rivalId);
        if (me == null || won || failed || converted) return "Pas de Rival en cours";
        Convert(me, GameManager.instance, "test F7");
        return null;
    }

    static void ServerTick(GameManager gm)
    {
        if (rivalId == 0 || targetId == 0 || won || failed || converted) return;
        if (!gm.IsState(GameStates.InGame) && !gm.IsState(GameStates.Meeting)) return;
        var me = RoleKit.LobbyById(rivalId);
        if (me == null || me.isFired)
        {
            failed = true;   // viré (ou parti) avant d'avoir réussi
            if (me != null) NetSignal.SendToPlayer(RoleKit.PlayerIdOf(me), FailedSignal);
            Plugin.L.LogInfo("[RIVAL] Rival viré : mission échouée");
            return;
        }
        if (me.playerRole == PlayerRole.Manager)
        {
            failed = promoted = true;   // il pourrait virer sa cible lui-même
            NetSignal.SendToPlayer(RoleKit.PlayerIdOf(me), PromotedSignal);
            Plugin.L.LogInfo("[RIVAL] Rival devenu manager : raté");
            return;
        }
        var t = RoleKit.LobbyById(targetId);
        if (t != null && t.isFired)
        {
            won = true;
            NetSignal.SendToPlayer(RoleKit.PlayerIdOf(me), WonSignal);
            // Prévient déjà tout le monde (rien ne s'affiche avant l'écran de fin)
            RoleKit.Announce(Role, me);
            AnnounceTarget();
            RoleKit.SendOutcome(Role, true);
            Plugin.L.LogInfo("[RIVAL] cible virée : gagné");
            return;
        }
        if (t == null) Convert(me, gm, "cible partie");
        else if (t.playerRole == PlayerRole.Manager) Convert(me, gm, "cible devenue manager");
    }

    static void Convert(LobbyPlayer me, GameManager gm, string why)
    {
        converted = true;
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(me), ConvertSignal);
        Jester.MakeConverted(me, gm);
        Plugin.L.LogInfo($"[RIVAL] {why} : le Rival devient Jester");
    }

    // ===================== Client =====================
    static bool mine, wonLocal, lostLocal, introShown, convertedSeen, promotedSeen;
    internal static bool ConvertedLocal;   // le joueur local est un ex-Rival devenu Jester
    static string targetName;               // chez tous ; affiché seulement chez le Rival (et à la fin)

    static string Target => targetName ?? "?";

    static void TryIntro()
    {
        if (!mine || targetName == null || introShown) return;
        introShown = true;
        RoleKit.ShowAfterCard(T($"Tu es le RIVAL ! Fais virer {targetName}. Ton camp ne compte pas pour toi. (F1 : aide)",
                                $"You are the RIVAL! Get {targetName} fired. Your team doesn't matter to you. (F1: help)"), 8f);
    }

    static string BaseName() => RoleKit.LocalLobby()?.playerRole == PlayerRole.Slacker ? T("fainéant", "slacker") : T("spécialiste", "specialist");

    static string ResultLine(bool won)
    {
        if (convertedSeen || ConvertedLocal)
            return won ? T("Rival : {0}, devenu Jester : viré à temps, seul gagnant !", "Rival: {0}, turned Jester: fired in time, sole winner!")
                       : T("Rival : {0}, devenu Jester (raté)", "Rival: {0}, turned Jester (missed)");
        var tn = Target.Replace("{", "{{").Replace("}", "}}");   // ResultLine passe par string.Format
        if (promotedSeen) return T("Rival : {0} (raté, devenu manager)", "Rival: {0} (missed, became manager)");
        return won ? T($"Rival : {{0}} a fait virer {tn} !", $"Rival: {{0}} got {tn} fired!")
                   : T($"Rival : {{0}} (raté), sa cible était {tn}", $"Rival: {{0}} (missed), their target was {tn}");
    }

    static void RoleCard(ShowRoleTab t)
    {
        if (t.roleText != null) t.roleText.text = T("Vous êtes le Rival !", "You are the Rival!");
        if (t.roleDescriptionText != null)
            t.roleDescriptionText.text = T(
                $"Rôle caché et neutre : pour tout le monde, tu es un {BaseName()}, mais ton camp ne compte pas pour toi. Ta seule victoire : faire virer {Target}. Maintiens F1 pour l'aide.",
                $"Hidden neutral role: to everyone you are a {BaseName()}, but your team doesn't matter to you. Your only win: get {Target} fired. Hold F1 for help.");
    }

    static string HelpText()
    {
        string state = wonLocal ? T($"<color={Hex}>Gagné !</color> {Target} a été viré(e). Garde le secret jusqu'à la fin.",
                                    $"<color={Hex}>Won!</color> {Target} got fired. Keep it secret until the end.")
                     : lostLocal ? T("Viré : mission échouée.", "Fired: mission failed.")
                     : T($"Ta cible : <size=150%><color={Hex}>{Target}</color></size>", $"Your target: <size=150%><color={Hex}>{Target}</color></size>");
        return $"<size=170%><color={Hex}>RIVAL</color></size>\n\n" + state + "\n\n" +
            T($"Tu es <b>neutre</b> : pour tout le monde, tu es un {BaseName()} (fais tes tâches pour passer inaperçu), mais <b>la victoire de ton camp ne compte pas pour toi</b>.\n" +
              "Ta seule victoire : que ta cible (toujours un spécialiste) <b>se fasse virer</b>, par n'importe quel moyen.\n" +
              "Personne ne sait que tu es le Rival, pas même ta cible.\n" +
              "Si ta cible quitte la partie ou devient manager, tu deviens <b>Jester</b>. Si tu te fais virer avant, ou si tu deviens manager, c'est raté.",
              $"You are <b>neutral</b>: to everyone you are a {BaseName()} (do your tasks to blend in), but <b>your team's win doesn't matter to you</b>.\n" +
              "Your only win: your target (always a specialist) <b>gets fired</b>, by any means.\n" +
              "Nobody knows you are the Rival, not even your target.\n" +
              "If your target leaves the game or becomes manager, you become a <b>Jester</b>. Fired before that, or promoted to manager: mission failed.");
    }
}
