using System;
using System.Collections.Generic;
using Computer.Executables.Email;
using Computer.Scripts.System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Player.Tasks;
using Props.FireEx;
using Props.Scripts;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Formation sécurité" (bleue, Specialist ; voir TaskPools) :
// - vérifier 3 extincteurs DIFFÉRENTS, en main ("Vérifier l'extincteur", 3 s) ; 1 chance sur 4 que l'extincteur parte
//   tout seul : jet pour tous, et le porteur + les joueurs à moins de 2 m sont aspergés (comme "Rafraîchissement d'équipe") ;
// - puis envoyer au manager le mail de compte rendu, prérempli dès que les 3 vérifications sont faites.
// Les étapes du jeu se valident dans n'importe quel ordre : le mail n'est prérempli qu'après les 3 vérifications.
static class SafetyCheck
{
    internal const string Key = "dalemod.formationSecurite";
    internal const string CheckedAction = "Checked", ExObj = "Fire Extinguisher", ReportedAction = "Reported", ManagerObj = "Manager";
    const int Needed = 3;
    const float CheckSeconds = 3f, MisfireChance = 0.25f, MisfireRadius = 2f, CheckAction = -82.5f;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);
    static TaskHook Hook(string action, string obj) => new(null, obj, null, action, null, null);

    // Chez le joueur : extincteurs déjà vérifiés pour la tâche en cours (remis à zéro à chaque nouvelle tâche)
    static IntPtr taskPtr;
    static readonly HashSet<uint> checkedIds = new();
    static bool reported;

    static void SyncTask()
    {
        var t = CustomTaskPatches.ActiveTask();
        var p = t?.Pointer ?? IntPtr.Zero;
        if (p == taskPtr) return;
        taskPtr = p;
        checkedIds.Clear();
        reported = false;
    }

    // --- Action sur l'extincteur tenu en main ---
    [HarmonyPatch(typeof(Collectible), nameof(Collectible.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddCheck(Collectible __instance)
    {
        var ex = __instance.TryCast<FireExController>();
        if (ex == null || ex.alternatives == null) return;
        ex.alternatives.Add(new InteractionAlternative(CustomTaskPatches.Text("Vérifier l'extincteur", "Inspect the extinguisher"),
            InteractionPermission.All,
            NetSignal.ServerAction(ex, CheckAction, player => ServerCheck(ex, player), () => LocalChecked(ex)),
            true,   // pendant l'utilisation : l'extincteur est en main
            DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(new Func<bool>(() =>
            {
                SyncTask();
                return TaskActive() && checkedIds.Count < Needed && !checkedIds.Contains(ex.netId)
                       && FireExPrank.IsMine(ex, NetworkClient.localPlayer);   // en main (détection de FireExPrank)
            })),
            CheckSeconds,
            DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { })),
            DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { })),
            false, false));
    }

    // Chez le joueur qui vérifie : étape validée, extincteur noté
    static void LocalChecked(FireExController ex)
    {
        SyncTask();
        if (!TaskActive() || !checkedIds.Add(ex.netId)) return;
        TaskHook.TriggerTaskHookCommandStatic(Hook(CheckedAction, ExObj));
        Plugin.L.LogInfo($"[SECU] extincteur vérifié ({checkedIds.Count}/{Needed})");
    }

    // Serveur : 1 chance sur 4 que l'extincteur parte tout seul
    static void ServerCheck(FireExController ex, NetworkIdentity player)
    {
        if (UnityEngine.Random.value < MisfireChance) FireExPrank.ServerMisfire(ex, player, MisfireRadius);
    }

    // --- Mail au manager, prérempli après les 3 vérifications (comme BossMail / ChainMail) ---
    static readonly HashSet<IntPtr> seen = new(), ours = new();

    static string Subject() => CustomTaskPatches.Text("Contrôle des extincteurs : RAS", "Fire extinguisher inspection: all clear");
    static string Body() => CustomTaskPatches.Text(
        "Bonjour,\n\nJe vous confirme que le contrôle annuel des extincteurs a été effectué ce jour.\n" +
        "3 extincteurs vérifiés : pression correcte, goupilles en place, dates de validité conformes.\n\n" +
        "Aucune anomalie à signaler (ou presque).\n\nCordialement",
        "Hello,\n\nThis is to confirm that the annual fire extinguisher inspection was carried out today.\n" +
        "3 extinguishers inspected: pressure OK, safety pins in place, expiry dates valid.\n\n" +
        "Nothing to report (or almost).\n\nBest regards");
    static string To() => BossMail.ManagerAddress() ?? "direction@DaleDawson.com";   // sans manager : la direction

    internal static void Tick()
    {
        SyncTask();
        if (!TaskActive() || reported || checkedIds.Count < Needed) return;
        foreach (var w in Object.FindObjectsOfType<WriteEmail>())
        {
            if (!seen.Add(w.Pointer)) continue;
            var pc = w.GetComponentInParent<ComputerController>(true);
            if (pc == null || !pc.isLocalPlayerUsingComputer) continue;
            ours.Add(w.Pointer);
            if (w.sender != null) w.sender.text = pc.emailAddress;
            if (w.receiver != null) w.receiver.text = To();
            if (w.subject != null) w.subject.text = Subject();
            if (w.text != null) w.text.text = Body();
            Plugin.L.LogInfo("[SECU] mail de compte rendu prérempli");
        }
    }

    [HarmonyPatch(typeof(WriteEmail), nameof(WriteEmail.SendMail))]
    [HarmonyPrefix]
    static bool OnSendMail(WriteEmail __instance)
    {
        if (!ours.Remove(__instance.Pointer)) return true;
        var pc = __instance.GetComponentInParent<ComputerController>(true);
        if (pc == null || !TaskActive()) return true;
        EmailManager.instance.SendEmail(pc.emailAddress, To(), Subject(), Body());
        reported = true;
        TaskHook.TriggerTaskHookCommandStatic(Hook(ReportedAction, ManagerObj));
        pc.ShowPopup(CustomTaskPatches.Text("Compte rendu envoyé. Formation validée !", "Report sent. Training complete!"), "OK");
        __instance.applicationWindow?.Close();
        Plugin.L.LogInfo("[SECU] compte rendu envoyé au manager");
        return false;
    }
}
