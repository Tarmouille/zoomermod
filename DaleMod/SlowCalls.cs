using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Player.Scripts;

namespace DaleMod;

// Diagnostic (Debug) : appels lents de l'écran de personnalisation et du changement d'apparence. Chaque méthode
// de NewCustomizeTab, PlayerCustomizer.Customize et nos étapes autour sont chronométrées ; un appel de plus de 15 ms est écrit dans le log
// ("[LENT] nom : x ms"), avec le nombre de Customize qu'il a déclenchés.
#if DEBUG
[HarmonyPatch]
static class SlowCalls
{
    const double LogMs = 10;
    static int customizeCalls, depth;

    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var m in typeof(NewCustomizeTab).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            if (!m.IsSpecialName && !m.IsGenericMethod && !m.Name.StartsWith("Method_")) yield return m;
        yield return AccessTools.Method(typeof(PlayerCustomizer), nameof(PlayerCustomizer.Customize));
        // Nos étapes autour de Customize (ajout des objets du mod, placement sur la tête, coupe du crâne…)
        foreach (var (t, n) in new[] { (typeof(Cosmetics), "Ensure"), (typeof(Cosmetics), "Place"),
                                       (typeof(Cosmetics), "EnsureTab"), (typeof(Cosmetics), "MatchLayers"),
                                       (typeof(CosmeticExtras), "AddHats"), (typeof(CosmeticExtras), "AddClothes"), (typeof(CosmeticExtras), "PlaceHats"),
                                       (typeof(FaceGear), "Place"), (typeof(Ponpons), "Place"), (typeof(SkullCut), "Apply"),
                                       (typeof(FaceGear), "AddItems"), (typeof(Robe), "NewItem"), (typeof(Tabard), "NewItem"), (typeof(Doudoune), "NewItem"),
                                       (typeof(Ponpons), "NewItem"), (typeof(SkullCut), "NewItem"), (typeof(Cosmetics), "TexturedFrom") })
        {
            var m = AccessTools.FirstMethod(t, x => x.Name == n);
            if (m != null) yield return m; else Plugin.L.LogWarning($"[LENT] {t.Name}.{n} introuvable");
        }
    }

    static void Prefix(MethodBase __originalMethod, out (long t0, int calls) __state)
    {
        if (__originalMethod.Name == nameof(PlayerCustomizer.Customize)) customizeCalls++;
        depth++;
        __state = (Stopwatch.GetTimestamp(), customizeCalls);
    }

    static void Finalizer(MethodBase __originalMethod, (long t0, int calls) __state)
    {
        depth--;
        double ms = (Stopwatch.GetTimestamp() - __state.t0) * 1000.0 / Stopwatch.Frequency;
        if (ms < LogMs) return;
        int inner = customizeCalls - __state.calls;
        Plugin.L.LogInfo($"[LENT] {new string(' ', depth * 2)}{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} : {ms:0} ms" +
                         (inner > 0 ? $" ({inner} Customize dedans)" : ""));
    }
}
#endif
