using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Objects.Scripts;
using Props.Scripts;
using UnityEngine;
using UnityEngine.Events;

namespace DaleMod;

// Action "Prendre le sachet" sur le bureau du joueur pendant la tâche "Rail de bureau",
// sur le modèle de "Prendre le paquet de cigarettes" : même durée d'interaction, puis un délai avant d'en reprendre un.
static class DeskBagPatches
{
    internal const string RailKey = "dalemod.railDeBureau";
    const string CigaretteKey = "environment.workStation.interactions.pickCigarettePack";
    const float RetakeCooldown = 20f;
    static float lastTake = -999f;

    [HarmonyPatch(typeof(WorkStationController), nameof(WorkStationController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddTakeBag(WorkStationController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;

        float duration = 0f;
        var cigText = LocalizationManager.instance?.GetLocalizedValue(CigaretteKey);
        foreach (var a in alts) if (a.text == cigText) duration = a.duration;

        var desk = __instance;
        // Le sachet est créé par le serveur ; le délai avant d'en reprendre un est compté chez le joueur
        var action = NetSignal.ServerAction(desk, -11.5f, Take, () => lastTake = Time.time);
        var show = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(new Func<bool>(() => CanTake(desk)));
        var noTimer = DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { }));
        var noStart = DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { }));
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Prendre le sachet", "Take the bag"), InteractionPermission.All, action, false, show, duration, noTimer, noStart, false, false));
    }

    static Player.Tasks.Task ActiveRail()
    {
        var task = CustomTaskPatches.ActiveTask();
        // La tâche "Rail de bureau" elle-même (son support ExtinguishPumpkinTask sert aussi à d'autres tâches custom)
        return CustomTaskPatches.IsActive(task, RailKey) ? task : null;
    }

    static bool CanTake(WorkStationController desk)
    {
        var owner = desk.ownerLobbyPlayer;
        return ActiveRail() != null
               && Time.time - lastTake > RetakeCooldown
               && ModItem.CountInScene(CokeBag.Item) < ModItem.GameLimit()   // même plafond que les objets du frigo
               && owner != null && owner.netId == DebugMenu.LocalTC.netId;   // uniquement son propre bureau
    }

    // Serveur (la tâche a été vérifiée chez le joueur par CanTake : celle de l'hôte n'a rien à voir)
    static void Take(NetworkIdentity player)
    {
        if (ModItem.CountInScene(CokeBag.Item) >= ModItem.GameLimit()) return;
        CokeBag.GiveTo(player);
    }

    // "Sniffer" n'apparaît qu'aux toilettes : impossible de gaspiller le sachet ailleurs
    [HarmonyPatch(typeof(Consumable), nameof(Consumable.CanConsume))]
    [HarmonyPostfix]
    static void OnlyInToilets(Consumable __instance, ref bool __result)
    {
        if (__result && __instance.interactableName == CokeBag.Name && CokeBag.RequireToilets && !CokeBag.LocalPlayerNearToilet())
            __result = false;
    }
}
