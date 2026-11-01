using HarmonyLib;
using Player.Tasks;

namespace DaleMod;

// Tâche "Moulin à vent" (rouge, Slacker ; voir TaskPools) : poste de travail, dessiner dans Paint, imprimer, punaiser au panneau.
// Les clics PC sont rejoués chez tous les clients : on ne compte que le Paint du PC du joueur local.
// Les étapes "dessiner" et "imprimer" servent aussi à "L'Autographe-gate" (Autograph.cs).
static class Windmill
{
    internal const string Key = "dalemod.moulinAVent";
    internal const string DrewAction = "Drew", PrintedAction = "Printed", DrawingObj = "DaleDrawing";

    internal static TaskHook Hook(string action) => new(null, DrawingObj, null, action, null, null);

    static bool OnLocalPc(Paint p)
    {
        var local = PcMod.LocalComputer();
        return local != null && p.computerController != null && p.computerController.Pointer == local.Pointer
               && DrawTaskActive();
    }

    // Moulin à vent, Autographe-gate ou STONKS
    internal static bool DrawTaskActive()
    {
        var t = CustomTaskPatches.ActiveTask();
        return CustomTaskPatches.IsActive(t, Key) || CustomTaskPatches.IsActive(t, Autograph.Key) || CustomTaskPatches.IsActive(t, Autograph.StonksKey);
    }

    [HarmonyPatch(typeof(Paint), nameof(Paint.OnCursorClick))]
    [HarmonyPostfix]
    static void OnDraw(Paint __instance)
    {
        if (!OnLocalPc(__instance)) return;
        TaskHook.TriggerTaskHookCommandStatic(Hook(DrewAction));
        Plugin.L.LogInfo("[MOULIN] dessin");
    }

    [HarmonyPatch(typeof(Paint), nameof(Paint.Print))]
    [HarmonyPostfix]
    static void OnPrint(Paint __instance)
    {
        Plugin.L.LogInfo($"[MOULIN] impression Paint (PC local={OnLocalPc(__instance)})");
        if (!OnLocalPc(__instance)) return;
        TaskHook.TriggerTaskHookCommandStatic(Hook(PrintedAction));
        Autograph.NotePrint();
    }
}
