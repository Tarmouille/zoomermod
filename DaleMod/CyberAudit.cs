using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Player.Lobby;
using Player.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Audit Cybersécurité" (bleue, Specialist ; voir TaskPools) : sur le PC d'un joueur, ouvrir l'app Antivirus du
// jeu, le désactiver puis le réactiver. Chaque réactivation (après une désactivation par moi, sur ce PC) valide une
// étape ; 3 PC différents, sauf s'il y a moins de 3 joueurs (le même PC peut alors resservir).
// 1 chance sur 10 qu'un contrôle validé ouvre 10 fenêtres de pub sur ce PC (PopupSpam), à fermer avec leur croix.
// Détection : RpcSetFirewall(joueur, état), reçu par tous les clients quand quelqu'un bascule un antivirus.
static class CyberAudit
{
    internal const string Key = "dalemod.auditCyber";
    internal const string AuditedAction = "Audited", AntivirusObj = "Antivirus";
    const int Needed = 3;
    const float SpamChance = 0.1f;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    static IntPtr taskPtr;
    static readonly HashSet<IntPtr> disabledByMe = new(), audited = new();
    static int done;

    static void SyncTask()
    {
        var p = CustomTaskPatches.ActiveTask()?.Pointer ?? IntPtr.Zero;
        if (p == taskPtr) return;
        taskPtr = p;
        disabledByMe.Clear(); audited.Clear(); done = 0;
    }

    // Joueur et nouvel état lus dans le message sans le consommer, puis traités
    [HarmonyPatch(typeof(VirusController), nameof(VirusController.InvokeUserCode_RpcSetFirewall__NetworkIdentity__Boolean))]
    [HarmonyPrefix]
    static void OnFirewall(NetworkBehaviour obj, NetworkReader reader)
    {
        var vc = obj?.TryCast<VirusController>();
        if (vc == null || reader == null) return;
        NetworkIdentity player;
        bool on;
        int pos = reader.Position;
        try { player = NetworkReaderExtensions.ReadNetworkIdentity(reader); on = NetworkReaderExtensions.ReadBool(reader); }
        catch { return; }
        finally { reader.Position = pos; }

        SyncTask();
        if (player == null || player != NetworkClient.localPlayer || !TaskActive()) return;
        var owner = vc.computerController?.user?.username;
        Plugin.L.LogInfo($"[CYBER] antivirus du PC de {owner ?? "personne"} {(on ? "réactivé" : "désactivé")}");
        if (owner == null) return;   // PC d'un joueur seulement
        if (!on) { disabledByMe.Add(vc.Pointer); return; }
        if (!disabledByMe.Remove(vc.Pointer) || done >= Needed) return;

        bool fewPlayers = Object.FindObjectsOfType<LobbyPlayer>().Length < Needed;
        if (!audited.Add(vc.Pointer) && !fewPlayers)
        {
            vc.computerController?.ShowPopup(CustomTaskPatches.Text("Ce PC a déjà été audité. Passe à un autre poste.",
                                                                    "This computer was already audited. Move on to another one."), "OK");
            return;
        }
        done++;
        TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, AntivirusObj, null, AuditedAction, null, null));
        Plugin.L.LogInfo($"[CYBER] contrôle validé ({done}/{Needed})");
        if (UnityEngine.Random.value < SpamChance) PopupSpam.Request(vc.computerController);   // popups sur CE PC, chez tous
    }
}
