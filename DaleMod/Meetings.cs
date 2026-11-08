using System;
using GameManagement;
using GameManagement.StateMachine;
using HarmonyLib;
using UnityEngine;

namespace DaleMod;

// Déroulé d'une réunion côté serveur (hôte), pour les rôles bonus :
// appel (le manager convoque) -> compte à rebours (15 s pour rejoindre la salle, les retardataires sont juste absents)
// -> vrai début -> fin (retour en jeu). Le jeu ne synchronise pas ces minuteurs : seul le serveur suit le déroulé.
static class Meetings
{
    internal const float DefaultCountdown = 15f;
    internal static event Action<GameManager> Called, Started, Ended;
    internal static bool Proper { get; private set; }   // entre le vrai début et la fin

    static float calledAt = -1f;
    static bool sawMeetingState;

    internal static float Countdown(GameManager gm) => gm != null && gm.meetingCountdown > 0f ? gm.meetingCountdown : DefaultCountdown;

    internal static void Reset() { calledAt = -1f; Proper = sawMeetingState = false; }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ServerCallMeeting))]
    [HarmonyPostfix]
    static void AfterCallMeeting(GameManager __instance) => OnCalled(__instance);

    static void OnCalled(GameManager gm)
    {
        if (calledAt >= 0f) return;
        calledAt = Time.time;
        Proper = false;
        Plugin.L.LogInfo($"[MEETING] réunion appelée (état {(gm.IsState(GameStates.Meeting) ? "Meeting" : "autre")}, active {gm.meetingActive}, " +
                         $"compte à rebours {gm.meetingCountdown:0.0} / {gm.meetingCountdownTimer:0.0})");
        Raise(Called, gm);
    }

    // Appelé par RoleKit chaque image, sur le serveur
    internal static void ServerTick(GameManager gm)
    {
        if (gm.IsState(GameStates.WaitingForPlayerConnections)) { Reset(); return; }
        bool meeting = gm.IsState(GameStates.Meeting);
        if (meeting && !sawMeetingState) Plugin.L.LogInfo($"[MEETING] état Meeting (active {gm.meetingActive}, compte à rebours {gm.meetingCountdownTimer:0.0})");
        if (meeting && calledAt < 0f) OnCalled(gm);   // appel qui n'est pas passé par ServerCallMeeting
        sawMeetingState = meeting;

        if (calledAt >= 0f && !Proper && Time.time - calledAt >= Countdown(gm))
        {
            Proper = true;
            Plugin.L.LogInfo($"[MEETING] début réel de la réunion (active {gm.meetingActive})");
            Raise(Started, gm);
        }
        if (calledAt >= 0f && Proper && !meeting && !gm.IsState(GameStates.Meeting))
        {
            calledAt = -1f;
            Proper = false;
            Plugin.L.LogInfo("[MEETING] fin de réunion");
            Raise(Ended, gm);
        }
    }

    static void Raise(Action<GameManager> e, GameManager gm)
    {
        if (e == null) return;
        foreach (Action<GameManager> h in e.GetInvocationList())
            try { h(gm); } catch (Exception ex) { Plugin.L.LogError("[MEETING] " + ex); }
    }
}
