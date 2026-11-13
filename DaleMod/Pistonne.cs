using System;
using System.Collections.Generic;
using GameManagement;
using GameManagement.StateMachine;
using HarmonyLib;
using Mirror;
using Player.Lobby;
using UnityEngine;

namespace DaleMod;

// Rôle bonus caché "Pistonné" (RoleKit) : le neveu du patron, spécialiste ou fainéant, qui gagne avec son camp.
// - Seulement dans les parties à 4 licenciements ou plus (sauf tirage forcé en debug). De préférence un joueur sans
//   sous-rôle, sinon RH ou analyste (il garde ses pouvoirs ; sa carte de rôle reste celle du Pistonné, en orange).
// - Caché de tous jusqu'à son premier renvoi.
// - 1er renvoi par le manager : gratuit. Personne n'est viré, le licenciement n'est pas décompté, la réunion se termine
//   et tous apprennent "X est le neveu du patron" (pas son camp).
// - 2e renvoi : il est viré pour de bon, et la sanction dépend de son camp (il part, son camp n'a plus à rester caché) :
//   spécialiste -> un licenciement de malus en plus (même le dernier : le jeu décide alors de la fin) ;
//   fainéant -> +10 % de productivité.
// - Pas de mission : l'écran de fin dit juste si la protection a servi. Il ne colore pas l'écran de fin.
// - Une rétrogradation de manager (vote de remplacement) n'est pas un renvoi : la protection ne s'y applique pas.
static class Pistonne
{
    // Signaux propres : "tu es le Pistonné" (à lui seul), "renvoi bloqué", "viré pour de bon" (à tous)
    const float YouSignal = -9400f, SavedSignal = -9401f, FiredSpecialistSignal = -9402f, FiredSlackerSignal = -9403f;
    static readonly Color Orange = new(0.859f, 0.463f, 0.004f);   // #DB7601
    const string Hex = "#DB7601";
    internal const int MinFireLimit = 4;     // licenciements autorisés dans la partie pour tirer le rôle
    const float SlackerBonus = 10f;          // points de productivité gagnés en virant un Pistonné fainéant
    static string T(string fr, string en) => RoleKit.T(fr, en);

    internal static BonusRole Role;

    // Frame du renvoi bloqué : les autres patchs de ServerFirePlayer (tours du Jester) l'ignorent
    internal static int BlockedFrame = -1;

    internal static void Register()
    {
        Role = new BonusRole
        {
            Id = "pistonne", Name = "Pistonné", SettingKey = "ZoomerMod Pistonne", Color = Orange, SignalBase = -9470f,
            Pick = PickOf,
            Assign = (lp, _) => Make(lp),
            ServerReset = () => { pistonneId = 0; used = false; },
            ServerOutcome = () => pistonneId == 0 ? null : used,
            ServerHolder = () => pistonneId,
            ClientReset = () => { mine = savedLocal = false; },
            LocalIsHolder = () => mine,
            LocalHasHelp = () => mine,
            HelpText = HelpText,
            RoleCard = RoleCard,
            // Fond orange chez le Pistonné seul, s'il est encore là et que son camp gagne
            LocalResult = (rt, winner) =>
            {
                var me = RoleKit.LocalLobby();
                if (me != null && !me.isFired && me.playerRole == winner) RoleKit.TintResult(rt, Orange);
            },
            ResultLine = saved => saved ? T("Pistonné : {0}, sauvé par son oncle !", "Nepo baby: {0}, saved by their uncle!")
                                        : T("Pistonné : {0} (jamais inquiété)", "Nepo baby: {0} (never in trouble)"),
        };
        RoleKit.Add(Role);

        NetSignal.OnLocalPlayer(YouSignal, () =>
        {
            mine = true;
            RoleKit.RefreshRoleTabs();
            RoleKit.ShowAfterCard(T($"Tu es le PISTONNÉ ! Le neveu du patron : ton premier renvoi échouera. Tu gagnes avec ton camp.{AlsoSub()} (F1 : aide)",
                       $"You are the NEPO BABY! The boss's nephew: your first firing will fail. You win with your team.{AlsoSub()} (F1: help)"), 8f);
        });
        // Chez tous : le nom arrive juste avant (identité envoyée par RoleKit.Announce)
        NetSignal.On(SavedSignal, _ =>
        {
            if (mine) savedLocal = true;
            var name = Role.HolderName ?? "?";
            Hud.Show(T($"{name} est le neveu du patron ! Le renvoi est annulé.",
                       $"{name} is the boss's nephew! The firing is cancelled."), 7f);
            RoleKit.RefreshRoleTabs();
        });
        NetSignal.On(FiredSpecialistSignal, _ => Hud.Show(T(
            "Vous avez vraiment viré le neveu du patron !\nCe spécialiste avait le bon profil, mais pas le bon arbre généalogique. Cette décision vous coûtera cher !",
            "You really fired the boss's nephew!\nThis specialist had the right profile, but not the right family tree. This decision will cost you dearly!"), 9f));
        NetSignal.On(FiredSlackerSignal, _ => Hud.Show(T(
            "Vous avez vraiment viré le neveu du patron !\nLa méritocratie a ses raisons que le piston ignore, et la boutique avait besoin d'un soldat, pas d'un touriste.",
            "You really fired the boss's nephew!\nMerit has its reasons that nepotism knows nothing of: the shop needed a soldier, not a tourist."), 9f));
    }

    // ===================== Serveur (hôte) =====================
    static uint pistonneId;
    static bool used;
    static uint firingAgain;   // netId du Pistonné pendant son 2e renvoi (préfixe -> postfixe)
    static bool againSlacker;  // son camp, lu AVANT le renvoi (le jeu change le rôle du viré)

    static bool Playable(LobbyPlayer lp) =>
        lp != null && !lp.isFired && (lp.playerRole == PlayerRole.Specialist || lp.playerRole == PlayerRole.Slacker)
        && lp.subRole != SubRole.Assistant;

    // Spécialiste ou fainéant pas déjà pris : sans sous-rôle de préférence, sinon RH ou analyste ;
    // seulement à 4 licenciements ou plus, sauf tirage forcé (debug)
    static LobbyPlayer PickOf(List<LobbyPlayer> players, HashSet<uint> taken, bool forced)
    {
        int limit = GameManager.instance != null ? GameManager.instance.fireLimit : 0;
        if (!forced && limit < MinFireLimit)
        {
            Plugin.L.LogInfo($"[PISTONNE] pas de Pistonné : {limit} licenciement(s) autorisé(s), il en faut {MinFireLimit}");
            return null;
        }
        var plain = new List<LobbyPlayer>();
        var sub = new List<LobbyPlayer>();
        foreach (var lp in players)
        {
            if (!Playable(lp) || taken.Contains(lp.netId)) continue;
            (lp.subRole == SubRole.None ? plain : sub).Add(lp);
        }
        var c = plain.Count > 0 ? plain : sub;
        return c.Count == 0 ? null : c[new System.Random().Next(c.Count)];
    }

    static void Make(LobbyPlayer lp)
    {
        pistonneId = lp.netId;
        used = false;
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), YouSignal);
        Plugin.L.LogInfo("[PISTONNE] Pistonné tiré");   // jamais de nom
    }

    // Menu F7 (hôte) : rend ce joueur Pistonné (sans condition de licenciements)
    internal static string Force(LobbyPlayer lp)
    {
        if (!NetworkServer.active || lp == null) return "Hôte uniquement";
        if (!Playable(lp)) return "Le Pistonné doit être spécialiste ou fainéant (pas assistant)";
        Make(lp);
        RoleKit.Announce(Role, lp);
        return null;
    }

    // 1er renvoi du Pistonné : bloqué et gratuit. 2e renvoi : on le laisse passer et la sanction suit (postfixe).
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ServerFirePlayer))]
    [HarmonyPrefix]
    static bool BeforeFire(GameManager __instance, NetworkIdentity player, bool onlyDemoteOrFire)
    {
        firingAgain = 0;
        if (onlyDemoteOrFire || pistonneId == 0) return true;
        var lp = RoleKit.LobbyOf(player);
        if (lp == null || lp.netId != pistonneId || lp.playerRole == PlayerRole.Manager) return true;
        if (used) { firingAgain = lp.netId; againSlacker = lp.playerRole == PlayerRole.Slacker; return true; }
        used = true;
        BlockedFrame = Time.frameCount;
        RoleKit.Announce(Role, lp);
        var via = WhiteBoardController.instance;
        if (via != null) NetSignal.Send(via, SavedSignal);
        Plugin.L.LogInfo($"[PISTONNE] minuteur de licenciement avant fin de réunion : {__instance.terminationTimer:0.0} / {__instance.terminationMaxTime:0.0}");
        try { if (__instance.IsState(GameStates.Meeting)) __instance.ServerFinnishMeeting(); }
        catch (Exception e) { Plugin.L.LogError("[PISTONNE] fin de réunion : " + e); }
        // Renvoi bloqué, mais le manager a bien viré quelqu'un : son minuteur repart comme après un vrai renvoi
        ManagerTimer.Reset(__instance, "Pistonné sauvé");
        return false;
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ServerFirePlayer))]
    [HarmonyPostfix]
    static void AfterFire(GameManager __instance)
    {
        if (firingAgain == 0) return;
        var lp = RoleKit.LobbyById(firingAgain);
        firingAgain = 0;
        if (lp == null || !lp.isFired) return;
        Plugin.L.LogInfo($"[PISTONNE] minuteur de licenciement après un vrai renvoi : {__instance.terminationTimer:0.0} / {__instance.terminationMaxTime:0.0}");
        var via = WhiteBoardController.instance;
        try
        {
            if (againSlacker)
            {
                float before = __instance.finalProductivityMeter;
                __instance.productivity += SlackerBonus / 100f;
                if (via != null) NetSignal.Send(via, FiredSlackerSignal);
                __instance.UpdateProductivity();   // peut faire gagner les spécialistes
                Plugin.L.LogInfo($"[PISTONNE] Pistonné fainéant viré : productivité {before:0.00} -> {__instance.finalProductivityMeter:0.00}");
            }
            else if (CostKiller.FiringNow)
            {
                // Viré par le Cost Killer : c'est lui qui paie l'erreur (il est viré à son tour), pas le manager
                Plugin.L.LogInfo("[PISTONNE] Pistonné spécialiste viré par le Cost Killer : pas de licenciement de malus");
            }
            else
            {
                int before = __instance.fireLimit;
                __instance.NetworkfireLimit = Mathf.Max(0, before - 1);   // SyncVar : juste chez les invités aussi
                if (via != null) NetSignal.Send(via, FiredSpecialistSignal);
                __instance.EndGameIfFinished();   // c'était peut-être le dernier licenciement
                Plugin.L.LogInfo($"[PISTONNE] Pistonné spécialiste viré : licenciements autorisés {before} -> {__instance.fireLimit}");
            }
        }
        catch (Exception e) { Plugin.L.LogError("[PISTONNE] sanction du 2e renvoi : " + e); }
    }

    // ===================== Client =====================
    static bool mine, savedLocal;

    static string Team() => RoleKit.LocalLobby()?.playerRole == PlayerRole.Slacker ? T("fainéant", "slacker") : T("spécialiste", "specialist");

    static void RoleCard(ShowRoleTab t)
    {
        if (t.roleText != null) t.roleText.text = T("Vous êtes le Pistonné !", "You are the Nepo Baby!");
        if (t.roleDescriptionText != null)
            t.roleDescriptionText.text = T(
                $"Le neveu du patron, {Team()}. Ton premier renvoi échouera. Tu gagnes avec ton camp.{AlsoSub()} Maintiens F1 pour l'aide.",
                $"The boss's nephew, {Team()}. Your first firing will fail. You win with your team.{AlsoSub()} Hold F1 for help.");
    }

    // Pistonné cumulé avec RH ou analyste : la carte est celle du Pistonné, on rappelle l'autre sous-rôle
    static string AlsoSub() => RoleKit.LocalLobby()?.subRole switch
    {
        SubRole.HrRep => T(" Tu es aussi RH : tes pouvoirs restent les mêmes.", " You're also the HR Rep: your powers are unchanged."),
        SubRole.Analyst => T(" Tu es aussi analyste : tes pouvoirs restent les mêmes.", " You're also the Analyst: your powers are unchanged."),
        _ => "",
    };

    static string HelpText()
    {
        string title = $"<size=170%><color={Hex}>{T("PISTONNÉ", "NEPO BABY")}</color></size>\n\n";
        string state = savedLocal ? T("Ton oncle t'a déjà sauvé : <b>le prochain renvoi sera le bon</b>.", "Your uncle already saved you: <b>the next firing will stick</b>.")
                                  : T($"<color={Hex}>Protégé</color> : ton premier renvoi échouera.", $"<color={Hex}>Protected</color>: your first firing will fail.");
        return title + state + "\n\n" +
            T($"Tu joues ton camp normalement ({Team()}) et tu gagnes avec lui.{AlsoSub()}\n" +
              "La première fois que le manager te vire, ça échoue : tout le monde apprend que tu es le neveu du patron (pas ton camp), la réunion se termine et le renvoi ne compte pas.\n" +
              "S'il te vire une 2e fois, tu pars pour de bon. Si tu es spécialiste, il perd un licenciement de plus ; si tu es fainéant, l'entreprise gagne 10 % de productivité.",
              $"You play your team normally ({Team()}) and win with it.{AlsoSub()}\n" +
              "The first time the manager fires you, it fails: everyone learns you're the boss's nephew (not your team), the meeting ends and the firing doesn't count.\n" +
              "If they fire you a 2nd time, you're out for good. If you're a specialist, they lose one more firing; if you're a slacker, the company gains 10% productivity.");
    }
}
