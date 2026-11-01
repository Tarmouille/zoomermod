using System;
using Il2CppInterop.Runtime;
using HarmonyLib;
using Mirror;
using Player.Tasks;
using Props.Scripts;
using Props.ServerRack;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Sauvegarde des serveurs" (bleue, Specialist ; voir TaskPools) : "Lancer la sauvegarde" sur une baie (5 s à côté,
// vaut pour toute la salle). N'importe qui peut lancer une sauvegarde. Pendant la sauvegarde, les LED de toutes les
// baies clignotent en bleu, chez tous. Si les serveurs sont en panne (panne du jeu, tâche DisableServers…) au moment de
// finir, la sauvegarde échoue : il faut d'abord réparer la baie, puis recommencer.
// Bonus : 1 chance sur 4 qu'une sauvegarde réussie fasse tomber les serveurs, comme une panne aléatoire du jeu.
// Avec la tâche (expert) : après le même délai que la Panne générale, et l'étape est validée.
// Sans la tâche (un Slacker qui fait semblant) : la panne est immédiate.
static class ServerBackup
{
    internal const string Key = "dalemod.sauvegardeServeurs";
    internal const string BackedUpAction = "Backed up", ServersObj = "Servers";
    const float BackupSeconds = 5f, CrashChance = 0.25f;
    const float BlueUp = -84.1f, BlueOn = -84.2f, BlueOff = -84.3f, DoneSignal = -84.4f, FailedSignal = -84.5f,
                 ExpertAction = -84.6f, AmateurAction = -84.7f;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    internal static void Register()
    {
        NetSignal.OnServer(BlueUp, (it, _) => NetSignal.Send(it, BlueOn));
        NetSignal.On(BlueOn, _ => ServerBlink.BlinkBlue(BackupSeconds + 1f));
        NetSignal.On(BlueOff, _ => ServerBlink.StopBlue());
        NetSignal.OnLocalPlayer(DoneSignal, () =>
        {
            Hud.Show(CustomTaskPatches.Text("Sauvegarde terminée.", "Backup complete."), 3f);
            if (!TaskActive()) return;
            TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, ServersObj, null, BackedUpAction, null, null));
            Plugin.L.LogInfo("[BACKUP] sauvegarde réussie");
        });
        NetSignal.OnLocalPlayer(FailedSignal, () =>
        {
            Hud.Show(CustomTaskPatches.Text("Erreur serveur : sauvegarde impossible. Répare la baie puis recommence.",
                                            "Server error: backup failed. Repair the rack, then try again."), 5f);
            Plugin.L.LogInfo("[BACKUP] sauvegarde échouée (serveurs en panne)");
        });
    }

    [HarmonyPatch(typeof(ServerController), nameof(ServerController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddBackup(ServerController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var rack = __instance;
        // Le joueur sait s'il a la tâche : il envoie l'action "expert" ou "amateur" au serveur
        var expert = NetSignal.ServerAction(rack, ExpertAction, player => ServerFinish(player, rack, true));
        var amateur = NetSignal.ServerAction(rack, AmateurAction, player => ServerFinish(player, rack, false));
        var action = DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity, NetworkConnectionToClient>>(
            new Action<NetworkIdentity, NetworkConnectionToClient>((player, conn) =>
            {
                if (TaskActive()) expert.Invoke(player, conn); else amateur.Invoke(player, conn);
            }));
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Lancer la sauvegarde", "Start the backup"), InteractionPermission.All,
            action, false,
            DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(new Func<bool>(() => ServerController.connectionsEnabled)),   // tout le monde
            BackupSeconds,
            DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { })),
            // Début de la sauvegarde (chez le joueur) : LED bleues chez tous, via le serveur
            DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ =>
            {
                if (NetworkServer.active) NetSignal.Send(rack, BlueOn);
                else NetSignal.SendToServer(rack, BlueUp);
            })),
            false, false));
    }

    // Serveur : fin des 5 s
    static float crashAt = -1f;
    static void ServerFinish(NetworkIdentity player, ServerController rack, bool expert)
    {
        NetSignal.Send(rack, BlueOff);
        if (!ServerController.connectionsEnabled) { NetSignal.SendToPlayer(player, FailedSignal); return; }
        NetSignal.SendToPlayer(player, DoneSignal);
        if (UnityEngine.Random.value >= CrashChance || crashAt >= 0f) return;
        if (expert)
        {
            crashAt = Time.time + Incident.Delay;   // expert : même délai que la Panne générale (celui du jeu)
            Plugin.L.LogInfo($"[BACKUP] sauvegarde experte : les serveurs vont tomber dans {Incident.Delay}s");
        }
        else
        {
            crashAt = Time.time;                    // amateur (sans la tâche) : ça pète tout de suite
            Plugin.L.LogInfo("[BACKUP] sauvegarde amateur : les serveurs tombent immédiatement");
        }
    }

    // Serveur : la panne "aléatoire" après le délai (aucun joueur désigné, comme une panne du jeu)
    internal static void Tick()
    {
        if (crashAt < 0f || Time.time < crashAt) return;
        crashAt = -1f;
        if (!NetworkServer.active || !ServerController.connectionsEnabled) return;
        foreach (var r in Object.FindObjectsOfType<ServerController>()) r.RpcSetConnectionEnabled(null, false);
        Plugin.L.LogInfo("[BACKUP] panne des serveurs après la sauvegarde");
    }
}
