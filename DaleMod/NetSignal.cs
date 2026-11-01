using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Props.Scripts;

namespace DaleMod;

// Signal serveur -> tous les clients sans message Mirror custom : on envoie l'RPC existant
// Interactable.RpcSetInteractionCoolDown avec une valeur impossible en jeu (négative), interceptée chez chaque client,
// et on remet le délai d'origine. UserCode_RpcSetInteractionCoolDown est inliné par IL2CPP : on encadre InvokeUserCode.
// Client -> serveur : SendToServer / OnServer (voir plus bas).
static class NetSignal
{
    static readonly Dictionary<float, Action<Interactable>> handlers = new();

    // Plusieurs abonnés possibles pour un même signal
    internal static void On(float signal, Action<Interactable> handler) =>
        handlers[signal] = handlers.TryGetValue(signal, out var h) ? h + handler : handler;

    // Côté serveur : diffusé à tous les clients (hôte compris), via l'objet interactif donné
    internal static void Send(Interactable via, float signal)
    {
        if (NetworkServer.active && via != null) via.RpcSetInteractionCoolDown(signal);
    }

    // --- Sens inverse, client -> serveur : la commande existante CmdSetInteractionTimeCounter avec une valeur impossible.
    // Sur le serveur, on lit ses arguments sans consommer le message ; si c'est un signal, la commande du jeu est ignorée.
    static readonly Dictionary<float, Action<Interactable, NetworkIdentity>> serverHandlers = new();

    internal static void OnServer(float signal, Action<Interactable, NetworkIdentity> handler) =>
        serverHandlers[signal] = serverHandlers.TryGetValue(signal, out var h) ? h + handler : handler;

    internal static void SendToServer(Interactable via, float signal)
    {
        if (NetworkClient.active && via != null && NetworkClient.localPlayer != null)
            via.CmdSetInteractionTimeCounter(NetworkClient.localPlayer, signal, false);
    }

    // --- Signal adressé à un joueur précis (serveur -> son client), via son PlayerInteractable ---
    internal static void SendToPlayer(NetworkIdentity player, float signal)
    {
        var pi = PlayerInteractableOf(player);
        if (pi != null) Send(pi, signal);
        else Plugin.L.LogWarning($"[SIGNAL] joueur introuvable pour le signal {signal}");
    }

    internal static void OnLocalPlayer(float signal, Action handler) =>
        On(signal, it => { if (it.netIdentity != null && it.netIdentity == NetworkClient.localPlayer) handler(); });

    internal static Player.Scripts.PlayerInteractable PlayerInteractableOf(NetworkIdentity player)
    {
        if (player == null) return null;
        foreach (var p in UnityEngine.Object.FindObjectsOfType<Player.Scripts.PlayerInteractable>())
            if (p.netIdentity == player) return p;
        return null;
    }

    // --- Actions d'interaction custom exécutées sur le serveur ---
    // Le jeu exécute l'action d'une alternative chez le joueur qui interagit (ses propres actions envoient ensuite une
    // commande au serveur). Les nôtres doivent tourner sur le serveur : chez l'hôte on l'appelle directement, chez un
    // client on l'envoie au serveur, qui retrouve l'action de son propre exemplaire de l'objet (même alternative).
    static readonly Dictionary<(IntPtr, float), Action<NetworkIdentity>> serverActions = new();
    static readonly HashSet<float> serverActionSignals = new();

    internal static UnityEngine.Events.UnityAction<NetworkIdentity, NetworkConnectionToClient> ServerAction(
        Interactable via, float signal, Action<NetworkIdentity> run, Action local = null)   // local : chez le joueur qui interagit
    {
        serverActions[(via.Pointer, signal)] = run;
        if (serverActionSignals.Add(signal))
            OnServer(signal, (it, player) =>
            {
                if (serverActions.TryGetValue((it.Pointer, signal), out var r)) r(player);
                else Plugin.L.LogWarning($"[SIGNAL] action {signal} inconnue sur {it.name}");
            });
        return Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<NetworkIdentity, NetworkConnectionToClient>>(
            new Action<NetworkIdentity, NetworkConnectionToClient>((player, _) =>
            {
                local?.Invoke();
                if (NetworkServer.active) run(player);
                else SendToServer(via, signal);
            }));
    }

    [HarmonyPatch(typeof(Interactable), nameof(Interactable.InvokeUserCode_CmdSetInteractionTimeCounter__NetworkIdentity__Single__Boolean__NetworkConnectionToClient))]
    [HarmonyPrefix]
    static bool BeforeCmd(NetworkBehaviour obj, NetworkReader reader, NetworkConnectionToClient senderConnection)
    {
        if (serverHandlers.Count == 0 || reader == null) return true;
        int pos = reader.Position;
        float value;
        try { NetworkReaderExtensions.ReadNetworkIdentity(reader); value = NetworkReaderExtensions.ReadFloat(reader); }
        catch { return true; }
        finally { reader.Position = pos; }
        if (!serverHandlers.TryGetValue(value, out var handler)) return true;
        var it = obj?.TryCast<Interactable>();
        if (it != null)
            try { handler(it, senderConnection?.identity); }
            catch (Exception e) { Plugin.L.LogError($"[SIGNAL] {e}"); }
        return false;
    }

    [HarmonyPatch(typeof(Interactable), nameof(Interactable.InvokeUserCode_RpcSetInteractionCoolDown__Single))]
    [HarmonyPrefix]
    static void Before(NetworkBehaviour obj, out float __state) =>
        __state = obj?.TryCast<Interactable>()?.interactionCoolDown ?? 0f;

    [HarmonyPatch(typeof(Interactable), nameof(Interactable.InvokeUserCode_RpcSetInteractionCoolDown__Single))]
    [HarmonyPostfix]
    static void After(NetworkBehaviour obj, float __state)
    {
        var it = obj?.TryCast<Interactable>();
        if (it == null || !handlers.TryGetValue(it.interactionCoolDown, out var handler)) return;
        it.interactionCoolDown = __state;
        try { handler(it); }
        catch (Exception e) { Plugin.L.LogError($"[SIGNAL] {e}"); }
    }
}
