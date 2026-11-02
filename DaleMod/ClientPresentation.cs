using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Player;
using Player.Tasks;
using Props.Scripts;
using Room;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Présentation client" (bleue, Specialist ; voir TaskPools) :
// 1) préparer sa présentation au tableau blanc des réunions (action, 3 s) ;
// 2) la faire pendant 15 s dans la salle de réunion (la pièce du tableau blanc), et nulle part ailleurs : s'il sort de
//    la salle, le compteur repart à zéro (la présentation reste préparée).
// Repli si la salle est introuvable : à moins de 4 m du tableau blanc.
static class ClientPresentation
{
    internal const string Key = "dalemod.presentationClient";
    internal const string PreparedAction = "Prepared", PresentedAction = "Presented", BoardObj = "Presentation";
    const float PrepareSeconds = 3f, Needed = 15f, Poll = 0.25f, FallbackDistance = 4f;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    static IntPtr taskPtr;
    static bool prepared, done;
    static float progress, nextPoll;

    static void SyncTask()
    {
        var p = CustomTaskPatches.ActiveTask()?.Pointer ?? IntPtr.Zero;
        if (p == taskPtr) return;
        taskPtr = p; prepared = done = false; progress = 0f;
    }

    // --- Tableau blanc : "Préparer la présentation" (chez le joueur, rien à faire côté serveur) ---
    [HarmonyPatch(typeof(WhiteBoardController), nameof(WhiteBoardController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddPrepare(WhiteBoardController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Préparer la présentation", "Prepare the presentation"), InteractionPermission.All,
            DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity, NetworkConnectionToClient>>(new Action<NetworkIdentity, NetworkConnectionToClient>((_, _) =>
            {
                SyncTask();
                if (!TaskActive() || prepared) return;
                prepared = true;
                TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, BoardObj, null, PreparedAction, null, null));
                Hud.Show(CustomTaskPatches.Text("Présentation prête : présente-la 15 s dans la salle de réunion.",
                                                "Presentation ready: give it for 15 s in the meeting room."), 4f);
                Plugin.L.LogInfo("[PRES] présentation préparée");
            })),
            false,
            DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(new Func<bool>(() => { SyncTask(); return TaskActive() && !prepared; })),
            PrepareSeconds,
            DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { })),
            DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { })),
            false, false));
    }

    // --- 15 s dans la salle de réunion ---
    internal static void Tick()
    {
        SyncTask();
        if (!TaskActive() || !prepared || done || Time.time < nextPoll) return;
        nextPoll = Time.time + Poll;
        var me = NetworkClient.localPlayer?.GetComponent<PlayerController>();
        var board = Object.FindObjectOfType<WhiteBoardController>();
        if (me == null || board == null) return;

        bool inside;
        var room = MeetingRoom(board);
        if (room != null) inside = Rooms.Of(me) == room;   // pièce d'après la position (Rooms)
        else inside = Vector3.Distance(me.transform.position, board.transform.position) < FallbackDistance;

        if (!inside)
        {
            if (progress > 0f)
            {
                Hud.Show(CustomTaskPatches.Text("Tu as quitté la salle : la présentation reprend de zéro.", "You left the room: the presentation starts over."), 3f);
                Plugin.L.LogInfo($"[PRES] sorti de la salle à {progress:0} s");
            }
            progress = 0f;
            return;
        }
        progress += Poll;
        Hud.Show(CustomTaskPatches.Text($"Présentation : {Mathf.Min(progress, Needed):0}/{Needed:0} s", $"Presentation: {Mathf.Min(progress, Needed):0}/{Needed:0} s"), 0.6f);
        if (progress < Needed) return;
        done = true;
        TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, BoardObj, null, PresentedAction, null, null));
        Hud.Show(CustomTaskPatches.Text("Présentation terminée. Le client est conquis (ou endormi).", "Presentation done. The client is won over (or asleep)."), 3f);
        Plugin.L.LogInfo("[PRES] présentation terminée");
    }

    static RoomController meetingRoom;
    static bool logged;
    static RoomController MeetingRoom(WhiteBoardController board)
    {
        if (meetingRoom != null) return meetingRoom;
        meetingRoom = Rooms.Of(board);
        if (!logged) { logged = true; Plugin.L.LogInfo($"[PRES] salle de réunion : {(meetingRoom == null ? "introuvable (repli 4 m)" : $"'{meetingRoom.roomName}' / '{meetingRoom.subRoomName}'")}"); }
        return meetingRoom;
    }
}
