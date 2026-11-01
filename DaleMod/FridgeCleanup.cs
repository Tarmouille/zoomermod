using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Player.PlayerEffects;
using Player.Tasks;
using Props.Scripts;
using Props.TrashBin;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Inventaire du frigo" (bleue, Specialist ; voir TaskPools) : sortir 3 aliments périmés du frigo et les jeter.
// - "Sortir un aliment périmé" au frigo : seulement avec la tâche ; tirage au hasard entre tupperware moisi, yaourt
//   périmé et part de pizza périmée (objets réseau ModItem, qui ne se mangent pas et ne disparaissent pas tout seuls).
// - Odeur : bouffées vertes au-dessus de chaque aliment, chez tous les joueurs (Puffs).
// - Tenu 10 s d'affilée : nausée (serveur), puis toutes les 10 s tant qu'on le garde.
// - Jeté à la poubelle par n'importe qui : l'étape revient au joueur qui l'a sorti du frigo (signal adressé à lui).
static class FridgeCleanup
{
    internal const string Key = "dalemod.inventaireFrigo";
    internal const string ThrownAction = "Thrown", FoodObj = "Expired Food";
    const float TakeSeconds = 2f, NauseaAfter = 10f, OdorEvery = 0.35f;
    const float ThrownSignal = -81.1f, TakeAction = -81.5f;

    static readonly Color Mold = new(0.36f, 0.6f, 0.22f), Odor = new(0.45f, 0.78f, 0.3f);

    static Dictionary<string, Color> MoldyColors()
    {
        var c = new Dictionary<string, Color>(Tupperware.Colors) { ["sauce"] = Mold, ["smear"] = Mold };
        return c;
    }
    static Dictionary<string, string> MoldyTextures()
    {
        var t = new Dictionary<string, string>(Tupperware.Textures);
        t.Remove("sauce");   // fond vert uni au lieu de la sauce
        return t;
    }

    static readonly ModItem[] Items =
    {
        new("Moldy Tupperware", 0xC0CA1004, "tupperware_dirty.obj", Tupperware.Scale, MoldyColors())
            { ResetChildRotation = true, SitOnPivot = true, Textures = MoldyTextures(), NeverDespawn = true },
        new("Expired Yogurt", 0xC0CA1005, "yogurt.obj", new Vector3(0.07f, 0.08f, 0.07f), new Dictionary<string, Color>
            {
                ["cup"] = new(0.93f, 0.91f, 0.87f), ["label"] = new(0.9f, 0.43f, 0.55f),
                ["foil"] = new(0.75f, 0.77f, 0.79f), ["mold"] = Mold,
            }) { ResetChildRotation = true, SitOnPivot = true, NeverDespawn = true },
        new("Expired Pizza", 0xC0CA1006, "pizza.obj", new Vector3(0.14f, 0.03f, 0.18f), new Dictionary<string, Color>
            {
                ["dough"] = new(0.84f, 0.67f, 0.41f), ["cheese"] = new(0.94f, 0.78f, 0.35f), ["crust"] = new(0.7f, 0.47f, 0.24f),
                ["pepperoni"] = new(0.67f, 0.16f, 0.12f), ["mold"] = Mold,
            }) { ResetChildRotation = true, SitOnPivot = true, NeverDespawn = true },
    };

    static bool IsSpoiled(Interactable it) => Array.Exists(Items, i => i.Is(it));
    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);
    static bool LocalHoldsOne() => Array.Exists(Items, i => i.LocalHolds());

    internal static void Register()
    {
        _ = Items.Length;   // objets réseau créés dès le chargement (enregistrés ensuite à chaque session, comme le sachet)
        NetSignal.OnLocalPlayer(ThrownSignal, () =>
        {
            if (!TaskActive()) return;
            TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, FoodObj, null, ThrownAction, null, null));
            Plugin.L.LogInfo("[FRIGO] aliment périmé jeté");
        });
    }

    // --- Frigo : sortir un aliment périmé (seulement avec la tâche) ---
    [HarmonyPatch(typeof(KitchenCabinetController), nameof(KitchenCabinetController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void FridgeAction(KitchenCabinetController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null || __instance.interactableName != ModItem.FridgeName) return;
        var fridge = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Sortir un aliment périmé", "Take out expired food"), InteractionPermission.All,
            NetSignal.ServerAction(fridge, TakeAction, Take), false,
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(new Func<bool>(() => TaskActive() && !LocalHoldsOne())),
            TakeSeconds,
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<float>>(new Action<float>(_ => { })),
            Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { })),
            false, false));
    }

    // Serveur : on retient qui l'a sorti (l'étape lui revient, même si quelqu'un d'autre le jette)
    static readonly Dictionary<uint, NetworkIdentity> owners = new();

    static void Take(NetworkIdentity player)
    {
        var item = Items[UnityEngine.Random.Range(0, Items.Length)];
        var go = item.GiveTo(player);
        var id = go?.GetComponent<NetworkIdentity>();
        if (id != null) owners[id.netId] = player;
        Plugin.L.LogInfo($"[FRIGO] {item.Name} sorti par {player?.name}");
    }

    // --- Pas d'action "Manger" ---
    [HarmonyPatch(typeof(Consumable), nameof(Consumable.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void NotEdible(Consumable __instance)
    {
        var alts = __instance.alternatives;
        if (alts != null && alts.Count > 0 && IsSpoiled(__instance)) alts.RemoveAt(alts.Count - 1);   // "Consume" du soda
    }

    // --- Poubelle (serveur) : l'aliment tenu par celui qui jette compte pour son propriétaire ---
    [HarmonyPatch(typeof(TrashBin), nameof(TrashBin.InvokeUserCode_CmdThrowAway__NetworkIdentity__NetworkConnectionToClient))]
    [HarmonyPrefix]
    static void BeforeThrow(NetworkConnectionToClient senderConnection)
    {
        var thrower = senderConnection?.identity;
        if (thrower == null) return;
        foreach (var c in Object.FindObjectsOfType<Consumable>())
        {
            if (!IsSpoiled(c) || !ModItem.HeldBy(c, thrower)) continue;
            Plugin.L.LogInfo($"[FRIGO] {c.interactableName} jeté par {thrower.name}");
            if (owners.Remove(c.netId, out var owner) && owner != null) NetSignal.SendToPlayer(owner, ThrownSignal);
            break;
        }
    }

    // --- Chaque frame : odeur (tous les clients) et nausée (serveur) ---
    static float nextOdor;
    static readonly Dictionary<uint, (NetworkIdentity holder, float since)> holding = new();

    internal static void Tick()
    {
        if (Time.time < nextOdor) return;
        nextOdor = Time.time + OdorEvery;
        var alive = new HashSet<uint>();
        foreach (var c in Object.FindObjectsOfType<Consumable>())
        {
            if (!IsSpoiled(c)) continue;
            alive.Add(c.netId);
            Puffs.Emit(c.transform.position + Vector3.up * 0.08f + UnityEngine.Random.insideUnitSphere * 0.03f, Odor,
                       UnityEngine.Random.Range(0.1f, 0.16f), rise: 0.3f, life: 1.6f, alpha: 0.35f, drift: 0.05f);

            if (!NetworkServer.active) continue;
            var holder = c.usingPlayerController;
            if (holder == null) { holding.Remove(c.netId); continue; }
            if (!holding.TryGetValue(c.netId, out var h) || h.holder != holder) { holding[c.netId] = (holder, Time.time); continue; }
            if (Time.time - h.since < NauseaAfter) continue;
            var fx = holder.GetComponent<PlayerEffectController>();
            if (fx != null) Nausea.Apply(fx);
            holding[c.netId] = (holder, Time.time);   // et ainsi de suite toutes les 10 s
            Plugin.L.LogInfo($"[FRIGO] {holder.name} tient {c.interactableName} depuis 10 s : nauséeux");
        }
        foreach (var k in new List<uint>(holding.Keys)) if (!alive.Contains(k)) holding.Remove(k);
    }
}
