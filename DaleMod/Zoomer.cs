using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Player;
using Player.Scripts;
using Player.Tasks;
using Props.PaperShredder;
using Props.Printer;
using Props.Scripts;
using UnityEngine;
using UnityEngine.Events;
using Il2CppInterop.Runtime;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Attention Mr. Zoomer" (Saboteur) : saboter le broyeur (2 s). Rien ne l'indique et on ne peut pas le réparer d'avance.
// Le prochain joueur qui broie un document (une seule fois) a la main coincée : immobile 60 s max, cri + alarme de bourrage
// de l'imprimante audibles par tous, éclaboussure de sang (Blood) ; la victime pointe du doigt de force (la main "coincée" dans le broyeur). Un autre joueur peut le dégager plus tôt depuis le broyeur.
// L'armement n'est connu que du serveur ; piège / victime / libération sont diffusés par NetSignal.
static class Zoomer
{
    internal const string Key = "dalemod.attentionZoomer";
    internal const string SabotagedAction = "Sabotaged", ShredderObj = "Paper Shredder";
    const float SabotagedSignal = -61.1f, TrapOn = -61.2f, VictimSignal = -61.3f, TrapOff = -61.4f, ArmAction = -61.5f, FreeAction = -61.6f;
    const float SabotageSeconds = 2f, FreeSeconds = 3f, TrapSeconds = 60f, Near = 3f;

    internal static TaskHook Hook(string action, string obj) => new(null, obj, null, action, null, null);
    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    static readonly HashSet<IntPtr> armed = new();        // serveur seulement : broyeurs sabotés, piège pas encore déclenché
    static readonly Dictionary<IntPtr, float> releaseAt = new();   // serveur : broyeurs piégés -> libération automatique
    static readonly HashSet<IntPtr> trapped = new();      // tous les clients : broyeurs qui tiennent une main
    static readonly Dictionary<IntPtr, AudioSource> alarms = new();
    static uint victimNetId;                               // tous les clients : 0 = personne
    static AudioClip scream;

    // Chez la victime : position imposée et vitesses d'origine
    static bool locked;
    static Vector3 lockPos;
    static float savedMove, savedSprint, savedJump, savedSensitivity;

    internal static void Register()
    {
        // L'étape revient au saboteur (signal adressé à lui seul)
        NetSignal.OnLocalPlayer(SabotagedSignal, () =>
        {
            if (!TaskActive()) return;
            TaskHook.TriggerTaskHookCommandStatic(Hook(SabotagedAction, ShredderObj));
            Plugin.L.LogInfo("[ZOOMER] broyeur saboté");
        });
        NetSignal.On(TrapOn, it =>
        {
            trapped.Add(it.Pointer);
            lastTrap = it.transform;   // VictimSignal suit : la victime sera placée devant ce broyeur
            scream ??= ModAssets.LoadWav("scream.wav");
            ModAudio.Play(scream, it.transform.position);
            var jam = Printer.instance?.jamAudioSource;
            if (jam?.clip != null && !alarms.ContainsKey(it.Pointer))
                alarms[it.Pointer] = ModAudio.PlayLoop(jam.clip, it.transform.position, jam);
            try { Blood.Splash(it); Blood.StartDripping(it); }
            catch (Exception e) { Plugin.L.LogError("[ZOOMER] sang : " + e); }
            Plugin.L.LogInfo("[ZOOMER] main coincée dans le broyeur !");
        });
        NetSignal.On(VictimSignal, it =>
        {
            victimNetId = it.netId;
            if (it.netIdentity == NetworkClient.localPlayer) Lock(lastTrap);
        });
        NetSignal.On(TrapOff, it =>
        {
            trapped.Remove(it.Pointer);
            if (alarms.Remove(it.Pointer, out var src)) ModAudio.Stop(src);
            Blood.Release(it);
            if (trapped.Count == 0) { victimNetId = 0; Unlock(); }
            Plugin.L.LogInfo("[ZOOMER] main dégagée");
        });
    }

    // --- Actions sur le broyeur ---
    [HarmonyPatch(typeof(PaperShredder), nameof(PaperShredder.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddAlternatives(PaperShredder __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var shredder = __instance;

        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Saboter le broyeur", "Sabotage the shredder"), InteractionPermission.All,
            NetSignal.ServerAction(shredder, ArmAction, player => Arm(shredder, player)), false,
            Show(() => TaskActive()), SabotageSeconds, NoTimer(), NoStart(), false, false));

        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Dégager la main", "Free the hand"), InteractionPermission.All,
            NetSignal.ServerAction(shredder, FreeAction, _ => Release(shredder)), false,
            Show(() => trapped.Contains(shredder.Pointer) && NetworkClient.localPlayer?.netId != victimNetId),
            FreeSeconds, NoTimer(), NoStart(), false, false));
    }

    // Serveur
    static void Arm(PaperShredder shredder, NetworkIdentity player)
    {
        if (!NetworkServer.active) return;
        armed.Add(shredder.Pointer);
        NetSignal.SendToPlayer(player, SabotagedSignal);
    }

    static void Release(PaperShredder shredder)
    {
        if (!NetworkServer.active || !releaseAt.Remove(shredder.Pointer)) return;
        NetSignal.Send(shredder, TrapOff);
    }

    // Le broyage arrive par cette commande (UserCode_ est inliné) : le prochain qui broie après sabotage se fait coincer
    [HarmonyPatch(typeof(PaperShredder), nameof(PaperShredder.InvokeUserCode_ShredDocument__NetworkIdentity__NetworkConnectionToClient))]
    [HarmonyPrefix]
    static void BeforeShred(NetworkBehaviour obj, NetworkConnectionToClient senderConnection)
    {
        var shredder = obj?.TryCast<PaperShredder>();
        Plugin.L.LogInfo($"[ZOOMER] broyage par {senderConnection?.identity?.name} (piégé : {shredder != null && armed.Contains(shredder.Pointer)})");
        if (shredder == null || !armed.Remove(shredder.Pointer)) return;
        Trap(shredder, senderConnection?.identity);
    }

    static void Trap(PaperShredder shredder, NetworkIdentity victim)
    {
        PlayerInteractable target = null;
        foreach (var p in Object.FindObjectsOfType<PlayerInteractable>())
            if (p.netIdentity == victim) { target = p; break; }
        if (target == null) { Plugin.L.LogWarning("[ZOOMER] victime introuvable"); return; }
        releaseAt[shredder.Pointer] = Time.time + TrapSeconds;
        NetSignal.Send(shredder, TrapOn);
        NetSignal.Send(target, VictimSignal);
    }

    // Hôte : libération automatique au bout de 60 s
    internal static void Tick()
    {
        if (!NetworkServer.active || releaseAt.Count == 0) return;
        foreach (var shredder in Object.FindObjectsOfType<PaperShredder>())
            if (releaseAt.TryGetValue(shredder.Pointer, out var t) && Time.time >= t) Release(shredder);
    }

    // --- Immobilisation de la victime (chez elle) ---
    static PlayerController Me() => NetworkClient.localPlayer?.GetComponent<PlayerController>();

    static Transform lastTrap;
    const float InFront = 0.65f;   // distance broyeur -> joueur une fois coincé

    // On peut broyer de loin : la victime est d'abord ramenée juste devant le broyeur (du côté où elle se trouvait),
    // tournée vers lui, puis bloquée là
    static void Lock(Transform shredder)
    {
        var pc = Me();
        if (pc == null || locked) return;
        locked = true;
        lockPos = pc.transform.position;
        if (shredder != null)
        {
            var dir = pc.transform.position - shredder.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = shredder.forward;
            dir.Normalize();
            lockPos = new Vector3(shredder.position.x, pc.transform.position.y, shredder.position.z) + dir * InFront;
            Teleport(pc, lockPos);
            pc.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
        }
        savedMove = pc.moveSpeed; savedSprint = pc.sprintSpeed; savedJump = pc.jumpHeight;
        pc.moveSpeed = 0f; pc.sprintSpeed = 0f; pc.jumpHeight = 0f;
        pc.inputPointing = true;   // bras tendu vers le broyeur : la main est coincée dedans
        var cam = pc.cameraController;
        if (cam != null) { savedSensitivity = cam.cameraSensitivity; cam.cameraSensitivity = 0f; }   // regard figé aussi
        Plugin.L.LogInfo("[ZOOMER] je suis coincé");
    }

    static void Unlock()
    {
        if (!locked) return;
        locked = false;
        var pc = Me();
        if (pc == null) return;
        pc.moveSpeed = savedMove; pc.sprintSpeed = savedSprint; pc.jumpHeight = savedJump;
        pc.inputPointing = false;
        if (pc.cameraController != null) pc.cameraController.cameraSensitivity = savedSensitivity;
    }

    // Pointage forcé (le clic droit ne doit pas le relâcher) et filet de sécurité si quelque chose pousse le joueur
    internal static void LateTick()
    {
        if (!locked) return;
        var pc = Me();
        if (pc == null) return;
        pc.inputPointing = true;
        var cam = pc.cameraController;
        if (cam != null) { cam.cameraSensitivity = 0f; cam.lookInput = Vector2.zero; }   // les réglages du jeu peuvent la remettre
        if ((pc.transform.position - lockPos).sqrMagnitude < 0.0025f) return;
        Teleport(pc, lockPos);
    }

    // Le CharacterController écrase une position modifiée directement : on le coupe le temps du déplacement
    static void Teleport(PlayerController pc, Vector3 pos)
    {
        var cc = pc.controller;
        if (cc != null) cc.enabled = false;
        pc.transform.position = pos;
        if (cc != null) cc.enabled = true;
    }

    // --- Debug (hôte) ---
    static PaperShredder Nearest()
    {
        var me = NetworkClient.localPlayer;
        PaperShredder best = null;
        float bestD = float.MaxValue;
        if (me == null) return null;
        foreach (var s in Object.FindObjectsOfType<PaperShredder>())
        {
            float d = Vector3.Distance(s.transform.position, me.transform.position);
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }

    internal static string TestArm()
    {
        if (!NetworkServer.active) return "Hôte uniquement";
        var s = Nearest();
        if (s == null) return "Aucun broyeur";
        armed.Add(s.Pointer);
        return "Broyeur le plus proche saboté";
    }

    internal static string TestTrapSelf()
    {
        if (!NetworkServer.active) return "Hôte uniquement";
        var s = Nearest();
        if (s == null) return "Aucun broyeur";
        Trap(s, NetworkClient.localPlayer);
        return "Main coincée (test solo, 60 s)";
    }

    static Il2CppSystem.Func<bool> Show(Func<bool> f) => DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(f);
    static UnityAction<float> NoTimer() => DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { }));
    static UnityAction<NetworkIdentity> NoStart() => DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { }));
}
