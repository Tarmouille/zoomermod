using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Player.Tasks;
using Props.Scripts;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâches "Tupperware 2.0" : prendre un tupperware de bolo dans le frigo, le manger (7 s, speed), puis
// laver le tupperware sale à un évier (Specialist) ou le remettre dans le frigo "ni vu ni connu" (Slacker).
// N'importe qui peut prendre/manger un tupperware ; plein ou sale, il peut traîner par terre et finir à la poubelle.
// Étapes "pris" et "mangé" : détectées chez le joueur (ce qu'il tient) ; "lavé"/"remis" : signal NetSignal depuis l'évier/le frigo.
static class Tupperware
{
    internal const string SpecialistKey = "dalemod.tupperware", SlackerKey = "dalemod.tupperwareNiVuNiConnu";
    internal const string Obj = "Tupperware", TookAction = "Took", AteAction = "Ate", WashedAction = "Washed", ReturnedAction = "Returned";
    const float EatSeconds = 7f, WashSeconds = 4f, ReturnSeconds = 3f, TakeSeconds = 1.5f, Near = 3f;
    const float WashedSignal = -41.1f, ReturnedSignal = -41.2f;

    internal static readonly Dictionary<string, Color> Colors = new()
    {
        ["plastic"] = new Color(0.82f, 0.86f, 0.88f),
        ["plastic_dirty"] = new Color(0.80f, 0.74f, 0.64f),
        ["lid"] = new Color(0.27f, 0.55f, 0.86f),
        ["lid_dark"] = new Color(0.16f, 0.37f, 0.67f),
        ["sauce"] = new Color(0.67f, 0.18f, 0.10f),
        ["pasta"] = new Color(0.94f, 0.80f, 0.43f),
        ["meat"] = new Color(0.47f, 0.22f, 0.12f),
        ["smear"] = new Color(0.59f, 0.27f, 0.16f),
        ["food"] = new Color(0.62f, 0.25f, 0.12f),     // couleurs de repli si une texture manque
        ["dirty"] = new Color(0.85f, 0.76f, 0.64f),
    };
    internal static readonly Vector3 Scale = new(0.18f, 0.09f, 0.13f);
    // Textures générées par tools/make_textures.py (UV des modèles : tools/make_tupperware.py)
    internal static readonly Dictionary<string, string> Textures = new()
    {
        ["food"] = "tupp_food.png", ["plastic"] = "tupp_plastic.png", ["lid"] = "tupp_lid.png",
        ["dirty"] = "tupp_dirty.png", ["sauce"] = "tupp_sauce.png",
    };

    internal static readonly ModItem Full = new("Tupperware", 0xC0CA1002, "tupperware_full.obj", Scale, Colors) { ResetChildRotation = true, SitOnPivot = true, Textures = Textures };
    internal static readonly ModItem Dirty = new("Dirty Tupperware", 0xC0CA1003, "tupperware_dirty.obj", Scale, Colors) { ResetChildRotation = true, SitOnPivot = true, Textures = Textures };

    internal static TaskHook Hook(string action) => new(null, Obj, null, action, null, null);

    static Task Active() => CustomTaskPatches.ActiveTask();
    static bool TaskActive() => CustomTaskPatches.IsActive(Active(), SpecialistKey) || CustomTaskPatches.IsActive(Active(), SlackerKey);

    internal static void Register()
    {
        NetSignal.OnLocalPlayer(WashedSignal, () => Step(WashedAction, SpecialistKey));
        NetSignal.OnLocalPlayer(ReturnedSignal, () => Step(ReturnedAction, SlackerKey));
    }

    // Chez le joueur qui a lavé / remis le tupperware (signal adressé à lui seul)
    static void Step(string action, string key)
    {
        Plugin.L.LogInfo($"[TUPP] {action}");
        if (CustomTaskPatches.IsActive(Active(), key)) TaskHook.TriggerTaskHookCommandStatic(Hook(action));
    }

    // --- Étapes 1 et 2 : ce que tient le joueur local ---
    static bool tookSent, ateSent;

    // Serveur : tupperwares sales à mettre en main un peu après le repas (la main n'est pas libre tout de suite)
    const float GiveDelay = 0.5f;
    static readonly List<(NetworkIdentity player, float at)> pendingDirty = new();

    internal static void Tick()
    {
        for (int i = pendingDirty.Count - 1; i >= 0; i--)
        {
            var (player, at) = pendingDirty[i];
            if (Time.time < at) continue;
            pendingDirty.RemoveAt(i);
            if (NetworkServer.active && player != null) Dirty.GiveTo(player);
        }

        if (!TaskActive()) { tookSent = ateSent = false; return; }
        if (!tookSent && Full.LocalHolds()) { tookSent = true; TaskHook.TriggerTaskHookCommandStatic(Hook(TookAction)); Plugin.L.LogInfo("[TUPP] repas pris"); }
        if (!ateSent && Dirty.LocalHolds()) { ateSent = true; TaskHook.TriggerTaskHookCommandStatic(Hook(AteAction)); Plugin.L.LogInfo("[TUPP] repas mangé"); }
    }

    // --- Tupperware plein : "Manger" (7 s) ; sale : pas d'action de consommation ---
    [HarmonyPatch(typeof(Consumable), nameof(Consumable.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void SetUpActions(Consumable __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null || alts.Count == 0) return;
        if (Full.Is(__instance))
        {
            var eat = alts[alts.Count - 1];   // "Consume" du soda
            eat.text = CustomTaskPatches.Text("Manger", "Eat");
            eat.duration = EatSeconds;
        }
        else if (Dirty.Is(__instance)) alts.RemoveAt(alts.Count - 1);
    }

    // Serveur : manger donne le speed, et le tupperware sale arrive en main. Consume ne tourne que chez le joueur qui
    // mange ; c'est sa commande CmdConsume qui arrive au serveur (aussi pour l'hôte) : on s'y accroche.
    [HarmonyPatch(typeof(Consumable), nameof(Consumable.InvokeUserCode_CmdConsume__NetworkIdentity__NetworkConnectionToClient))]
    [HarmonyPrefix]
    static void BeforeEat(NetworkBehaviour obj, NetworkConnectionToClient senderConnection)
    {
        var c = obj?.TryCast<Consumable>();
        var player = senderConnection?.identity;
        if (c == null || !Full.Is(c) || player == null) return;
        CokeBagPatches.ApplySpeed(player);
        pendingDirty.Add((player, Time.time + GiveDelay));
        Plugin.L.LogInfo($"[TUPP] tupperware mangé par {player.name} -> sale");
    }

    // --- Frigo : prendre un repas (tout le monde) / remettre le tupperware vide ---
    [HarmonyPatch(typeof(KitchenCabinetController), nameof(KitchenCabinetController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void FridgeActions(KitchenCabinetController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var fridge = __instance;
        if (fridge.interactableName != ModItem.FridgeName) return;   // pas les placards / casiers / post-it

        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Prendre un tupperware", "Take a tupperware"), InteractionPermission.All,
            NetSignal.ServerAction(fridge, -41.5f, player => { if (UnderLimit()) Full.GiveTo(player); }), false,
            Show(() => UnderLimit() && !Full.LocalHolds() && !Dirty.LocalHolds()), TakeSeconds, NoTimer(), NoStart(), false, false));

        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Remettre le tupperware", "Put the tupperware back"), InteractionPermission.All,
            NetSignal.ServerAction(fridge, -41.6f, player => ServerConsumeDirty(player, fridge, ReturnedSignal)), HeldItem, false,
            Show(Dirty.LocalHolds), ReturnSeconds, false, NoStart(), false));
    }

    // Objet tenu requis par l'action : sans ça, le jeu masque l'action quand on a quelque chose en main
    static Il2CppSystem.Type HeldItem => Il2CppType.Of<Consumable>();

    // Même plafond que les mugs / bières du frigo ; plein et sale comptent comme un seul tupperware
    static bool UnderLimit() => ModItem.CountInScene(Full, Dirty) < ModItem.GameLimit();

    // --- Lavabos des toilettes (pas l'évier de la cuisine) : laver le tupperware sale ---
    const float ToiletSinkRadius = 8f;
    static readonly Dictionary<IntPtr, bool> toiletSink = new();

    internal static bool IsToiletSink(SinkController sink)
    {
        if (toiletSink.TryGetValue(sink.Pointer, out var v)) return v;
        v = false;
        foreach (var t in Object.FindObjectsOfType<Props.WC.Toilet.Toilet>())
            if (Vector3.Distance(t.transform.position, sink.transform.position) < ToiletSinkRadius) { v = true; break; }
        toiletSink[sink.Pointer] = v;
        Plugin.L.LogInfo($"[TUPP] évier '{sink.name}' : {(v ? "toilettes" : "cuisine")}");
        return v;
    }
    [HarmonyPatch(typeof(SinkController), nameof(SinkController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void SinkActions(SinkController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var sink = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Laver le tupperware", "Wash the tupperware"), InteractionPermission.All,
            NetSignal.ServerAction(sink, -41.7f, player => ServerConsumeDirty(player, sink, WashedSignal)), HeldItem, false,
            Show(() => IsToiletSink(sink) && Dirty.LocalHolds()), WashSeconds, false, NoStart(), false));
    }

    // Serveur : le tupperware sale tenu disparaît, et les clients sont prévenus (validation de l'étape)
    static void ServerConsumeDirty(NetworkIdentity player, Interactable at, float signal)
    {
        Plugin.L.LogInfo($"[TUPP] action sur '{at.name}' (serveur={NetworkServer.active})");
        if (!NetworkServer.active || !Dirty.ServerRemoveHeldBy(player)) return;
        NetSignal.SendToPlayer(player, signal);
    }

    static Il2CppSystem.Func<bool> Show(Func<bool> f) => DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(f);
    static UnityAction<float> NoTimer() => DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { }));
    static UnityAction<NetworkIdentity> NoStart() => DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { }));
}
