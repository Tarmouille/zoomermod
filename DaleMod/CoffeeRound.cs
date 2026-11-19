using System;
using System.Collections.Generic;
using HarmonyLib;
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
// Détection chez le joueur : la tasse pleine qu'il tient, posée sur le porte-tasse d'un poste qui n'est pas le sien.
// Le jeu le signale (TaskHook "Placed" Mug, location vide, locationOwner = propriétaire du poste) : compté sur cet
// événement, même si le collègue reprend ou boit la tasse aussitôt (le sondage seul ratait ces cas). Le sondage reste
// en secours. Chaque tasse ne compte qu'une fois. (Café empoisonné à l'encre : tant pis pour le collègue.)
static class CoffeeRound
{
    internal const string Key = "dalemod.cafeEquipe";
    internal const string DeliveredAction = "Delivered", CoffeeObj = "Team Coffee";
    const float PollSeconds = 0.25f, NearHolder = 0.3f;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    static IntPtr taskPtr;
    static readonly HashSet<uint> mine = new(), delivered = new(), warned = new();   // tasses pleines que j'ai tenues / déjà comptées
    static float nextPoll;
    static uint inHand;   // tasse pleine dans ma main au dernier sondage (0 : aucune)

    static void SyncTask()
    {
        var ptr = CustomTaskPatches.ActiveTask()?.Pointer ?? IntPtr.Zero;
        if (ptr != taskPtr) { taskPtr = ptr; mine.Clear(); delivered.Clear(); inHand = 0; }
    }

    // Tasse posée par moi : sur un porte-tasse de poste (location vide), pas la machine à café ni ailleurs
    [HarmonyPatch(typeof(TaskHook), nameof(TaskHook.TriggerTaskHookCommandStatic))]
    [HarmonyPrefix]
    static void OnHook(TaskHook taskHook)
    {
        if (taskHook == null || taskHook.action != "Placed" || taskHook.passiveObjectName != "Mug") return;
        SyncTask();
        if (!TaskActive()) return;
        uint id = inHand;
        if (id == 0 || !string.IsNullOrEmpty(taskHook.location)) return;
        var owner = taskHook.locationOwner ?? "";
        var me = LobbyManager.instance?.GetLocalPlayer()?.username;
        if (owner.Length > 0 && owner == me)
        {
            if (warned.Add(id)) Plugin.L.LogInfo("[CAFE] tasse posée sur MON poste : ne compte pas");
            return;
        }
        Deliver(id, owner.Length > 0 ? owner : "personne (bureau vide)");
    }

    static void Deliver(uint id, string to)
    {
        inHand = 0;
        if (!delivered.Add(id)) return;
        TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, CoffeeObj, null, DeliveredAction, null, null));
        Plugin.L.LogInfo($"[CAFE] café livré au poste de {to} ({delivered.Count}/2)");
    }

    internal static void Tick()
    {
        if (Time.time < nextPoll) return;
        nextPoll = Time.time + PollSeconds;
        SyncTask();
        if (!TaskActive()) return;

        var me = NetworkClient.localPlayer;
        var myLobby = LobbyManager.instance?.GetLocalPlayer()?.netIdentity;
        if (me == null) return;
        var holders = Object.FindObjectsOfType<MugHolder>();
        uint held = 0;
        foreach (var mug in Object.FindObjectsOfType<Mug>())
        {
            bool full = mug.coffeeAmount > 0.01f;
            if (ModItem.HeldBy(mug, me)) { if (full) { mine.Add(mug.netId); held = mug.netId; } continue; }
            if (!full || !mine.Contains(mug.netId) || delivered.Contains(mug.netId) || mug.usingPlayerController != null) continue;
            var holder = HolderOf(mug, holders);
            if (holder == null) continue;
            var owner = holder.lobbyPlayerOwner;
            if (owner != null && myLobby != null && owner == myLobby)   // pas son propre poste ; un bureau vide compte
            {
                if (warned.Add(mug.netId)) Plugin.L.LogInfo("[CAFE] tasse posée sur MON poste : ne compte pas");
                continue;
            }
            Deliver(mug.netId, owner == null ? "personne (bureau vide)" : owner.name);
        }
        inHand = held;
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
