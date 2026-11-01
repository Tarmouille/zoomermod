using System;
using System.Collections.Generic;
using Mirror;
using Player.Lobby;
using Player.Tasks;
using Props.Scripts;
using Props.WorkStation.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Café pour l'équipe" (bleue, Specialist ; voir TaskPools) : faire des cafés à la machine et en déposer 2 dans
// le porte-tasse d'un poste qui n'est pas le sien : collègue ou bureau vide (le même poste peut être servi deux fois).
// Détection chez le joueur, par sondage : une tasse pleine qu'il a eue en main, posée sur le porte-tasse d'un poste
// qui n'est pas le sien. Chaque tasse ne compte qu'une fois. (Café empoisonné à l'encre : tant pis pour le collègue.)
static class CoffeeRound
{
    internal const string Key = "dalemod.cafeEquipe";
    internal const string DeliveredAction = "Delivered", CoffeeObj = "Team Coffee";
    const float PollSeconds = 0.25f, NearHolder = 0.3f;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    static IntPtr taskPtr;
    static readonly HashSet<uint> mine = new(), delivered = new(), warned = new();   // tasses pleines que j'ai tenues / déjà comptées
    static float nextPoll;

    internal static void Tick()
    {
        if (Time.time < nextPoll) return;
        nextPoll = Time.time + PollSeconds;
        var task = CustomTaskPatches.ActiveTask();
        var ptr = task?.Pointer ?? IntPtr.Zero;
        if (ptr != taskPtr) { taskPtr = ptr; mine.Clear(); delivered.Clear(); }
        if (!TaskActive()) return;

        var me = NetworkClient.localPlayer;
        var myLobby = LobbyManager.instance?.GetLocalPlayer()?.netIdentity;
        if (me == null) return;
        var holders = Object.FindObjectsOfType<MugHolder>();
        foreach (var mug in Object.FindObjectsOfType<Mug>())
        {
            bool full = mug.coffeeAmount > 0.01f;
            if (ModItem.HeldBy(mug, me)) { if (full) mine.Add(mug.netId); continue; }
            if (!full || !mine.Contains(mug.netId) || delivered.Contains(mug.netId) || mug.usingPlayerController != null) continue;
            var holder = HolderOf(mug, holders);
            if (holder == null) continue;
            var owner = holder.lobbyPlayerOwner;
            if (owner != null && myLobby != null && owner == myLobby)   // pas son propre poste ; un bureau vide compte
            {
                if (warned.Add(mug.netId)) Plugin.L.LogInfo("[CAFE] tasse posée sur MON poste : ne compte pas");
                continue;
            }
            delivered.Add(mug.netId);
            TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, CoffeeObj, null, DeliveredAction, null, null));
            Plugin.L.LogInfo($"[CAFE] café livré au poste de {(owner == null ? "personne (bureau vide)" : owner.name)} ({delivered.Count}/2)");
        }
    }

    // Porte-tasse où la tasse est posée : son support déclaré, sinon le porte-tasse tout proche
    static MugHolder HolderOf(Mug mug, MugHolder[] holders)
    {
        var h = mug.currentHolder?.TryCast<MugHolder>();
        if (h != null) return h;
        foreach (var x in holders)
            if (Vector3.Distance(x.transform.position, mug.transform.position) < NearHolder) return x;
        return null;
    }
}
