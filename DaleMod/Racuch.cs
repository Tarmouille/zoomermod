using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Player.Tasks;
using Props.Scripts;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Le monde de Neko" (rouge, Slacker ; voir TaskPools) : seulement sur les cartes avec l'aquarium et un chat
// (Racuch ou Luna, n'importe lequel).
//  1) à l'aquarium, "Pêcher un poisson" : un poisson apparaît en main (un en plus : ceux de l'aquarium ne bougent pas) ;
//  2) au chat, "Donner le poisson" : le chat le mange (même bruit que la pâtée) et la tâche est validée.
// Le poisson est un vrai objet : lâché, il reste par terre ; n'importe qui peut le ramasser, le donner au chat ou le
// remettre dans l'aquarium. Modèle : celui des poissons de l'aquarium, appliqué chez chaque client.
static class Racuch
{
    internal const string Key = "dalemod.racuch";
    internal const string Obj = "RacuchFish", TookAction = "Took", GaveAction = "Gave";
    const float FishSeconds = 2.5f, GiveSeconds = 1.5f, ReturnSeconds = 1.5f;
    // Signaux : étape validée (au donneur) ; actions serveur : pêcher / donner / remettre
    const float GaveSignal = -97.1f, FishAction = -97.2f, GiveAction = -97.3f, ReturnAction = -97.4f;

    internal static readonly ModItem Fish = new("Fish", 0xC0CA1009, "racuch_fish_placeholder.obj", new Vector3(0.18f, 0.06f, 0.06f),
        new System.Collections.Generic.Dictionary<string, Color> { ["default"] = new Color(1f, 0.55f, 0.1f) }) { NeverDespawn = true };

    static TaskHook Hook(string action) => new(null, Obj, null, action, null, null);
    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    // Carte avec l'aquarium et le chat (sinon la tâche n'est pas tirée)
    internal static bool Available => Object.FindObjectOfType<FishTankController>() != null && Object.FindObjectOfType<CatController>() != null;

    internal static void Register()
    {
        NetSignal.OnLocalPlayer(GaveSignal, () =>
        {
            if (!TaskActive()) return;
            TaskHook.TriggerTaskHookCommandStatic(Hook(GaveAction));
            Plugin.L.LogInfo("[RACUCH] poisson donné : tâche validée");
        });
    }

    // ===================== Actions =====================
    [HarmonyPatch(typeof(FishTankController), nameof(FishTankController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void TankActions(FishTankController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var tank = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Pêcher un poisson", "Catch a fish"), InteractionPermission.All,
            NetSignal.ServerAction(tank, FishAction, player => { if (!Holds(player)) Fish.GiveTo(player); }), false,
            Show(() => TaskActive() && !tookSent && !Fish.LocalHolds()), FishSeconds, NoTimer(), NoStart(), false, false));
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Remettre le poisson dans l'aquarium", "Put the fish back in the tank"), InteractionPermission.All,
            NetSignal.ServerAction(tank, ReturnAction, player => { if (Fish.ServerRemoveHeldBy(player)) Plugin.L.LogInfo("[RACUCH] poisson remis dans l'aquarium"); }), HeldItem, false,
            Show(() => Fish.LocalHolds()), ReturnSeconds, false, NoStart(), false));
    }

    [HarmonyPatch(typeof(CatController), nameof(CatController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void CatActions(CatController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var cat = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Donner le poisson", "Give the fish"), InteractionPermission.All,
            NetSignal.ServerAction(cat, GiveAction, player => ServerGive(cat, player)), HeldItem, false,
            Show(() => Fish.LocalHolds()), GiveSeconds, false, NoStart(), false));
    }

    static void ServerGive(CatController cat, NetworkIdentity player)
    {
        if (!NetworkServer.active || !Fish.ServerRemoveHeldBy(player)) return;
        try { cat.RpcFeed(player); } catch (Exception e) { Plugin.L.LogWarning("[RACUCH] bruit du repas : " + e.Message); }   // même bruit que la pâtée
        NetSignal.SendToPlayer(player, GaveSignal);
        Plugin.L.LogInfo($"[RACUCH] {cat.interactableName} mange un poisson");
    }

    static bool Holds(NetworkIdentity player)
    {
        foreach (var c in Object.FindObjectsOfType<Consumable>())
            if (Fish.Is(c) && ModItem.HeldBy(c, player)) return true;
        return false;
    }

    static Il2CppSystem.Type HeldItem => Il2CppType.Of<Consumable>();

    // Pas d'action "Consommer" sur le poisson (copie du soda)
    [HarmonyPatch(typeof(Consumable), nameof(Consumable.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void NoConsume(Consumable __instance)
    {
        var alts = __instance.alternatives;
        if (alts != null && alts.Count > 0 && Fish.Is(__instance)) alts.RemoveAt(alts.Count - 1);
    }

    // ===================== Étape 1, et modèle du poisson =====================
    static bool tookSent;
    static float nextLook;

    internal static void Tick()
    {
        if (!TaskActive()) tookSent = false;
        else if (!tookSent && Fish.LocalHolds()) { tookSent = true; TaskHook.TriggerTaskHookCommandStatic(Hook(TookAction)); Plugin.L.LogInfo("[RACUCH] poisson pêché"); }
        if (Time.time >= nextLook) { nextLook = Time.time + 0.3f; ApplyFishLook(); }
    }

    // Chaque poisson du mod prend le maillage et la matière d'un poisson de l'aquarium, à sa taille réelle
    static Mesh fishMesh;
    static Material[] fishMats;
    static Vector3 fishSize;

    static void ApplyFishLook()
    {
        if (fishMesh == null)
        {
            var src = Object.FindObjectOfType<FishController>();
            if (src == null) return;
            var smr = src.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Renderer r = smr;
            Mesh m = smr != null ? smr.sharedMesh : null;
            if (m == null) { var mf = src.GetComponentInChildren<MeshFilter>(true); m = mf?.sharedMesh; r = mf?.GetComponent<MeshRenderer>(); }
            if (m == null || r == null) return;
            fishMesh = m;
            fishMats = r.sharedMaterials;
            fishSize = r.transform.lossyScale;
            Plugin.L.LogInfo($"[RACUCH] modèle du poisson : '{m.name}' ({m.vertexCount} sommets), échelle {fishSize}, taille {m.bounds.size}");
        }
        foreach (var c in Object.FindObjectsOfType<Consumable>())
        {
            if (!Fish.Is(c)) continue;
            foreach (var mf in c.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled || mf.sharedMesh == fishMesh) continue;
                mf.sharedMesh = fishMesh;
                mr.sharedMaterials = fishMats;
                var parent = mf.transform.parent;
                var ps = parent != null ? parent.lossyScale : Vector3.one;
                // Taille d'un poisson de l'aquarium, un peu grossie pour qu'on le voie en main
                mf.transform.localScale = new Vector3(fishSize.x / ps.x, fishSize.y / ps.y, fishSize.z / ps.z) * 1.6f;
                mf.transform.localRotation = Quaternion.identity;
            }
        }
    }

    static Il2CppSystem.Func<bool> Show(Func<bool> f) => DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(f);
    static UnityAction<float> NoTimer() => DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { }));
    static UnityAction<NetworkIdentity> NoStart() => DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { }));
}
