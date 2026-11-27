using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Player.Tasks;
using Props.Scripts;
using Props.VendingMachine;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Distributeur bloqué" (bleue, tirée seulement par le Janitor : CustomTask.JanitorOnly) :
//  1) "Débloquer le distributeur" (5 s, n'importe quel distributeur de la carte) : 2 objets tirés au hasard parmi ceux
//     du distributeur (VendingMachine.items) tombent dans son bac, avec son bruit habituel ;
//  2) et 3) les manger, tous les deux. Le serveur retient les 2 objets tombés : chaque objet mangé par le janitor valide
//     une étape chez lui ; mangé par quelqu'un d'autre, il est perdu et l'action "Débloquer" revient pour en refaire
//     tomber.
static class VendingJam
{
    internal const string Key = "dalemod.distributeurBloque";
    internal const string Obj = "Vending Machine", UnjammedAction = "Unjammed", AteAction = "Ate";
    const float UnjamSeconds = 5f;
    const int Drops = 2;
    // Action serveur : débloquer ; au janitor : objets tombés / un objet mangé / un objet perdu (mangé par un autre)
    const float UnjamAction = -99.1f, DroppedSignal = -99.2f, AteSignal = -99.3f, LostSignal = -99.4f;

    static TaskHook Hook(string action) => new(null, Obj, null, action, null, null);
    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    internal static void Register()
    {
        NetSignal.OnLocalPlayer(DroppedSignal, () =>
        {
            pending = Drops;
            if (!TaskActive()) return;
            if (!unjammed) { unjammed = true; TaskHook.TriggerTaskHookCommandStatic(Hook(UnjammedAction)); }
            Hud.Show(CustomTaskPatches.Text("Deux trucs sont tombés : mange-les !", "Two things dropped: eat them!"), 3f);
            Plugin.L.LogInfo("[DISTRIB] distributeur débloqué");
        });
        NetSignal.OnLocalPlayer(AteSignal, () =>
        {
            pending = Math.Max(0, pending - 1);
            if (!TaskActive()) return;
            TaskHook.TriggerTaskHookCommandStatic(Hook(AteAction));
            Plugin.L.LogInfo("[DISTRIB] un objet tombé mangé : étape validée");
        });
        NetSignal.OnLocalPlayer(LostSignal, () =>
        {
            pending = Math.Max(0, pending - 1);
            if (TaskActive()) Hud.Show(CustomTaskPatches.Text("Quelqu'un a mangé ton échantillon !", "Someone ate your sample!"), 3f);
            Plugin.L.LogInfo("[DISTRIB] un objet tombé mangé par un autre");
        });
    }

    // ===================== Chez le janitor =====================
    static IntPtr taskPtr;
    static bool unjammed;   // étape 1 validée pour cette tâche
    static int pending;     // objets tombés pour moi, pas encore mangés

    static void SyncTask()
    {
        var p = CustomTaskPatches.ActiveTask()?.Pointer ?? IntPtr.Zero;
        if (p == taskPtr) return;
        taskPtr = p; unjammed = false; pending = 0;
    }

    [HarmonyPatch(typeof(VendingMachine), nameof(VendingMachine.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddUnjam(VendingMachine __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var vm = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Débloquer le distributeur", "Unjam the vending machine"), InteractionPermission.All,
            NetSignal.ServerAction(vm, UnjamAction, player => ServerUnjam(vm, player)), false,
            Show(() => { SyncTask(); return TaskActive() && pending == 0; }), UnjamSeconds, NoTimer(), NoStart(), false, false));
    }

    // ===================== Serveur =====================
    // Objet tombé -> janitor à qui il est destiné
    static readonly Dictionary<IntPtr, NetworkIdentity> drops = new();
    static readonly System.Random rng = new();

    static void ServerUnjam(VendingMachine vm, NetworkIdentity player)
    {
        if (!NetworkServer.active || player == null) return;
        var edible = new List<Collectible>();
        if (vm.items != null) foreach (var it in vm.items) if (it != null && it.GetComponent<Consumable>() != null) edible.Add(it);
        if (edible.Count == 0) { Plugin.L.LogWarning("[DISTRIB] aucun objet mangeable dans le distributeur"); return; }
        var at = vm.itemSpawnPos != null ? vm.itemSpawnPos : vm.transform;
        var names = new List<string>();
        for (int i = 0; i < Drops; i++)
        {
            var prefab = edible[rng.Next(edible.Count)];
            var pos = at.position + at.right * (i == 0 ? -0.06f : 0.06f);   // côte à côte dans le bac
            var go = Object.Instantiate(prefab.gameObject, pos, at.rotation);
            NetworkServer.Spawn(go);
            var c = go.GetComponent<Consumable>();
            if (c != null) drops[c.Pointer] = player;
            names.Add(prefab.interactableName);
        }
        try { vm.RpcPlayAudioClip(player); } catch (Exception e) { Plugin.L.LogWarning("[DISTRIB] son : " + e.Message); }
        NetSignal.SendToPlayer(player, DroppedSignal);
        Plugin.L.LogInfo($"[DISTRIB] tombés du distributeur : {string.Join(", ", names)} (parmi {edible.Count} objets mangeables)");
    }

    // Un objet tombé est mangé : au janitor, étape validée ; à un autre, objet perdu (le janitor en refait tomber)
    [HarmonyPatch(typeof(Consumable), nameof(Consumable.InvokeUserCode_CmdConsume__NetworkIdentity__NetworkConnectionToClient))]
    [HarmonyPrefix]
    static void BeforeEat(NetworkBehaviour obj, NetworkConnectionToClient senderConnection)
    {
        var c = obj?.TryCast<Consumable>();
        var eater = senderConnection?.identity;
        if (c == null || eater == null || !drops.Remove(c.Pointer, out var owner) || owner == null) return;
        NetSignal.SendToPlayer(owner, owner == eater ? AteSignal : LostSignal);
    }

    // Pour l'indicateur : les distributeurs (débloquer, puis manger ce qui en est tombé)
    internal static VendingMachine[] Machines() => Object.FindObjectsOfType<VendingMachine>();

    static Il2CppSystem.Func<bool> Show(Func<bool> f) => DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(f);
    static UnityAction<float> NoTimer() => DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { }));
    static UnityAction<NetworkIdentity> NoStart() => DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { }));
}
