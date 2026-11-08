using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace DaleMod;

// Mesure du temps passé dans le mod : toutes les 10 s, une ligne [PERF] dans le log (images/s, ms du mod par image,
// morceaux les plus coûteux avec leur nombre d'appels). Sert à trouver les baisses d'images/s.
static class Perf
{
    const float Every = 10f;
    static readonly Dictionary<string, (long ticks, int calls)> acc = new();
    static float since = -1f;
    static int frames;

    internal static void Run(string name, Action a)
    {
        long t0 = Stopwatch.GetTimestamp();
        try { a(); }
        catch (Exception ex) { Plugin.L.LogError($"[{name}] " + ex); }
        Add(name, Stopwatch.GetTimestamp() - t0);
    }

    internal static long Start() => Stopwatch.GetTimestamp();
    internal static void Stop(string name, long t0) => Add(name, Stopwatch.GetTimestamp() - t0);

    static void Add(string name, long ticks)
    {
        acc.TryGetValue(name, out var v);
        acc[name] = (v.ticks + ticks, v.calls + 1);
    }

    // Une fois par image
    internal static void Frame()
    {
        if (since < 0f) since = Time.unscaledTime;
        frames++;
        float dt = Time.unscaledTime - since;
        if (dt < Every) return;
        double total = 0;
        var l = new List<(string n, double ms, int calls)>();
        foreach (var (n, v) in acc)
        {
            double ms = v.ticks * 1000.0 / Stopwatch.Frequency / frames;
            total += ms;
            l.Add((n, ms, v.calls));
        }
        l.Sort((a, b) => b.ms.CompareTo(a.ms));
        var s = new System.Text.StringBuilder();
        s.Append($"[PERF] {frames / dt:0} img/s, mod {total:0.00} ms/image :");
        for (int i = 0; i < l.Count && i < 6; i++) s.Append($" {l[i].n} {l[i].ms:0.00} ms ({l[i].calls / dt:0}/s) ;");
        Plugin.L.LogInfo(s.ToString());
        acc.Clear();
        frames = 0;
        since = Time.unscaledTime;
    }
}
