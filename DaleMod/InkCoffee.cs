using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Player.Lobby;
using Player.PlayerEffects;
using Player.Tasks;
using Props.Scripts;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Inkish Coffee" (Saboteur) : prendre une cartouche d'encre, la verser dans la machine à café.
// Ensuite les cafés servis par cette machine donnent l'effet "Nauséeux" au lieu du speed, jusqu'à ce qu'un joueur
// la répare (Janitor 5 s, autres rôles 45 s). Pour garder le mystère, l'action "Réparer" n'apparaît qu'après 2 cafés
// servis par la machine empoisonnée (compté par le serveur, signal RevealSignal). L'état "empoisonnée" est diffusé à tous les clients par NetSignal.
static class InkCoffee
{
    internal const string Key = "dalemod.inkishCoffee";
    // Étape 1 = hook du jeu "Picked up Ink Cartridge" : sa flèche vise déjà les cartouches
    internal const string PickedAction = "Picked up", InkObj = "Ink Cartridge", PoisonedAction = "Poisoned", MachineObj = "Coffee Machine";
    const float PoisonOn = -31.1f, PoisonOff = -31.2f, PouredSignal = -31.3f, RevealSignal = -31.4f, PourAction = -31.5f, RepairAction = -31.6f;
    const int ServedBeforeReveal = 2;
    const float PourSeconds = 3f, JanitorRepairSeconds = 5f, RepairSeconds = 45f, NearMachine = 3f;

    internal static TaskHook Hook(string action, string obj) => new(null, obj, null, action, null, null);

    static readonly HashSet<IntPtr> poisoned = new();       // machines empoisonnées (connu de tous les clients)
    static readonly HashSet<IntPtr> revealed = new();       // ... dont la panne se voit (2 cafés servis depuis) : "Réparer" visible
    static readonly Dictionary<IntPtr, int> servedSincePoison = new();   // serveur
    static readonly HashSet<uint> poisonedMugs = new();     // tasses remplies par une machine empoisonnée (serveur), par netId
    static readonly List<InteractionAlternative> repairAlts = new();

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    internal static void Register()
    {
        NetSignal.On(PoisonOn, it =>
        {
            poisoned.Add(it.Pointer);
            Plugin.L.LogInfo("[INK] machine à café empoisonnée");
        });
        // L'étape revient au joueur qui a versé l'encre (signal adressé à lui seul)
        NetSignal.OnLocalPlayer(PouredSignal, () =>
        {
            if (TaskActive()) TaskHook.TriggerTaskHookCommandStatic(Hook(PoisonedAction, MachineObj));
        });
        NetSignal.On(PoisonOff, it => { poisoned.Remove(it.Pointer); revealed.Remove(it.Pointer); Plugin.L.LogInfo("[INK] machine à café réparée"); });
        NetSignal.On(RevealSignal, it => { revealed.Add(it.Pointer); Plugin.L.LogInfo("[INK] la machine à café a l'air bizarre : réparation possible"); });
    }

    // Étape 1 : le jeu envoie lui-même "Picked up Ink Cartridge". Ici : durée de réparation selon le rôle local
    internal static void Tick()
    {
        bool janitor = LobbyManager.instance?.GetLocalPlayer()?.playerRole == PlayerRole.Janitor;
        foreach (var a in repairAlts) a.duration = janitor ? JanitorRepairSeconds : RepairSeconds;
    }

    static bool HeldBy(Collectible c, NetworkIdentity player) =>
        player != null && (c.usingPlayerController == player
                           || (c.currentHolder != null && c.currentHolder.GetComponentInParent<NetworkIdentity>() == player));

    // --- Actions sur la machine à café ---
    [HarmonyPatch(typeof(CoffeeMachine), nameof(CoffeeMachine.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddAlternatives(CoffeeMachine __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var machine = __instance;

        // Verser l'encre : le jeu exige de tenir une cartouche (requireUsable)
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Verser l'encre", "Pour the ink"), InteractionPermission.All,
            NetSignal.ServerAction(machine, PourAction, player => Pour(machine, player)), Il2CppType.Of<InkCartridge>(), false,
            Show(() => TaskActive() && !poisoned.Contains(machine.Pointer)), PourSeconds, false, NoStart(), true));

        // Réparer : tous les rôles, durée ajustée dans Tick (Janitor 5 s, sinon 45 s)
        var repair = new InteractionAlternative(CustomTaskPatches.Text("Réparer la machine à café", "Repair the coffee machine"), InteractionPermission.All,
            NetSignal.ServerAction(machine, RepairAction, _ => Repair(machine)), false,
            Show(() => poisoned.Contains(machine.Pointer) && revealed.Contains(machine.Pointer)), RepairSeconds, NoTimer(), NoStart(), false, false);
        alts.Add(repair);
        repairAlts.Add(repair);
    }

    static void Pour(CoffeeMachine machine, NetworkIdentity player)
    {
        if (!NetworkServer.active) return;
        foreach (var ink in Object.FindObjectsOfType<InkCartridge>())
            if (HeldBy(ink, player)) { ink.ServerDestroyCollectible(); break; }
        servedSincePoison.Remove(machine.Pointer);
        NetSignal.Send(machine, PoisonOn);
        NetSignal.SendToPlayer(player, PouredSignal);
        Plugin.L.LogInfo("[INK] encre versée");
    }

    static void Repair(CoffeeMachine machine)
    {
        if (!NetworkServer.active) return;
        servedSincePoison.Remove(machine.Pointer);
        NetSignal.Send(machine, PoisonOff);
    }

    // --- Cafés empoisonnés (serveur) ---
    // MakeCoffee ne tourne que chez le joueur qui fait le café ; c'est sa commande CmdMakeCoffee qui remplit la tasse
    // sur le serveur (aussi pour l'hôte) : on marque la tasse juste après.
    [HarmonyPatch(typeof(CoffeeMachine), nameof(CoffeeMachine.InvokeUserCode_CmdMakeCoffee__NetworkIdentity__NetworkConnectionToClient))]
    [HarmonyPostfix]
    static void AfterMakeCoffee(NetworkBehaviour obj)
    {
        var __instance = obj?.TryCast<CoffeeMachine>();
        if (!NetworkServer.active || __instance == null || __instance.collectibles == null) return;
        bool bad = poisoned.Contains(__instance.Pointer);
        if (bad)
        {
            int n = servedSincePoison.TryGetValue(__instance.Pointer, out var c) ? c + 1 : 1;
            servedSincePoison[__instance.Pointer] = n;
            if (n == ServedBeforeReveal) NetSignal.Send(__instance, RevealSignal);   // 2e café empoisonné : réparation visible
        }
        foreach (var entry in __instance.collectibles)
        {
            var mug = entry.Key?.TryCast<Mug>();
            if (mug == null) continue;
            if (bad) poisonedMugs.Add(mug.netId); else poisonedMugs.Remove(mug.netId);
            Plugin.L.LogInfo($"[INK] tasse {mug.netId} remplie {(bad ? "EMPOISONNÉE" : "propre")}");
        }
    }

    // Après le speed du café : on le remplace par la nausée si la tasse est empoisonnée
    [HarmonyPatch(typeof(Mug), nameof(Mug.InvokeUserCode_CmdDrinkCoffee__NetworkIdentity__NetworkConnectionToClient))]
    [HarmonyPostfix]
    static void AfterDrink(NetworkBehaviour obj)
    {
        var mug = obj?.TryCast<Mug>();
        if (mug == null || !poisonedMugs.Contains(mug.netId)) return;
        var fx = mug.usingPlayerController?.GetComponent<PlayerEffectController>();
        if (fx == null) { Plugin.L.LogWarning("[INK] buveur introuvable"); return; }
        Nausea.Apply(fx);
        Plugin.L.LogInfo("[INK] café empoisonné bu");
    }

    static Il2CppSystem.Func<bool> Show(Func<bool> f) => DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(f);
    static UnityAction<float> NoTimer() => DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { }));
    static UnityAction<NetworkIdentity> NoStart() => DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { }));
}
