using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Props.Scripts;
using Props.ServerRack;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// "Panne générale" : un slacker déclenche depuis la salle serveur l'écran virus du jeu sur tous les PC.
// Pendant la panne, les LED des baies clignotent en rouge (ServerBlink), sans couper les connexions.
// Seule la réparation en salle serveur ("Enlever tous les virus", 2x la durée de réactivation, manager uniquement) y met fin.
// L'incident n'existe que via la tâche : le mécanisme de virus aléatoire du jeu n'est pas modifié.
// TODO réseau : l'état de l'incident n'est connu que de l'hôte ; il faudra le synchroniser (message Mirror).
static class Incident
{
    internal const string HookAction = "Outage";
    internal static bool Active;   // connu de tous les clients via les signaux de ServerBlink
    internal static bool Armed;    // sabotage fait, panne pas encore déclenchée (connu de tous les clients)
    const float NearServers = 4f;
    const float ArmedSignal = -21.3f;

    // Délai entre le sabotage et la panne : celui de la coupure des serveurs du jeu (DelayedSetConnection),
    // appris dès que le jeu en lance une ; valeur par défaut d'ici là
    internal static float Delay = 10f;
    static float startAt = -1f;

    // Serveur : arme la panne, déclenchée après le délai (Tick)
    internal static void Arm(ServerController via)
    {
        if (!NetworkServer.active || Active || startAt > 0f) return;
        startAt = Time.time + Delay;
        NetSignal.Send(via, ArmedSignal);
        Plugin.L.LogInfo($"[INCIDENT] Panne armée : déclenchement dans {Delay}s");
    }

    internal static void Tick()
    {
        if (startAt < 0f || Time.time < startAt) return;
        startAt = -1f;
        if (NetworkServer.active) Start();
    }

    // Chez chaque client : état de la panne, et validation de l'étape pour le saboteur qui se tient à côté des baies
    internal static void Register()
    {
        NetSignal.On(ServerBlink.OnSignal, _ => { Active = true; Armed = false; });
        // L'étape du saboteur est validée au moment du sabotage (il est encore devant les baies), pas au déclenchement
        NetSignal.On(ArmedSignal, _ =>
        {
            Armed = true;
            var me = Mirror.NetworkClient.localPlayer;
            if (me == null || !CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), CustomTaskPatches.IncidentKey)) return;
            foreach (var s in Object.FindObjectsOfType<ServerController>())
                if (Vector3.Distance(me.transform.position, s.transform.position) < NearServers)
                {
                    Player.Tasks.TaskHook.TriggerTaskHookCommandStatic(new Player.Tasks.TaskHook(null, "Servers", null, HookAction, null, null));
                    break;
                }
        });
        NetSignal.On(ServerBlink.OffSignal, _ => Active = false);
    }

    internal static string Start()
    {
        if (!NetworkServer.active) return "Hôte uniquement";
        int n = SetAllScreens(true);
        Active = true;
        ServerBlink.Broadcast(true);
        Plugin.L.LogInfo($"[INCIDENT] Panne générale déclenchée ({n} PC)");
        return $"Panne générale : {n} PC touchés";
    }

    internal static string Clear()
    {
        if (!NetworkServer.active) return "Hôte uniquement";
        int n = SetAllScreens(false);
        Active = false;
        ServerBlink.Broadcast(false);
        Plugin.L.LogInfo($"[INCIDENT] Panne réparée ({n} PC)");
        return $"Panne réparée : {n} PC nettoyés";
    }

    static int SetAllScreens(bool on)
    {
        var all = Object.FindObjectsOfType<VirusController>();
        PopupSpam.BypassVirus = true;   // virus imposé partout : pas de tirage "popups à la place"
        try { foreach (var v in all) v.ServerSetVirus(on); }
        finally { PopupSpam.BypassVirus = false; }
        return all.Length;
    }
}

static class IncidentPatches
{
    const float FallbackRepairSeconds = 10f;

    [HarmonyPatch(typeof(ServerController), nameof(ServerController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddAlternatives(ServerController __instance)
    {
        var rack = __instance;
        var alts = __instance.alternatives;
        if (alts == null) return;

        // Les deux dernières alternatives du jeu sont "désactiver" puis "réactiver" les connexions
        const float sabotage = 1f;
        float repair = alts.Count >= 1 ? alts[alts.Count - 1].duration * 2f : 0f;
        if (repair <= 0f) repair = FallbackRepairSeconds;
        Plugin.L.LogInfo($"[INCIDENT] Durées : sabotage {sabotage}s, réparation {repair}s");

        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Déclencher la panne générale", "Trigger total outage"), InteractionPermission.All,
            NetSignal.ServerAction(rack, -21.5f, _ => Incident.Arm(rack)), false, Show(CanSabotage), sabotage, NoTimer(), NoStart(), false, true));
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Enlever tous les virus", "Remove all viruses"), InteractionPermission.All,
            NetSignal.ServerAction(rack, -21.6f, _ => Incident.Clear()), false, Show(CanRepair), repair, NoTimer(), NoStart(), false, false));
    }

    // Visible seulement pour le joueur dont la tâche active est "Panne générale", si aucune panne n'est en cours
    static bool CanSabotage() =>
        !Incident.Active && !Incident.Armed && CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), CustomTaskPatches.IncidentKey);

    // Seul le manager peut réparer
    static bool CanRepair() =>
        Incident.Active && LobbyManager.instance?.GetLocalPlayer()?.playerRole == Player.Lobby.PlayerRole.Manager;

    // Serveur : l'étape est validée chez le saboteur par le signal de début de panne (Incident.Register)
    // Délai de la coupure des serveurs du jeu : lu sur sa coroutine au premier pas
    [HarmonyPatch(typeof(ServerController._DelayedSetConnection_d__13), nameof(ServerController._DelayedSetConnection_d__13.MoveNext))]
    [HarmonyPrefix]
    static void LearnDelay(ServerController._DelayedSetConnection_d__13 __instance)
    {
        if (__instance.__1__state != 0 || __instance.delay <= 0f || __instance.delay == Incident.Delay) return;
        Incident.Delay = __instance.delay;
        Plugin.L.LogInfo($"[INCIDENT] délai de coupure du jeu : {Incident.Delay}s (repris pour la panne générale)");
    }

    static Il2CppSystem.Func<bool> Show(Func<bool> f) => DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(f);

    // Le jeu peut appeler ces callbacks sans vérifier null : on fournit des callbacks vides
    static UnityAction<float> NoTimer() => DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { }));
    static UnityAction<NetworkIdentity> NoStart() => DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { }));
}
