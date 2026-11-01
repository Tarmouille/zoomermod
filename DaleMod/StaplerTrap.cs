using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Objects.Scripts;
using Player;
using Player.Scripts;
using Player.Tasks;
using Props.Scripts;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Agrafeuse piégée" (noire, Saboteur ; voir TaskPools) : saboter l'agrafeuse d'un poste (2 s, rien ne se voit).
// Tâche validée au sabotage. Le piège se déclenche une fois, sur le premier de ces événements à ce poste :
//  - un joueur s'assoit au poste ;
//  - un joueur met de la gelée sur l'agrafeuse, ou l'enlève (commande du jeu CmdEnableJelly : on sait qui).
// Effet : cri et giclure de sang sur l'agrafeuse pour tous ; la victime est bloquée 10 s (pas de mouvement, caméra
// figée, doigt pointé), libérable plus tôt par un collègue ("Dégager le doigt" sur le poste).
static class StaplerTrap
{
    internal const string Key = "dalemod.agrafeusePiegee";
    internal const string SabotagedAction = "Sabotaged", StaplerObj = "Stapler";
    const float SabotageSeconds = 2f, FreeSeconds = 2f, TrapSeconds = 10f;
    const float SabotagedSignal = -88.1f, TrapOn = -88.2f, VictimSignal = -88.3f, TrapOff = -88.4f, ArmAction = -88.5f, FreeAction = -88.6f;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    static readonly HashSet<IntPtr> armed = new();                        // serveur : postes piégés
    static readonly Dictionary<IntPtr, NetworkIdentity> lastUser = new();  // serveur : occupant précédent de chaque poste
    static readonly Dictionary<IntPtr, float> releaseAt = new();           // serveur : postes dont l'agrafeuse tient un doigt
    static readonly HashSet<IntPtr> trapped = new();                       // tous les clients
    static uint victimNetId;
    static AudioClip scream;

    internal static void Register()
    {
        NetSignal.OnLocalPlayer(SabotagedSignal, () =>
        {
            if (!TaskActive()) return;
            TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, StaplerObj, null, SabotagedAction, null, null));
            Plugin.L.LogInfo("[STAPLER] agrafeuse sabotée");
        });
        NetSignal.On(TrapOn, it =>
        {
            trapped.Add(it.Pointer);
            var ws = it.TryCast<WorkStationController>();
            var at = StaplerPos(ws);
            scream ??= ModAssets.LoadWav("scream.wav");
            ModAudio.Play(scream, at);
            var b = Blood.Bounds(it);
            try { Blood.SplashAt(at, b.floor, 25, 1); } catch (Exception e) { Plugin.L.LogError("[STAPLER] sang : " + e); }
            Plugin.L.LogInfo("[STAPLER] doigt agrafé !");
        });
        NetSignal.On(VictimSignal, it =>
        {
            victimNetId = it.netId;
            if (it.netIdentity == NetworkClient.localPlayer) Lock();
        });
        NetSignal.On(TrapOff, it =>
        {
            trapped.Remove(it.Pointer);
            if (trapped.Count == 0) { victimNetId = 0; Unlock(); }
            Blood.FadeAll();
            Plugin.L.LogInfo("[STAPLER] doigt dégagé");
        });
    }

    // Agrafeuse : la gelée du poste si elle existe (l'agrafeuse y est), sinon le dessus du bureau
    static Vector3 StaplerPos(WorkStationController ws)
    {
        if (ws == null) return Vector3.zero;
        if (ws.jelly != null) return ws.jelly.transform.position + Vector3.up * 0.05f;
        return Blood.Bounds(ws).top;
    }

    // --- Actions sur le poste ---
    [HarmonyPatch(typeof(WorkStationController), nameof(WorkStationController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddActions(WorkStationController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var ws = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Saboter l'agrafeuse", "Sabotage the stapler"), InteractionPermission.All,
            NetSignal.ServerAction(ws, ArmAction, player => { armed.Add(ws.Pointer); lastUser[ws.Pointer] = ws.usingPlayerController; NetSignal.SendToPlayer(player, SabotagedSignal); }),
            false, Show(TaskActive), SabotageSeconds, NoTimer(), NoStart(), false, true));
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Dégager le doigt", "Free the finger"), InteractionPermission.All,
            NetSignal.ServerAction(ws, FreeAction, _ => Release(ws)), false,
            Show(() => trapped.Contains(ws.Pointer) && NetworkClient.localPlayer?.netId != victimNetId),
            FreeSeconds, NoTimer(), NoStart(), false, false));
    }

    // --- Déclencheurs (serveur) ---
    // Gelée mise ou enlevée sur un poste piégé : la victime est celui qui l'a fait
    [HarmonyPatch(typeof(WorkStationController), nameof(WorkStationController.InvokeUserCode_CmdEnableJelly__NetworkIdentity__Boolean__NetworkConnectionToClient))]
    [HarmonyPostfix]
    static void AfterJelly(NetworkBehaviour obj, NetworkConnectionToClient senderConnection)
    {
        var ws = obj?.TryCast<WorkStationController>();
        if (ws == null || !NetworkServer.active || !armed.Contains(ws.Pointer)) return;
        Plugin.L.LogInfo("[STAPLER] gelée touchée sur une agrafeuse piégée");
        Trap(ws, senderConnection?.identity);
    }

    static float nextPoll;
    internal static void Tick()
    {
        if (!NetworkServer.active) return;
        // Libération automatique
        if (releaseAt.Count > 0)
            foreach (var ws in Object.FindObjectsOfType<WorkStationController>())
                if (releaseAt.TryGetValue(ws.Pointer, out var t) && Time.time >= t) Release(ws);
        // Quelqu'un s'assoit à un poste piégé
        if (armed.Count == 0 || Time.time < nextPoll) return;
        nextPoll = Time.time + 0.2f;
        foreach (var ws in Object.FindObjectsOfType<WorkStationController>())
        {
            if (!armed.Contains(ws.Pointer)) continue;
            var user = ws.usingPlayerController;
            lastUser.TryGetValue(ws.Pointer, out var before);
            lastUser[ws.Pointer] = user;
            if (user != null && user != before) { Plugin.L.LogInfo("[STAPLER] quelqu'un s'assoit à un poste piégé"); Trap(ws, user); }
        }
    }

    static void Trap(WorkStationController ws, NetworkIdentity victim)
    {
        if (!armed.Remove(ws.Pointer)) return;   // une seule fois
        var pi = NetSignal.PlayerInteractableOf(victim);
        releaseAt[ws.Pointer] = Time.time + TrapSeconds;
        NetSignal.Send(ws, TrapOn);
        if (pi != null) NetSignal.Send(pi, VictimSignal);
    }

    static void Release(WorkStationController ws)
    {
        if (!NetworkServer.active || !releaseAt.Remove(ws.Pointer)) return;
        NetSignal.Send(ws, TrapOff);
    }

    // --- Blocage de la victime (chez elle) : pas de mouvement, caméra figée, doigt pointé ---
    static bool locked;
    static float savedMove, savedSprint, savedJump, savedSens;
    static PlayerController Me() => NetworkClient.localPlayer?.GetComponent<PlayerController>();

    static void Lock()
    {
        var pc = Me();
        if (pc == null || locked) return;
        locked = true;
        savedMove = pc.moveSpeed; savedSprint = pc.sprintSpeed; savedJump = pc.jumpHeight;
        pc.moveSpeed = 0f; pc.sprintSpeed = 0f; pc.jumpHeight = 0f;
        pc.inputPointing = true;
        if (pc.cameraController != null) { savedSens = pc.cameraController.cameraSensitivity; pc.cameraController.cameraSensitivity = 0f; }
        Plugin.L.LogInfo("[STAPLER] je me suis agrafé le doigt");
    }

    static void Unlock()
    {
        if (!locked) return;
        locked = false;
        var pc = Me();
        if (pc == null) return;
        pc.moveSpeed = savedMove; pc.sprintSpeed = savedSprint; pc.jumpHeight = savedJump;
        pc.inputPointing = false;
        if (pc.cameraController != null) pc.cameraController.cameraSensitivity = savedSens;
    }

    internal static void LateTick()
    {
        if (!locked) return;
        var pc = Me();
        if (pc == null) return;
        pc.inputPointing = true;
        if (pc.cameraController != null) { pc.cameraController.cameraSensitivity = 0f; pc.cameraController.lookInput = Vector2.zero; }
    }

    static Il2CppSystem.Func<bool> Show(Func<bool> f) => DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(f);
    static UnityAction<float> NoTimer() => DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { }));
    static UnityAction<NetworkIdentity> NoStart() => DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { }));
}
