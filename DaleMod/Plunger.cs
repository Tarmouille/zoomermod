using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Player.Tasks;
using Props.Scripts;
using Props.WC.Toilet;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Toilettes bouchées" (bleue, tirée seulement par le Janitor, avec le tirage du jeu : CustomTask.JanitorOnly) :
//  1) prendre la ventouse dans le placard de la cuisine (celui de la gelée, comme le coussin de "Pascal Prout"),
//     seulement avec la tâche, et autant de fois qu'il faut (ventouse posée ou perdue) ;
//  2) déboucher LES toilettes tirées au sort à la mise en place de la tâche (chez le janitor seul : rien ne montre
//     aux autres qu'elles sont bouchées) : action de 5 s, ventouse en main. Un des 3 sons d'Assets/plunger, le même
//     chez tous, joué sur les toilettes pendant l'action (coupé si on s'écarte avant la fin). La ventouse disparaît
//     une fois les toilettes débouchées.
static class Plunger
{
    internal const string Key = "dalemod.toilettesBouchees";
    internal const string Obj = "Plunger", TookAction = "Took", UnclogAction = "Unclogged";
    const float TakeSeconds = 1.5f, PlungeSeconds = 5f;
    const int SoundCount = 3;
    // Actions serveur : prendre au placard / déboucher ; début de l'action (client -> serveur) ; abandon ;
    // à tous : son n° k, son coupé ; au janitor : étape validée
    const float TakeAction = -98.1f, PlungeAction = -98.2f, StartUp = -98.3f, CancelUp = -98.4f, CancelSignal = -98.5f,
                DoneSignal = -98.6f;
    static float SoundSignal(int k) => -98.71f - k * 0.01f;

    internal static readonly Dictionary<string, Color> Colors = new()
    {
        ["rubber"] = new Color(0.75f, 0.10f, 0.13f),
        ["rubber_dark"] = new Color(0.58f, 0.07f, 0.09f),
        ["rubber_inner"] = new Color(0.27f, 0.04f, 0.05f),
        ["wood"] = new Color(0.77f, 0.55f, 0.32f),
        ["wood_end"] = new Color(0.67f, 0.45f, 0.24f),
    };
    // Modèle : tools/make_plunger.py (en mètres, origine à la prise en main)
    internal static readonly ModItem Item = new("Plunger", 0xC0CA100A, "plunger.obj", Vector3.one, Colors)
        { ResetChildRotation = true, GripOnPivot = true, NeverDespawn = true };

    static TaskHook Hook(string action) => new(null, Obj, null, action, null, null);
    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    internal static void Register()
    {
        NetSignal.OnLocalPlayer(DoneSignal, () =>
        {
            plunging = null;
            if (!TaskActive()) return;
            TaskHook.TriggerTaskHookCommandStatic(Hook(UnclogAction));
            Plugin.L.LogInfo("[VENTOUSE] toilettes débouchées : tâche validée");
        });
        NetSignal.OnServer(StartUp, (it, _) => ServerSound(it));
        NetSignal.OnServer(CancelUp, (it, _) => NetSignal.Send(it, CancelSignal));
        for (int k = 0; k < SoundCount; k++)
        {
            int n = k;
            NetSignal.On(SoundSignal(n), it => PlaySound(it, n));
        }
        NetSignal.On(CancelSignal, it =>
        {
            if (sounds.TryGetValue(it.Pointer, out var src)) { ModAudio.Stop(src); sounds.Remove(it.Pointer); }
            Plugin.L.LogInfo("[VENTOUSE] débouchage interrompu");
        });
    }

    // ===================== Toilettes bouchées (chez le janitor, à la mise en place) =====================
    static readonly Dictionary<IntPtr, IntPtr> clogged = new();   // tâche -> toilettes

    internal static void PickToilet(Task task)
    {
        var all = Object.FindObjectsOfType<Toilet>();
        if (all.Length == 0) { Plugin.L.LogWarning("[VENTOUSE] aucune toilette sur la carte"); return; }
        var t = all[new System.Random().Next(all.Length)];
        clogged[task.Pointer] = t.Pointer;
        TaskHookObjectsPatches.Retarget(UnclogAction, Obj);   // l'indicateur suit les toilettes de CETTE tâche
        Plugin.L.LogInfo($"[VENTOUSE] toilettes bouchées : '{t.name}' (parmi {all.Length})");
    }

    static bool IsClogged(Toilet t)
    {
        var task = CustomTaskPatches.ActiveTask();
        return t != null && task != null && clogged.TryGetValue(task.Pointer, out var p) && p == t.Pointer;
    }

    // Pour l'indicateur : les toilettes à déboucher (aucune si la tâche n'est pas active chez moi)
    internal static List<Toilet> CloggedToilets()
    {
        var l = new List<Toilet>();
        if (!TaskActive()) return l;
        foreach (var t in Object.FindObjectsOfType<Toilet>()) if (IsClogged(t)) l.Add(t);
        return l;
    }

    // ===================== Actions =====================
    static bool tookSent;
    static float nextHoldCheck;   // recherche de l'objet en main : 4 fois par seconde (coûteuse)

    // Placard de la cuisine : celui de la gelée
    [HarmonyPatch(typeof(KitchenCabinetController), nameof(KitchenCabinetController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void CupboardAction(KitchenCabinetController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var cab = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Prendre la ventouse", "Take the plunger"), InteractionPermission.All,
            NetSignal.ServerAction(cab, TakeAction, player => { if (!Holds(player)) Item.GiveTo(player); }), false,
            Show(() => TaskActive() && !Item.LocalHolds() && IsJellyCabinet(cab)), TakeSeconds, NoTimer(), NoStart(), false, false));
    }

    static bool IsJellyCabinet(KitchenCabinetController cab)
    {
        var k = ModItem.JellyCabinet();
        return k != null && k.Pointer == cab.Pointer;
    }

    // Toilettes bouchées : déboucher (ventouse en main)
    [HarmonyPatch(typeof(Toilet), nameof(Toilet.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void ToiletAction(Toilet __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var toilet = __instance;
        var alt = new InteractionAlternative(CustomTaskPatches.Text("Déboucher les toilettes", "Unclog the toilet"), InteractionPermission.All,
            NetSignal.ServerAction(toilet, PlungeAction, player => ServerUnclog(toilet, player)), false,
            Show(() => TaskActive() && Item.LocalHolds() && IsClogged(toilet)), PlungeSeconds,
            DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { lastBeat = Time.time; beats++; })),   // chaque image pendant l'action
            DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => StartPlunge(toilet))),
            false, false);
        alt.requireUsable = HeldItem;   // le constructeur avec suivi de progression ne prend pas l'objet tenu
        alts.Add(alt);
    }

    // Objet tenu requis par l'action : sans ça, le jeu masque l'action quand on a quelque chose en main
    static Il2CppSystem.Type HeldItem => Il2CppType.Of<Consumable>();

    static bool Holds(NetworkIdentity player)
    {
        foreach (var c in Object.FindObjectsOfType<Consumable>())
            if (Item.Is(c) && ModItem.HeldBy(c, player)) return true;
        return false;
    }

    // Pas d'action "Consommer" sur la ventouse (copie du soda)
    [HarmonyPatch(typeof(Consumable), nameof(Consumable.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void NoConsume(Consumable __instance)
    {
        var alts = __instance.alternatives;
        if (alts != null && alts.Count > 0 && Item.Is(__instance)) alts.RemoveAt(alts.Count - 1);
    }

    // ===================== Débouchage : son pendant l'action, abandon =====================
    static Toilet plunging;          // chez le janitor : toilettes en cours de débouchage
    static float plungeStart, lastBeat;
    static int beats;
    static readonly Dictionary<IntPtr, AudioSource> sounds = new();
    static readonly AudioClip[] clips = new AudioClip[SoundCount];

    static void StartPlunge(Toilet toilet)
    {
        plunging = toilet; plungeStart = lastBeat = Time.time; beats = 0;
        if (NetworkServer.active) ServerSound(toilet); else NetSignal.SendToServer(toilet, StartUp);
    }

    static void ServerSound(Interactable toilet)
    {
        int k = new System.Random().Next(SoundCount);
        NetSignal.Send(toilet, SoundSignal(k));
    }

    static void PlaySound(Interactable toilet, int k)
    {
        clips[k] ??= ModAssets.LoadWav($"plunger/{k + 1}.wav");
        if (clips[k] == null) return;
        if (sounds.TryGetValue(toilet.Pointer, out var old)) ModAudio.Stop(old);
        var src = ModAudio.PlayLoop(clips[k], toilet.transform.position);
        if (src == null) return;
        src.loop = false;
        Object.Destroy(src.gameObject, clips[k].length + 0.2f);
        sounds[toilet.Pointer] = src;
        Plugin.L.LogInfo($"[VENTOUSE] son n°{k + 1}");
    }

    static void ServerUnclog(Toilet toilet, NetworkIdentity player)
    {
        if (!NetworkServer.active || !Item.ServerRemoveHeldBy(player)) return;
        NetSignal.SendToPlayer(player, DoneSignal);
        Plugin.L.LogInfo("[VENTOUSE] toilettes débouchées, ventouse rangée");
    }

    // Chez le janitor : étape 1 dès qu'il tient la ventouse ; abandon du débouchage s'il s'écarte avant la fin
    internal static void Tick()
    {
        if (!TaskActive()) tookSent = false;
        else if (!tookSent && Time.time >= nextHoldCheck)
        {
            nextHoldCheck = Time.time + 0.25f;
            if (Item.LocalHolds()) { tookSent = true; TaskHook.TriggerTaskHookCommandStatic(Hook(TookAction)); Plugin.L.LogInfo("[VENTOUSE] ventouse prise"); }
        }

        if (plunging != null && beats > 0 && Time.time - lastBeat > 0.35f && Time.time - plungeStart < PlungeSeconds - 0.2f)
        {
            var t = plunging;
            plunging = null;
            if (NetworkServer.active) NetSignal.Send(t, CancelSignal); else NetSignal.SendToServer(t, CancelUp);
        }
        else if (plunging != null && Time.time - plungeStart > PlungeSeconds + 2f) plunging = null;   // filet de sécurité
    }

    static Il2CppSystem.Func<bool> Show(Func<bool> f) => DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(f);
    static UnityAction<float> NoTimer() => DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { }));
    static UnityAction<NetworkIdentity> NoStart() => DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { }));
}
