using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Player.Scripts;
using Player.Tasks;
using Props.Scripts;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Douche froide" (rouge, Slacker ; voir TaskPools) : remplir un gobelet à la fontaine, le verser sur la tête d'un collègue.
// Tout le monde entend l'éclaboussure, puis les pas mouillés ("splotch") du collègue pendant 15 s ;
// lui voit un voile d'eau puis des gouttes sur son écran (Assets/water_drops.png). Signaux serveur -> clients par NetSignal.
static class ColdShower
{
    internal const string Key = "dalemod.doucheFroide";
    internal const string SoakedAction = "Soaked", CoworkerObj = "Coworker";
    const float PouredSignal = -71.1f, WetSignal = -71.2f, PourAction = -71.5f;
    const float PourSeconds = 1.5f, WetSeconds = 15f, StepDistance = 0.9f;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    static AudioClip splash, splotch;
    // Joueurs mouillés (chez tous les clients) : fin de l'effet et position du dernier "splotch"
    static readonly Dictionary<IntPtr, (Transform t, float until, Vector3 last)> wet = new();

    internal static void Register()
    {
        // L'étape revient au joueur qui a versé (signal adressé à lui seul)
        NetSignal.OnLocalPlayer(PouredSignal, () =>
        {
            if (!TaskActive()) return;
            TaskHook.TriggerTaskHookCommandStatic(WaterCup.Hook(SoakedAction, CoworkerObj));
            Plugin.L.LogInfo("[SHOWER] collègue douché");
        });
        NetSignal.On(WetSignal, it =>
        {
            splash ??= ModAssets.LoadWav("splash.wav");
            ModAudio.Play(splash, it.transform.position + Vector3.up * 1.6f);
            wet[it.Pointer] = (it.transform, Time.time + WetSeconds, it.transform.position);
            if (it.netIdentity == NetworkClient.localPlayer) dropsUntil = Time.time + WetSeconds;
            Plugin.L.LogInfo($"[SHOWER] {it.name} est trempé");
        });
    }

    // --- Action "Verser l'eau" sur chaque joueur (l'instance = la cible) ---
    [HarmonyPatch(typeof(PlayerInteractable), nameof(PlayerInteractable.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddPour(PlayerInteractable __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var target = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Verser l'eau sur sa tête", "Pour water on their head"), InteractionPermission.All,
            NetSignal.ServerAction(target, PourAction, player => Pour(player, target)),
            Il2CppType.Of<WaterCupController>(), false,
            DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(new Func<bool>(() =>
                TaskActive() && target.netIdentity != NetworkClient.localPlayer && WaterCup.HasWater(NetworkClient.localPlayer))),
            PourSeconds, false,
            DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { })), false));
    }

    // Serveur
    static void Pour(NetworkIdentity player, PlayerInteractable target)
    {
        if (!NetworkServer.active) return;
        var cup = WaterCup.Held(player);
        Plugin.L.LogInfo($"[SHOWER] {player?.name} verse sur {target?.name} (gobelet {(cup == null ? "introuvable" : cup.waterAmount.ToString("0.00"))})");
        if (cup == null || cup.waterAmount <= 0.01f) return;
        WaterCup.Empty(cup);
        NetSignal.SendToPlayer(player, PouredSignal);
        NetSignal.Send(target, WetSignal);
    }

    // Pas mouillés : un "splotch" tous les StepDistance mètres parcourus, chez tous les clients
    internal static void Tick()
    {
        if (wet.Count == 0) return;
        List<IntPtr> done = null;
        foreach (var (key, w) in wet)
        {
            if (w.t == null || Time.time >= w.until) { (done ??= new()).Add(key); continue; }
            var pos = w.t.position;
            if ((pos - w.last).sqrMagnitude < StepDistance * StepDistance) continue;
            splotch ??= ModAssets.LoadWav("plotch.wav");
            ModAudio.Play(splotch, pos, 0.8f);
            wet[key] = (w.t, w.until, pos);
            break;   // le dictionnaire vient d'être modifié : la suite au prochain tick
        }
        if (done != null) foreach (var k in done) wet.Remove(k);
    }

    // Debug (hôte) : se doucher soi-même
    internal static string TestSelf()
    {
        if (!NetworkServer.active) return "Hôte uniquement";
        var me = NetworkClient.localPlayer;
        foreach (var p in Object.FindObjectsOfType<PlayerInteractable>())
            if (p.netIdentity == me) { NetSignal.Send(p, WetSignal); return "Douché (test solo)"; }
        return "PlayerInteractable local introuvable";
    }

    // --- Écran mouillé du joueur trempé ---
    // Voile bleuté à l'impact, puis la texture de gouttes (tools/make_water_overlay.py) qui glisse lentement vers le bas
    // et s'estompe sur la fin de l'effet.
    static float dropsUntil = -1f;
    static Texture2D drops, wash;
    const float WashSeconds = 1.2f, FadeSeconds = 5f, SlidePixels = 40f;

    internal static void DrawOverlay()
    {
        float left = dropsUntil - Time.time;
        if (left <= 0f) return;
        float elapsed = WetSeconds - left;
        var prev = GUI.color;

        if (elapsed < WashSeconds)
        {
            wash ??= Solid(new Color(0.55f, 0.72f, 0.9f));
            GUI.color = new Color(1f, 1f, 1f, 0.55f * (1f - elapsed / WashSeconds));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), wash);
        }

        drops ??= LoadDrops();
        if (drops != null)
        {
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(left / FadeSeconds));
            float slide = SlidePixels * elapsed / WetSeconds * Screen.height / 900f;
            GUI.DrawTexture(new Rect(0, slide, Screen.width, Screen.height), drops, ScaleMode.ScaleAndCrop);
        }
        GUI.color = prev;
    }

    static Texture2D LoadDrops()
    {
        try
        {
            var t = ModAssets.LoadTexture("water_drops.png");
            if (t != null) t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return t;
        }
        catch (Exception e) { Plugin.L.LogWarning("[SHOWER] water_drops.png : " + e.Message); return null; }
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontUnloadUnusedAsset };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
