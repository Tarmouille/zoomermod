using System;
using System.Collections.Generic;
using HarmonyLib;
using Player.PlayerEffects;
using Player.PlayerEffects.Effects;
using Player.Scripts.Inputs;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Effet "Nauséeux" : l'effet Drunk du jeu, reconnaissable à sa durée "marquée" (partie décimale Marker),
// qui est déjà synchronisée vers tous les clients : pas de message réseau à ajouter. La bière garde l'effet normal.
// Pendant l'effet : nom et couleur changés, tangage plus lent, filtre vert sur l'écran du joueur concerné.
static class Nausea
{
    const float Marker = 0.0137f;
    internal const float Duration = 30f;
    const float SwaySpeedFactor = 0.5f;                       // tangue 2x moins vite que l'effet bourré
    internal static readonly Color Green = new(0.45f, 0.75f, 0.15f);

    static readonly HashSet<IntPtr> nauseous = new();         // PlayerEffectController concernés
    internal static bool LocalActive;
    static InputController input;
    static float savedSwaySpeed;

    internal static float MarkedDuration(float seconds) => Mathf.Floor(seconds) + Marker;
    internal static bool IsMarked(float d) => Mathf.Abs(d - Mathf.Floor(d) - Marker) < 0.0005f;
    internal static bool Is(PlayerEffectController c) => c != null && nauseous.Contains(c.Pointer);

    // Côté serveur : rend le joueur nauséeux
    internal static void Apply(PlayerEffectController fx, float seconds = Duration)
    {
        fx.ServerSetEffect((int)PlayerEffects.Drunk, MarkedDuration(seconds));
        Plugin.L.LogInfo($"[NAUSEE] appliqué par le serveur ({MarkedDuration(seconds)}s) ; useDrunkEffect={fx.useDrunkEffect}");
    }

    internal static PlayerEffectController LocalController()
    {
        foreach (var c in Object.FindObjectsOfType<PlayerEffectController>())
            if (c.isOwned) return c;
        return null;
    }

    internal static string ApplyToSelf()
    {
        if (!Mirror.NetworkServer.active) return "Hôte uniquement";
        var fx = LocalController();
        if (fx == null) return "PlayerEffectController local introuvable";
        Apply(fx);
        return $"Nauséeux pour {Duration}s";
    }

    // Appelé sur chaque client quand l'effet démarre sur un joueur (local ou non)
    internal static void Begin(PlayerEffectController c)
    {
        nauseous.Add(c.Pointer);
        if (!c.isOwned || LocalActive) return;
        LocalActive = true;
        input = c.GetComponent<InputController>() ?? Object.FindObjectOfType<InputController>();
        if (input != null)
        {
            savedSwaySpeed = input.drunkEffectSpeed;
            input.drunkEffectSpeed = savedSwaySpeed * SwaySpeedFactor;
            Plugin.L.LogInfo($"[NAUSEE] début ; réglages drunk : look={input.drunkLookIntensity} move={input.drunkMovementIntensity} vitesse={savedSwaySpeed} -> {input.drunkEffectSpeed}");
        }
        else Plugin.L.LogWarning("[NAUSEE] InputController introuvable");
    }

    internal static void End(PlayerEffectController c)
    {
        if (c == null || !nauseous.Remove(c.Pointer) || !c.isOwned || !LocalActive) return;
        LocalActive = false;
        if (input != null) input.drunkEffectSpeed = savedSwaySpeed;
        Plugin.L.LogInfo("[NAUSEE] fin");
    }

    // --- Filtre vert : vignette (bords plus opaques) qui pulse lentement, dessinée en IMGUI ---
    static Texture2D vignette;

    internal static void DrawOverlay()
    {
        if (!LocalActive) return;
        if (vignette == null) vignette = BuildVignette();
        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.75f + 0.25f * Mathf.Sin(Time.time * 1.3f));
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette);
        GUI.color = prev;
    }

    static Texture2D BuildVignette()
    {
        const int S = 128;
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / 1.414f);
                float a = Mathf.Lerp(0.12f, 0.7f, r * r);
                t.SetPixel(x, y, new Color(0.35f, 0.55f, 0.08f, a));    // SetPixel unitaire : SetPixels passe par des Span absents de l'interop
            }
        t.Apply();
        return t;
    }
}

static class NauseaPatches
{
    // Sur chaque client : un effet Drunk dont la durée synchronisée (nextDuration) est marquée = nausée.
    // OnStartEffect est virtuelle, donc jamais inlinée (SetEffectLocal / StartEffect ne passaient pas par nos patchs).
    [HarmonyPatch(typeof(DrunkEffect), nameof(DrunkEffect.OnStartEffect))]
    [HarmonyPostfix]
    static void OnStart(DrunkEffect __instance)
    {
        var c = __instance.playerEffectController;
        if (c == null) return;
        Plugin.L.LogInfo($"[NAUSEE] DrunkEffect démarre, durée {c.nextDuration}s (local={c.isOwned})");
        if (Nausea.IsMarked(c.nextDuration)) Nausea.Begin(c);
        else Nausea.End(c);
    }

    [HarmonyPatch(typeof(DrunkEffect), nameof(DrunkEffect.OnStopEffect))]
    [HarmonyPostfix]
    static void OnStop(DrunkEffect __instance) => Nausea.End(__instance.playerEffectController);

    [HarmonyPatch(typeof(DrunkEffect), nameof(DrunkEffect.GetEffectName))]
    [HarmonyPostfix]
    static void Name(DrunkEffect __instance, ref string __result)
    {
        if (Nausea.Is(__instance.playerEffectController)) __result = CustomTaskPatches.Text("Nauséeux", "Nauseous");
    }

    [HarmonyPatch(typeof(DrunkEffect), nameof(DrunkEffect.GetEffectColor))]
    [HarmonyPostfix]
    static void Color(DrunkEffect __instance, ref UnityEngine.Color __result)
    {
        if (Nausea.Is(__instance.playerEffectController)) __result = Nausea.Green;
    }
}
