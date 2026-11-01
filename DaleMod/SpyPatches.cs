using HarmonyLib;
using Player.Tasks;

namespace DaleMod;

// Observation seulement : journalise les hooks et l'avancement des tâches.
static class SpyPatches
{
    internal static string Hook(TaskHook h) => h == null ? "null" :
        $"action='{h.action}' object='{h.passiveObjectName}' owner='{h.passiveObjectOwner}' " +
        $"player='{h.passivePlayer}' location='{h.location}' locationOwner='{h.locationOwner}'";

    [HarmonyPatch(typeof(TaskHook), nameof(TaskHook.TriggerTaskHookCommandStatic))]
    [HarmonyPrefix]
    static void OnHook(TaskHook taskHook) => Plugin.L.LogInfo($"[HOOK] {Hook(taskHook)}");

    [HarmonyPatch(typeof(Task), nameof(Task.OnTaskHookCommand))]
    [HarmonyPostfix]
    static void OnTaskHook(Task __instance, TaskHook taskHook)
    {
        var steps = __instance.subTasks;
        if (steps == null) return;
        Plugin.L.LogInfo($"[MATCH] {__instance.GetIl2CppType().Name} reçoit ({Hook(taskHook)})");
        for (int i = 0; i < steps.Count; i++)
        {
            var st = steps[i];
            Plugin.L.LogInfo($"[MATCH]    [{(st.Item3 ? "x" : " ")}] '{st.Item2}' <- {Hook(st.Item1)}");
        }
    }
}
