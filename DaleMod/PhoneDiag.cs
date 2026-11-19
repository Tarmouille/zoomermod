using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Player.Lobby;
using Props.WorkStation.Phone;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Diagnostic des téléphones (le mod n'y touche pas) : appel impossible vers un joueur pas viré (Kim, 2026-11-16).
// Chez l'hôte, à chaque appel : l'annuaire du jeu (PhoneManager.phoneNumbers) et l'état de chaque téléphone ; chez
// l'appelant, l'échec signalé par le jeu.
static class PhoneDiag
{
    [HarmonyPatch(typeof(PhoneController), nameof(PhoneController.InvokeUserCode_CmdCall__NetworkIdentity__String__NetworkConnectionToClient))]
    [HarmonyPostfix]
    static void AfterCall(NetworkBehaviour obj)
    {
        try
        {
            var caller = obj?.TryCast<PhoneController>();
            Plugin.L.LogInfo($"[PHONE] appel depuis {Describe(caller)}");
            var book = new List<string>();
            var numbers = PhoneManager.instance?.phoneNumbers;
            if (numbers != null)
                foreach (var kv in numbers) book.Add($"{kv.Key} = {kv.Value?.username ?? "(personne)"}{(kv.Value != null && kv.Value.isFired ? " (viré)" : "")}");
            Plugin.L.LogInfo($"[PHONE] annuaire : {string.Join(" ; ", book)}");
            foreach (var p in Object.FindObjectsOfType<PhoneController>())
                if (caller == null || p.Pointer != caller.Pointer) Plugin.L.LogInfo($"[PHONE]   {Describe(p)}");
        }
        catch (Exception e) { Plugin.L.LogWarning("[PHONE] diag : " + e.Message); }
    }

    [HarmonyPatch(typeof(PhoneController), nameof(PhoneController.InvokeUserCode_RpcOnCallFailed))]
    [HarmonyPostfix]
    static void AfterFailed(NetworkBehaviour obj) =>
        Plugin.L.LogInfo($"[PHONE] appel échoué : {Describe(obj?.TryCast<PhoneController>())}");

    [HarmonyPatch(typeof(PhoneController), nameof(PhoneController.InvokeUserCode_RpcOnCallStarted))]
    [HarmonyPostfix]
    static void AfterStarted(NetworkBehaviour obj) =>
        Plugin.L.LogInfo($"[PHONE] appel décroché : {Describe(obj?.TryCast<PhoneController>())}");

    static string Describe(PhoneController p)
    {
        if (p == null) return "?";
        string owner = "?";
        try { owner = p.workStationController?.NetworkownerLobbyPlayer?.GetComponent<LobbyPlayer>()?.username ?? "(bureau vide)"; } catch { }
        return $"n° '{p.phoneNumber}' ({owner}) : actif={p.isActiveAndEnabled}, en main={p.isUsingTelephone}, compose={p.isDialing} '{p.callingNumber}', " +
               $"sonne='{p.receivingCall}', en ligne={p.isCallActive}";
    }
}
