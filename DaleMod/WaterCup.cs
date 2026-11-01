using System;
using Mirror;
using Player.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Gobelet de la fontaine à eau, partagé par "Douche froide" et "Dégât des eaux".
// Étape 1 "Remplir un gobelet" : détectée chez le joueur (il tient un gobelet qui contient de l'eau), comme le tupperware.
static class WaterCup
{
    internal const string FilledAction = "Filled", CupObj = "Water Cup";
    const float HandReach = 2f;

    static IntPtr filledFor;   // tâche pour laquelle l'étape "rempli" a déjà été envoyée

    internal static TaskHook Hook(string action, string obj) => new(null, obj, null, action, null, null);

    internal static void Tick()
    {
        var task = CustomTaskPatches.ActiveTask();
        if (task == null || task.Pointer == filledFor) return;
        if (!CustomTaskPatches.IsActive(task, ColdShower.Key) && !CustomTaskPatches.IsActive(task, WaterDamage.Key)) return;
        if (!HasWater(NetworkClient.localPlayer)) return;
        filledFor = task.Pointer;
        TaskHook.TriggerTaskHookCommandStatic(Hook(FilledAction, CupObj));
        Plugin.L.LogInfo("[WATER] gobelet rempli");
    }

    // Gobelet tenu par le joueur ; repli : le plus proche à portée de main (comme l'extincteur, la prise en main peut échapper à HeldBy)
    internal static WaterCupController Held(NetworkIdentity player)
    {
        if (player == null) return null;
        WaterCupController best = null;
        float bestD = HandReach;
        foreach (var c in Object.FindObjectsOfType<WaterCupController>())
        {
            if (ModItem.HeldBy(c, player)) return c;
            if (c.usingPlayerController != null && c.usingPlayerController != player) continue;   // dans la main d'un autre
            float d = Vector3.Distance(c.transform.position, player.transform.position);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    internal static bool HasWater(NetworkIdentity player)
    {
        var c = Held(player);
        return c != null && c.waterAmount > 0.01f;
    }

    // Serveur
    internal static void Empty(WaterCupController cup)
    {
        if (NetworkServer.active && cup != null) cup.ServerSetWaterAmount(0f);
    }
}
