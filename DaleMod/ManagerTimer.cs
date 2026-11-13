using System;
using GameManagement;
using UnityEngine;

namespace DaleMod;

// Minuteur de licenciement du manager (GameManager.terminationTimer, terminationMaxTime) : un vrai renvoi le relance.
// Nos renvois remplacés (Jester viré, Pistonné sauvé) sautent le vrai renvoi : on le relance nous-mêmes, chez tous.
// Sens du décompte appris en l'observant (il est à l'arrêt pendant les réunions, où se font les renvois) :
// +1 monte (relance à 0), -1 descend (relance au maximum).
static class ManagerTimer
{
    static float last = float.NaN;
    static int dir;

    internal static void Watch(GameManager gm)
    {
        try
        {
            float t = gm.terminationTimer, d = t - last;
            if (!float.IsNaN(last) && Mathf.Abs(d) > 1e-4f && Mathf.Abs(d) < 1f) dir = d > 0 ? 1 : -1;
            last = t;
        }
        catch { }
    }

    internal static void Reset(GameManager gm, string why)
    {
        try
        {
            float before = gm.terminationTimer, reset = dir > 0 ? 0f : gm.terminationMaxTime;
            if (dir == 0) Plugin.L.LogWarning("[TIMER] sens du minuteur inconnu : supposé décroissant");
            gm.terminationTimer = reset;
            gm.RpcResetTerminationTimer(reset);
            last = float.NaN;
            Plugin.L.LogInfo($"[TIMER] {why} : minuteur de licenciement relancé {before:0.0} -> {reset:0.0} (max {gm.terminationMaxTime:0.0}, sens {dir})");
        }
        catch (Exception e) { Plugin.L.LogError("[TIMER] " + e); }
    }
}
