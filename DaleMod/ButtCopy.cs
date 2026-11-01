using System;
using System.IO;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Mirror;
using Player;
using Player.Tasks;
using Props.Printer;
using Props.Scripts;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Photocopie de fesses" (rouge, Slacker ; voir TaskPools) :
// 1) "S'asseoir sur la photocopieuse" (5 s) : le joueur est posé ASSIS sur la machine (animation "assis sur une chaise"
//    du jeu, jouée chez tous), tourné vers l'avant de la photocopieuse, immobilisé (caméra comprise) ; son de copie ;
// 2) la photocopie (Assets\buttcopy.png, tools/make_buttcopy.py) sort de l'imprimante du jeu : la récupérer ;
// 3) la punaiser au panneau d'affichage (hook du jeu, comme le Moulin à vent).
// Les étapes 2 et 3 (hooks génériques "image imprimée") ne comptent qu'après s'être assis (AcceptHook).
static class ButtCopy
{
    internal const string Key = "dalemod.photocopieFesses";
    internal const string SatAction = "Sat", CopierObj = "Photocopier";
    const float SitSeconds = 5f, SatSignal = -87.1f, SitAction = -87.5f, SitUp = -87.2f, SeatOn = -87.3f, CopySound = -87.4f;
    const float SitDrop = 0.45f, FrontShift = 0.12f;   // assis : hanches au niveau du dessus de la machine

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    static IntPtr taskPtr;
    static bool sat;
    internal static bool Sat
    {
        get
        {
            var p = CustomTaskPatches.ActiveTask()?.Pointer ?? IntPtr.Zero;
            if (p != taskPtr) { taskPtr = p; sat = false; }
            return sat;
        }
    }

    internal static void Register()
    {
        // Début de la pose : le serveur prévient tout le monde (animation assise du joueur, son de la photocopieuse)
        NetSignal.OnServer(SitUp, (it, player) =>
        {
            var pi = NetSignal.PlayerInteractableOf(player);
            if (pi != null) NetSignal.Send(pi, SeatOn);
            NetSignal.Send(it, CopySound);
        });
        NetSignal.On(SeatOn, it => StartSeatAnim(it.netIdentity?.GetComponent<PlayerController>()));
        NetSignal.On(CopySound, it =>
        {
            copierClip ??= ModAssets.LoadWav("copier.wav");
            ModAudio.Play(copierClip, it.transform.position);
        });
        NetSignal.OnLocalPlayer(SatSignal, () =>
        {
            if (!TaskActive()) return;
            _ = Sat;
            sat = true;
            TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, CopierObj, null, SatAction, null, null));
            Hud.Show(CustomTaskPatches.Text("Photocopie lancée ! Récupère-la à l'imprimante.", "Copy started! Grab it at the printer."), 3f);
            Plugin.L.LogInfo("[BUTT] assis sur la photocopieuse");
        });
    }

    [HarmonyPatch(typeof(Printer), nameof(Printer.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddSit(Printer __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var printer = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("S'asseoir sur la photocopieuse", "Sit on the photocopier"), InteractionPermission.All,
            NetSignal.ServerAction(printer, SitAction, player => ServerCopy(printer, player)), false,
            DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(new Func<bool>(() => TaskActive() && !Sat)),
            SitSeconds,
            DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { })),
            // Début (chez le joueur) : perché sur la machine, immobile pendant la copie
            DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => SitOn(printer))),
            false, false));
    }

    // Serveur : la photocopie sort de l'imprimante (image imprimée du jeu). L'imprimante n'accepte que les couleurs de
    // la palette de Paint : l'image est en noir et blanc tramé ; on note la palette dans le log pour vérifier.
    static Il2CppStructArray<byte> image;
    static bool paletteLogged;
    static AudioClip copierClip;
    static void ServerCopy(Printer printer, NetworkIdentity player)
    {
        try
        {
            if (!paletteLogged)
            {
                paletteLogged = true;
                var cols = new System.Collections.Generic.List<string>();
                if (printer.allowedColors != null) foreach (var c in printer.allowedColors) cols.Add(ColorUtility.ToHtmlStringRGB(c));
                Plugin.L.LogInfo($"[BUTT] palette de l'imprimante ({printer.tolerance}) : {string.Join(" ", cols)}");
            }
            image ??= new Il2CppStructArray<byte>(File.ReadAllBytes(Path.Combine(ModAssets.Dir, "buttcopy.png")));
            printer.ServerPrintImage(image);
            Plugin.L.LogInfo("[BUTT] photocopie imprimée");
        }
        catch (Exception e) { Plugin.L.LogError("[BUTT] impression : " + e.Message); }
        NetSignal.SendToPlayer(player, SatSignal);
    }

    // --- Pose sur la machine (chez le joueur) ---
    static float releaseAt = -1f, savedMove, savedSprint, savedSens;
    static Vector3 seat;

    static Quaternion facing, savedViewLocal;
    static Transform viewCam;
    static Vector3 front;

    static void SitOn(Printer printer)
    {
        var pc = NetworkClient.localPlayer?.GetComponent<PlayerController>();
        if (pc == null || releaseAt > 0f) return;
        var b = Blood.Bounds(printer);   // haut et sol de la machine
        front = FrontOf(printer, pc);
        seat = new Vector3(b.top.x, b.top.y - SitDrop, b.top.z) + front * FrontShift;
        facing = Quaternion.LookRotation(front, Vector3.up);
        savedMove = pc.moveSpeed; savedSprint = pc.sprintSpeed;
        pc.moveSpeed = 0f; pc.sprintSpeed = 0f;
        var cam = pc.cameraController;
        if (cam != null) { savedSens = cam.cameraSensitivity; cam.cameraSensitivity = 0f; }
        viewCam = Camera.main != null ? Camera.main.transform : null;
        if (viewCam != null) savedViewLocal = viewCam.localRotation;   // remise en place à la fin (sinon vue décalée du corps)
        Teleport(pc, seat);
        pc.transform.rotation = facing;
        releaseAt = Time.time + SitSeconds + 0.3f;
        if (NetworkServer.active) { NetSignal.Send(NetSignal.PlayerInteractableOf(NetworkClient.localPlayer), SeatOn); NetSignal.Send(printer, CopySound); }
        else NetSignal.SendToServer(printer, SitUp);
        Plugin.L.LogInfo("[BUTT] assis sur la photocopieuse");
    }

    // --- Animation assise (chez chaque client, pour le joueur concerné) : celle de l'état "dans une chaise" du jeu ---
    static readonly System.Collections.Generic.Dictionary<IntPtr, (Animator anim, string sit, string idle, float until)> seated = new();

    static void StartSeatAnim(PlayerController pc)
    {
        if (pc == null) return;
        var anim = pc.GetComponentInChildren<Animator>(true);
        string sit = AnimName(pc, Player.StateMachineLogic.States.InChair), idle = AnimName(pc, Player.StateMachineLogic.States.Idle);
        Plugin.L.LogInfo($"[BUTT] animation assise '{sit}' (repos '{idle}') animator={(anim == null ? "-" : "ok")}");
        if (anim == null || string.IsNullOrEmpty(sit)) return;
        anim.CrossFade(sit, 0.2f);
        seated[pc.Pointer] = (anim, sit, idle, Time.time + SitSeconds + 0.3f);
    }

    static string AnimName(PlayerController pc, Player.StateMachineLogic.States s)
    {
        try
        {
            if (pc.states != null && pc.states.TryGetValue(s, out var st) && st != null)
                return st.TryCast<Player.StateMachineLogic.InChairState>()?.animName ?? AnimOf(st);
        }
        catch { }
        return null;
    }

    // animName est déclaré par chaque état : on le lit par réflexion de l'interop si le type exact n'est pas connu
    static string AnimOf(Il2CppSystem.Object st)
    {
        var prop = st.GetType().GetProperty("animName");
        return prop?.GetValue(st) as string;
    }

    static void UpdateSeatAnims()
    {
        foreach (var key in new System.Collections.Generic.List<IntPtr>(seated.Keys))
        {
            var (anim, sit, idle, until) = seated[key];
            if (anim == null) { seated.Remove(key); continue; }
            if (Time.time >= until)
            {
                if (!string.IsNullOrEmpty(idle)) anim.CrossFade(idle, 0.2f);
                seated.Remove(key);
                continue;
            }
            // Le jeu peut relancer son animation : on maintient la pose assise
            if (!anim.IsInTransition(0) && !anim.GetCurrentAnimatorStateInfo(0).IsName(sit)) anim.Play(sit);
        }
    }

    internal static void LateTick()
    {
        UpdateSeatAnims();
        if (releaseAt < 0f) return;
        var pc = NetworkClient.localPlayer?.GetComponent<PlayerController>();
        if (pc == null) { releaseAt = -1f; return; }
        if (Time.time >= releaseAt)
        {
            releaseAt = -1f;
            pc.moveSpeed = savedMove; pc.sprintSpeed = savedSprint;
            if (pc.cameraController != null) pc.cameraController.cameraSensitivity = savedSens;
            if (viewCam != null) viewCam.localRotation = savedViewLocal;   // la vue suit de nouveau le corps (tourné vers l'avant)
            viewCam = null;
            Teleport(pc, new Vector3(seat.x, seat.y + SitDrop, seat.z) + front * 0.8f + Vector3.down * 0.9f);   // descend devant la machine
            return;
        }
        if (pc.cameraController != null) { pc.cameraController.cameraSensitivity = 0f; pc.cameraController.lookInput = Vector2.zero; }
        if ((pc.transform.position - seat).sqrMagnitude > 0.0025f) Teleport(pc, seat);
        pc.transform.rotation = facing;
    }

    // Avant de la machine : parmi ses 4 côtés (axes de l'objet), celui qui a le plus d'espace libre (le dos est contre le
    // mur). À égalité (machine au milieu de la pièce), le côté le plus proche du joueur.
    static Vector3 FrontOf(Printer printer, PlayerController pc)
    {
        var t = printer.transform;
        var b = Blood.Bounds(printer);
        var origin = new Vector3(t.position.x, Mathf.Lerp(b.floor, b.top.y, 0.5f), t.position.z);
        var toPlayer = pc.transform.position - t.position; toPlayer.y = 0f;
        Vector3 best = Vector3.zero; float bestScore = float.MinValue;
        foreach (var raw in new[] { t.forward, -t.forward, t.right, -t.right })
        {
            var dir = new Vector3(raw.x, 0f, raw.z);
            if (dir.sqrMagnitude < 0.01f) continue;
            dir.Normalize();
            float free = 3f;
            foreach (var hit in Physics.RaycastAll(origin, dir, 3f, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(t) && !hit.collider.transform.IsChildOf(pc.transform)) free = Mathf.Min(free, hit.distance);
            float score = free + 0.1f * Vector3.Dot(dir, toPlayer.normalized);   // départage léger vers le joueur
            if (score > bestScore) { bestScore = score; best = dir; }
        }
        if (best == Vector3.zero) best = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : Vector3.forward;
        Plugin.L.LogInfo($"[BUTT] avant de la machine : {best}");
        return best;
    }

    // Caméra bloquée face à l'avant pendant la pose (le jeu ne tourne plus la vue : on la fixe après lui)
    [HarmonyLib.HarmonyPatch(typeof(CameraController), nameof(CameraController.HandleCameraRotation))]
    [HarmonyLib.HarmonyPrefix]
    static bool FreezeView() => releaseAt < 0f;

    [HarmonyLib.HarmonyPatch(typeof(CameraController), nameof(CameraController.LateUpdate))]
    [HarmonyLib.HarmonyPostfix]
    static void FixView(CameraController __instance)
    {
        if (releaseAt < 0f) return;
        var pc = NetworkClient.localPlayer?.GetComponent<PlayerController>();
        if (pc == null || pc.cameraController == null || pc.cameraController.Pointer != __instance.Pointer) return;
        pc.transform.rotation = facing;
        if (viewCam != null) viewCam.rotation = facing * Quaternion.Euler(12f, 0f, 0f);   // légèrement vers le bas
    }

    static void Teleport(PlayerController pc, Vector3 pos)
    {
        var cc = pc.controller;
        if (cc != null) cc.enabled = false;
        pc.transform.position = pos;
        if (cc != null) cc.enabled = true;
    }
}
