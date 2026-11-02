using System;
using System.Collections.Generic;
using Mirror;
using Player;
using Player.Lobby;
using Player.Tasks;
using Room;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// "1 to 1" : 15 s en tête-à-tête dans le bureau d'un "chef" (Manager, ou Assistant s'il y en a un), personne d'autre
// dans la pièce. Si quelqu'un d'autre est dans le bureau, ou si l'un des deux en sort, le compteur repart à zéro.
// - "1 to 1" (bleue, Specialist ; pas pour le Manager ; pas tirée sans chef) : avec un chef, dans SON bureau.
// - "1 to 1 Manager" (Manager ou Assistant) : avec n'importe quel autre joueur, dans MON bureau.
// Bureau = la pièce (Room.RoomController) qui contient le poste du chef ; l'Assistant n'a pas de bureau : il utilise
// celui du Manager. Pièce d'un joueur = son RoomTrigger.
// Repli si la pièce est introuvable : à moins de 3 m l'un de l'autre, personne d'autre à 3 m.
static class OneOnOne
{
    internal const string Key = "dalemod.oneToOne", ManagerKey = "dalemod.oneToOneManager";
    internal const string MetAction = "Met", ManagerObj = "Manager", EmployeeObj = "Employee";
    const float Needed = 15f, Poll = 0.25f, FallbackDistance = 3f;

    static bool Active(string key) => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), key);

    // Chef : Manager, ou Specialist au sous-rôle Assistant
    internal static bool IsBoss(LobbyPlayer lp) => lp != null && (lp.playerRole == PlayerRole.Manager || lp.subRole == SubRole.Assistant);

    // Un chef autre que `except` (le joueur qui recevrait la tâche) est dans la partie
    internal static bool BossPresent(LobbyPlayer except = null)
    {
        foreach (var lp in Object.FindObjectsOfType<LobbyPlayer>())
            if (IsBoss(lp) && (except == null || lp.Pointer != except.Pointer)) return true;
        return false;
    }

    static IntPtr taskPtr;
    static float progress, nextPoll;
    static bool done;
    static readonly HashSet<IntPtr> loggedOffices = new();

    internal static void Tick()
    {
        var p = CustomTaskPatches.ActiveTask()?.Pointer ?? IntPtr.Zero;
        if (p != taskPtr) { taskPtr = p; progress = 0f; done = false; }
        bool employee = Active(Key), boss = Active(ManagerKey);
        if ((!employee && !boss) || done || Time.time < nextPoll) return;
        nextPoll = Time.time + Poll;

        var me = NetworkClient.localPlayer?.GetComponent<PlayerController>();
        if (me == null) { Reset(); return; }
        var players = new List<PlayerController>();
        foreach (var pc in Object.FindObjectsOfType<PlayerController>()) if (pc.Pointer != me.Pointer) players.Add(pc);

        bool ok = false;
        if (employee)
        {
            // Avec un chef (n'importe lequel), dans son bureau
            foreach (var chief in players)
                if (IsBoss(chief.lobbyPlayer) && TogetherAlone(me, chief, chief, players)) { ok = true; break; }
        }
        else
        {
            // Je suis le chef : avec n'importe quel autre joueur, dans mon bureau
            foreach (var guest in players)
                if (TogetherAlone(me, guest, me, players)) { ok = true; break; }
        }

        if (!ok) { Reset(); return; }
        progress += Poll;
        Hud.Show(CustomTaskPatches.Text($"1 to 1 : {Mathf.Min(progress, Needed):0}/{Needed:0} s", $"1 to 1: {Mathf.Min(progress, Needed):0}/{Needed:0} s"), 0.6f);
        if (progress < Needed) return;
        done = true;
        TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, employee ? ManagerObj : EmployeeObj, null, MetAction, null, null));
        Hud.Show(CustomTaskPatches.Text("1 to 1 terminé. Merci pour ce point très constructif.", "1 to 1 done. Thanks for this very constructive meeting."), 3f);
        Plugin.L.LogInfo("[1TO1] entretien terminé");
    }

    // a et b ensemble dans le bureau du propriétaire `owner`, et aucun autre joueur dedans
    static bool TogetherAlone(PlayerController a, PlayerController b, PlayerController owner, List<PlayerController> players)
    {
        var office = OfficeOf(owner);
        if (office != null)
        {
            if (RoomOf(a) != office || RoomOf(b) != office) return false;
            foreach (var o in players) if (o.Pointer != b.Pointer && RoomOf(o) == office) return false;
            return true;
        }
        if (Vector3.Distance(a.transform.position, b.transform.position) >= FallbackDistance) return false;
        foreach (var o in players)
            if (o.Pointer != b.Pointer && Vector3.Distance(o.transform.position, owner.transform.position) < FallbackDistance) return false;
        return true;
    }

    static void Reset()
    {
        if (progress > 0f) Plugin.L.LogInfo($"[1TO1] entretien interrompu à {progress:0} s");
        progress = 0f;
    }

    static RoomController RoomOf(PlayerController pc) => Rooms.Of(pc);   // pièce d'après la position (Rooms)

    // Bureau d'un joueur : la pièce qui contient son poste. L'Assistant n'a pas de bureau : celui du Manager.
    static RoomController OfficeOf(PlayerController owner)
    {
        var lp = owner.lobbyPlayer;
        if (lp != null && lp.playerRole != PlayerRole.Manager && lp.subRole == SubRole.Assistant)
            foreach (var other in Object.FindObjectsOfType<LobbyPlayer>())
                if (other.playerRole == PlayerRole.Manager) { lp = other; break; }
        var desk = lp?.workStationController;
        if (desk == null) return null;
        var found = Rooms.At(desk.transform.position + Vector3.up * 0.5f);
        if (loggedOffices.Add(owner.Pointer))
            Plugin.L.LogInfo($"[1TO1] bureau de {owner.lobbyPlayer?.username} : {(found == null ? "pièce introuvable (repli 3 m)" : $"'{found.roomName}' / '{found.subRoomName}'")}");
        return found;
    }
}
