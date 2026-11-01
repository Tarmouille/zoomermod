using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Player.Scripts;
using Player.Tasks;
using Props.FireEx;
using Props.Scripts;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Rafraîchissement d'équipe" (Slacker) : prendre un extincteur et asperger un collègue.
// L'action "Asperger" n'existe que pour le joueur qui a la tâche (anti-abus). Le jet est visible et audible par tous,
// le collègue aspergé voit un nuage blanc pendant quelques secondes. Signaux serveur -> clients par NetSignal.
static class FireExPrank
{
    internal const string Key = "dalemod.rafraichissement";
    internal const string PickedAction = "Picked up", ExObj = "Fire Extinguisher",   // étape 1 = hook du jeu (sa flèche vise les extincteurs)
                           SprayedAction = "Sprayed", CoworkerObj = "Coworker";
    const float SpraySignal = -51.1f, BlindSignal = -51.2f, SprayedSignal = -51.3f, SprayAction = -51.5f;
    const float SpraySeconds = 1.5f, JetSeconds = 2f, BlindSeconds = 5f;

    internal static TaskHook Hook(string action, string obj) => new(null, obj, null, action, null, null);
    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    internal static void Register()
    {
        // Jet visible et audible par tous ; l'étape revient à celui qui tient l'extincteur
        NetSignal.On(SpraySignal, it =>
        {
            var ex = it.TryCast<FireExController>();
            if (ex == null) return;
            if (ex.fireExPs != null) { ex.fireExPs.Play(); jets.Add((ex.fireExPs, Time.time + JetSeconds)); }
            ModAudio.Play(ex.fireExSound, ex.transform.position, 1f, ex.GetComponentInChildren<AudioSource>());
        });
        // L'étape revient au joueur qui a aspergé (signal adressé à lui seul)
        NetSignal.OnLocalPlayer(SprayedSignal, () =>
        {
            if (!TaskActive()) return;
            TaskHook.TriggerTaskHookCommandStatic(Hook(SprayedAction, CoworkerObj));
            Plugin.L.LogInfo("[FIREX] collègue aspergé");
        });
        // Aveuglement, seulement chez le joueur visé (pour le test solo, le joueur local s'asperge lui-même)
        NetSignal.On(BlindSignal, it =>
        {
            if (it.netIdentity != NetworkClient.localPlayer) return;
            blindUntil = Time.time + BlindSeconds;
            Plugin.L.LogInfo("[FIREX] aspergé : aveuglé");
        });
    }

    // --- Arrêt des jets ---
    static readonly List<(ParticleSystem ps, float until)> jets = new();

    internal static void Tick()
    {
        for (int i = jets.Count - 1; i >= 0; i--)
            if (Time.time >= jets[i].until) { if (jets[i].ps != null) jets[i].ps.Stop(); jets.RemoveAt(i); }
    }

    // L'extincteur n'est pas "tenu" comme nos objets (HeldBy ne le voit pas) : tenu, sinon le plus proche du joueur
    const float HandReach = 2f;

    static FireExController HeldEx(NetworkIdentity player)
    {
        if (player == null) return null;
        FireExController best = null;
        float bestD = HandReach;
        foreach (var ex in Object.FindObjectsOfType<FireExController>())
        {
            if (ModItem.HeldBy(ex, player)) return ex;
            float d = Vector3.Distance(ex.transform.position, player.transform.position);
            if (d < bestD && !IsOnMount(ex)) { bestD = d; best = ex; }   // pas ceux restés sur leur support
        }
        return best;
    }

    static bool IsOnMount(FireExController ex) => ex.currentHolder != null && ex.currentHolder.TryCast<FireExHolder>() != null;
    internal static bool IsMine(FireExController ex, NetworkIdentity player) => player != null && HeldEx(player)?.Pointer == ex.Pointer;

    // --- Action "Asperger" sur chaque joueur (l'instance = la cible) ---
    [HarmonyPatch(typeof(PlayerInteractable), nameof(PlayerInteractable.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddSpray(PlayerInteractable __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var target = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Asperger", "Spray"), InteractionPermission.All,
            NetSignal.ServerAction(target, SprayAction, player => Spray(player, target)),
            Il2CppType.Of<FireExController>(), false,
            DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(new Func<bool>(() =>
                TaskActive() && target.netIdentity != NetworkClient.localPlayer)),
            SpraySeconds, false,
            DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { })), false));
    }

    // Debug (hôte) : s'asperger soi-même, faute de collègue à viser
    internal static string TestSelf()
    {
        if (!NetworkServer.active) return "Hôte uniquement";
        var me = NetworkClient.localPlayer;
        if (HeldEx(me) == null) return "Prends d'abord un extincteur";
        if (!TaskActive()) return "Assigne-toi d'abord la tâche";
        foreach (var p in Object.FindObjectsOfType<PlayerInteractable>())
            if (p.netIdentity == me) { Spray(me, p); return "Aspergé (test solo)"; }
        return "PlayerInteractable local introuvable";
    }

    // Serveur
    static void Spray(NetworkIdentity player, PlayerInteractable target)
    {
        if (!NetworkServer.active) return;
        var ex = HeldEx(player);
        Plugin.L.LogInfo($"[FIREX] aspersion par {player?.name} sur {target?.name} (extincteur {(ex == null ? "introuvable" : "ok")})");
        if (ex == null) return;
        NetSignal.Send(ex, SpraySignal);
        NetSignal.Send(target, BlindSignal);
        NetSignal.SendToPlayer(player, SprayedSignal);
    }

    // Serveur : l'extincteur part tout seul (Formation sécurité) : jet visible et audible par tous, et tous les joueurs
    // dans le rayon autour du porteur (lui compris) sont aspergés
    internal static void ServerMisfire(FireExController ex, NetworkIdentity holder, float radius)
    {
        if (!NetworkServer.active || ex == null || holder == null) return;
        NetSignal.Send(ex, SpraySignal);
        foreach (var p in Object.FindObjectsOfType<PlayerInteractable>())
            if (Vector3.Distance(p.transform.position, holder.transform.position) <= radius) NetSignal.Send(p, BlindSignal);
        Plugin.L.LogInfo($"[FIREX] l'extincteur de {holder.name} part tout seul");
    }

    // --- Nuage blanc sur l'écran du joueur aspergé (opacité décroissante) ---
    static float blindUntil = -1f;
    static Texture2D cloud;

    internal static void DrawOverlay()
    {
        float left = blindUntil - Time.time;
        if (left <= 0f) return;
        if (cloud == null) cloud = BuildCloud();
        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(left / BlindSeconds * 1.4f));
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), cloud);
        GUI.color = prev;
    }

    // Opaque au centre, un peu plus transparent sur les bords, avec du grain de poudre
    static Texture2D BuildCloud()
    {
        const int S = 128;
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
        var rnd = new System.Random(7);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / 1.414f);
                float a = Mathf.Lerp(0.97f, 0.75f, r * r) - (float)rnd.NextDouble() * 0.06f;
                float g = 0.93f + (float)rnd.NextDouble() * 0.07f;
                t.SetPixel(x, y, new Color(g, g, g, a));    // SetPixel unitaire : SetPixels passe par des Span absents de l'interop
            }
        t.Apply();
        return t;
    }
}
