using System;
using GameManagement;
using HarmonyLib;
using Mirror;
using Player.Lobby;
using UnityEngine;

namespace DaleMod;

// Rôle bonus "Syndicaliste" (RoleKit) : un FAINÉANT dont la mission est de faire renverser le manager par le vote de
// remplacement du jeu (n'importe quel vote réussi compte).
// - Connu des autres fainéants (ils sont prévenus de son nom), inconnu des autres.
// - Tiré seulement si les fainéants n'ont pas la majorité ; vérifié aussi au moment du vote (sinon il ne compte pas).
// - Viré avant d'avoir réussi : mission échouée (le rôle n'est jamais transmis).
// - Récompense : badge sur l'écran de fin ; la victoire de son camp ne change pas.
static class Syndic
{
    // Signaux propres au Syndicaliste : "tu es le Syndicaliste" / "réussi" (au Syndicaliste seul)
    const float YouSignal = -9200f, DoneSignal = -9201f, FailedSignal = -9202f;
    static readonly Color Yellow = new(1f, 0.831f, 0f);   // #FFD400
    const string Hex = "#FFD400";
    static string T(string fr, string en) => RoleKit.T(fr, en);

    internal static BonusRole Role;

    internal static void Register()
    {
        Role = new BonusRole
        {
            Id = "syndic", Name = "Syndicaliste", SettingKey = "ZoomerMod Syndic", Color = Yellow, SignalBase = -9270f,
            Pick = (players, taken, forced) =>
            {
                if (!forced && !NoSlackerMajority()) { Plugin.L.LogInfo("[SYNDIC] pas de Syndicaliste : majorité de fainéants"); return null; }
                return RoleKit.PickOf(players, taken, PlayerRole.Slacker);
            },
            Assign = (lp, _) => Make(lp),
            ServerTick = _ => ServerTick(),
            ServerReset = () => { syndicId = 0; succeeded = failed = false; },
            ServerOutcome = () => syndicId == 0 ? null : succeeded,
            ServerHolder = () => syndicId,
            ClientReset = () => { mine = false; done = lost = false; knownAs = null; },
            LocalIsHolder = () => mine,
            LocalHasHelp = () => mine || knownAs != null,
            HelpText = HelpText,
            RoleCard = RoleCard,
            OnIdentity = OnIdentity,
            // Texte du camp gagnant gardé. Couleur de l'écran de fin : Jester > Rival > Syndicaliste (simple badge)
            // Fond jaune chez le Syndicaliste seul, s'il a réussi (les autres gardent la couleur du camp gagnant)
            LocalResult = (rt, _) => { if (done) RoleKit.TintResult(rt, Yellow); },
            ResultLine = won => won ? T("Syndicaliste : {0} a fait tomber le manager !", "Union Rep: {0} brought the manager down!")
                                    : T("Syndicaliste : {0} (raté)", "Union Rep: {0} (missed)"),
        };
        RoleKit.Add(Role);

        NetSignal.OnLocalPlayer(YouSignal, () =>
        {
            mine = true;
            RoleKit.RefreshRoleTabs();
            RoleKit.ShowAfterCard(T("Tu es le SYNDICALISTE ! Fais renverser le manager par un vote. Les fainéants savent qui tu es. (F1 : aide)",
                       "You are the UNION REP! Get the manager voted out. The slackers know who you are. (F1: help)"), 8f);
        });
        NetSignal.OnLocalPlayer(DoneSignal, () =>
        {
            done = true;
            Hud.Show(T("Le manager est tombé : le syndicat a gagné !", "The manager is down: the union won!"), 6f);
        });
        NetSignal.OnLocalPlayer(FailedSignal, () =>
        {
            lost = true;
            Hud.Show(T("Viré… la lutte syndicale s'arrête là pour toi.", "Fired… the union struggle ends here for you."), 6f);
        });
    }

    // ===================== Serveur (hôte) =====================
    static uint syndicId;
    static bool succeeded, failed;

    // Pas de majorité de fainéants parmi les joueurs encore en jeu
    static bool NoSlackerMajority()
    {
        var (players, slackers) = RoleKit.InGameCounts();
        return slackers * 2 <= players;
    }

    static void Make(LobbyPlayer lp)
    {
        syndicId = lp.netId;
        succeeded = failed = false;
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), YouSignal);
        Plugin.L.LogInfo("[SYNDIC] Syndicaliste tiré");   // jamais de nom
    }

    // Menu F7 (hôte) : rend ce joueur Syndicaliste (il doit être fainéant)
    internal static string Force(LobbyPlayer lp)
    {
        if (!NetworkServer.active || lp == null) return "Hôte uniquement";
        if (lp.playerRole != PlayerRole.Slacker) return "Le Syndicaliste doit être un fainéant (Slacker)";
        Make(lp);
        RoleKit.Announce(Role, lp);
        return null;
    }

    static void ServerTick()
    {
        if (syndicId == 0 || succeeded || failed) return;
        var lp = RoleKit.LobbyById(syndicId);
        if (lp != null && !lp.isFired) return;
        failed = true;   // viré (ou parti) avant d'avoir réussi
        if (lp != null) NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), FailedSignal);
        Plugin.L.LogInfo("[SYNDIC] Syndicaliste viré : mission échouée");
    }

    // Manager renversé par le vote de remplacement du jeu
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ReplaceManager))]
    [HarmonyPostfix]
    static void AfterReplaceManager()
    {
        Plugin.L.LogInfo("[ROLES] manager remplacé par vote");
        if (syndicId == 0 || succeeded || failed) return;
        var lp = RoleKit.LobbyById(syndicId);
        if (lp == null || lp.isFired) return;   // ServerTick le marquera raté
        if (!NoSlackerMajority()) { Plugin.L.LogInfo("[SYNDIC] manager renversé, mais majorité de fainéants : ne compte pas"); return; }
        succeeded = true;
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), DoneSignal);
        RoleKit.Announce(Role, lp);
        RoleKit.SendOutcome(Role, true);
        Plugin.L.LogInfo("[SYNDIC] manager renversé : mission réussie");
    }

    // ===================== Client =====================
    static bool mine, done, lost;
    static string knownAs;   // chez un autre fainéant : nom du Syndicaliste

    static void OnIdentity(string name)
    {
        if (mine || name == null) return;
        var me = RoleKit.LocalLobby();
        if (me == null || me.playerRole != PlayerRole.Slacker) return;   // seuls les fainéants le savent
        if (knownAs == name) return;
        knownAs = name;
        RoleKit.ShowAfterCard(T($"{name} est le Syndicaliste de votre camp : aidez-le à faire renverser le manager par un vote ! (F1)",
                   $"{name} is your team's Union Rep: help them get the manager voted out! (F1)"), 8f);
    }

    static void RoleCard(ShowRoleTab t)
    {
        if (t.roleText != null) t.roleText.text = T("Vous êtes le Syndicaliste !", "You are the Union Rep!");
        if (t.roleDescriptionText != null)
            t.roleDescriptionText.text = T(
                "Fainéant et fier de l'être. Ta mission : faire renverser le manager par un vote de remplacement. Les autres fainéants savent qui tu es. Maintiens F1 pour l'aide.",
                "Slacker and proud of it. Your mission: get the manager replaced by a vote. The other slackers know who you are. Hold F1 for help.");
    }

    static string HelpText()
    {
        string title = $"<size=170%><color={Hex}>{T("SYNDICALISTE", "UNION REP")}</color></size>\n\n";
        if (!mine)
            return title + T($"<color={Hex}>{knownAs}</color> est le Syndicaliste de votre camp.\n" +
                             "Aide-le à faire <b>renverser le manager par un vote</b> : ça lui vaut un badge à la fin de la partie.",
                             $"<color={Hex}>{knownAs}</color> is your team's Union Rep.\n" +
                             "Help them get the <b>manager voted out</b>: it earns them a badge at the end of the game.");
        string state = done ? T($"<color={Hex}>Manager renversé !</color> Badge assuré à la fin de la partie.", $"<color={Hex}>Manager down!</color> Badge secured for the end of the game.")
                     : lost ? T("Viré : mission échouée.", "Fired: mission failed.")
                     : T("Le manager tient toujours.", "The manager is still standing.");
        return title + state + "\n\n" +
            T("Tu es un <b>fainéant</b> : tu gagnes ou perds avec ton camp.\n" +
              "Ta mission perso : faire <b>renverser le manager par un vote de remplacement</b> (peu importe qui le lance).\n" +
              "Les autres fainéants savent qui tu es ; personne d'autre.\n" +
              "Ça ne compte que si les fainéants n'ont pas la majorité au moment du vote. Si tu te fais virer avant, c'est raté.\n" +
              "Réussi : badge à l'écran de fin.",
              "You are a <b>slacker</b>: you win or lose with your team.\n" +
              "Your personal mission: get the <b>manager replaced by a vote</b> (whoever starts it).\n" +
              "The other slackers know who you are; nobody else does.\n" +
              "It only counts if the slackers don't hold the majority at vote time. Fired before that: mission failed.\n" +
              "Success: a badge on the end screen.");
    }
}
