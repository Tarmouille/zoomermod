using System;
using GameManagement;
using GameManagement.StateMachine;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Player.Lobby;
using Player.Scripts;
using Props.Scripts;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Rôle bonus "Maître chanteur" (Blackmailer, RoleKit) : un FAINÉANT qui bâillonne un joueur pour la prochaine réunion.
// - Viser un joueur à 1 m maximum et maintenir E 2 s ("Faire chanter").
//   Pas le manager, pas deux fois de suite la même personne, une victime par réunion, pas pendant une réunion.
// - Au VRAI début de la réunion (après les 15 s pour rejoindre la salle), la victime l'apprend par un gros message
//   et ne peut plus ni parler (voix coupée par le serveur) ni écrire dans le chat, jusqu'à la fin de la réunion.
//   Aucune indication avant, et personne d'autre ne sait qu'elle est bâillonnée.
// - Gagne avec les fainéants, pas de mission.
static class MaitreChanteur
{
    const float YouSignal = -9900f, ChosenSignal = -9901f, GaggedSignal = -9902f, UngaggedSignal = -9903f,
                GagAction = -9904f, LockedSignal = -9905f, RoundResetSignal = -9906f,
                ChatBlockedSignal = -9907f, GagSelfAction = -9908f;
    static readonly Color Green = new(0.235f, 0.553f, 0.353f);   // #3C8D5A
    const string Hex = "#3C8D5A";
    const float GagDistance = 1f, GagSeconds = 2f;
    static string T(string fr, string en) => RoleKit.T(fr, en);

    internal static BonusRole Role;

    internal static void Register()
    {
        Role = new BonusRole
        {
            Id = "chanteur", Name = "Maître chanteur", SettingKey = "ZoomerMod Chanteur", Color = Green, SignalBase = -9970f,
            Pick = (players, taken, _) => RoleKit.PickOf(players, taken, PlayerRole.Slacker),
            Assign = (lp, _) => Make(lp),
            ServerReset = () => { chanteurId = pendingVictim = lastVictim = gaggedId = 0; gaggedConn = -1; },
            ServerOutcome = () => chanteurId == 0 ? null : GameManager.instance != null && GameManager.instance.GetWinner() == PlayerRole.Slacker,
            ServerHolder = () => chanteurId,
            ClientReset = () => { if (gaggedLocal) Hud.HideBig(); ShowHint(null); holdStart = -1f; mine = locked = gaggedLocal = false; chosenName = null; chosenId = lastId = 0; },
            LocalIsHolder = () => mine,
            LocalHasHelp = () => mine,
            HelpText = HelpText,
            RoleCard = RoleCard,
            ResultLine = won => won ? T("Maître chanteur : {0}, gagne avec les fainéants", "Blackmailer: {0}, wins with the slackers")
                                    : T("Maître chanteur : {0}", "Blackmailer: {0}"),
        };
        RoleKit.Add(Role);

        NetSignal.OnLocalPlayer(YouSignal, () =>
        {
            mine = true;
            RoleKit.RefreshRoleTabs();
            RoleKit.ShowAfterCard(T("Tu es le MAÎTRE CHANTEUR ! Choisis un joueur : à la prochaine réunion, il ne pourra ni parler ni écrire. (F1 : aide)",
                       "You are the BLACKMAILER! Pick a player: at the next meeting, they won't be able to speak or write. (F1: help)"), 8f);
        });
        NetSignal.OnLocalPlayer(ChosenSignal, () =>
        {
            Hud.Show(T($"{chosenName} sera bâillonné(e) à la prochaine réunion.", $"{chosenName} will be gagged at the next meeting."), 5f);
        });
        NetSignal.OnLocalPlayer(LockedSignal, () => locked = true);
        NetSignal.OnLocalPlayer(RoundResetSignal, () => { locked = false; if (chosenId != 0) lastId = chosenId; chosenId = 0; chosenName = null; });
        NetSignal.OnLocalPlayer(GaggedSignal, () =>
        {
            gaggedLocal = true;
            Hud.ShowBig(T("<color=#FF4040>TU ES BÂILLONNÉ(E)</color>\n<size=55%>Quelqu'un te fait chanter : pendant cette réunion, tu ne peux ni parler ni écrire.</size>",
                          "<color=#FF4040>YOU ARE GAGGED</color>\n<size=55%>Someone is blackmailing you: during this meeting, you can neither speak nor write.</size>"), 99999f);   // jusqu'à la fin de la réunion
        });
        NetSignal.OnLocalPlayer(ChatBlockedSignal, () => Hud.Show(T("Message non envoyé : tu es bâillonné(e).", "Message not sent: you are gagged."), 3f));
        NetSignal.OnLocalPlayer(UngaggedSignal, () =>
        {
            if (!gaggedLocal) return;
            gaggedLocal = false;
            Hud.HideBig();
            Hud.Show(T("Tu peux de nouveau parler.", "You can speak again."), 4f);
        });

        NetSignal.OnServer(GagAction, (it, sender) => Choose(sender, it?.TryCast<PlayerInteractable>()));
#if DEBUG
        // Mannequin de test : le Maître chanteur se choisit lui-même (Debug seulement)
        NetSignal.OnServer(GagSelfAction, (_, sender) =>
        {
            var me = RoleKit.LobbyOf(sender);
            if (me == null || me.netId != chanteurId || pendingVictim != 0) return;
            pendingVictim = me.netId;
            NetSignal.SendToPlayer(sender, ChosenSignal);
        });
#endif
        Meetings.Called += _ => SendToChanteur(LockedSignal);
        Meetings.Started += _ => StartGag();
        Meetings.Ended += _ => EndGag();
    }

    // ===================== Serveur (hôte) =====================
    static uint chanteurId, pendingVictim, lastVictim, gaggedId;
    static int gaggedConn = -1;   // connexion de la victime pendant la réunion (-1 : personne)

    static void Make(LobbyPlayer lp)
    {
        chanteurId = lp.netId;
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), YouSignal);
        Plugin.L.LogInfo("[CHANTEUR] Maître chanteur tiré");   // jamais de nom
    }

    // Menu F7 (hôte)
    internal static string Force(LobbyPlayer lp)
    {
        if (!NetworkServer.active || lp == null) return "Hôte uniquement";
        if (lp.playerRole != PlayerRole.Slacker) return "Le Maître chanteur doit être un fainéant (Slacker)";
        Make(lp);
        RoleKit.Announce(Role, lp);
        return null;
    }

    // Debug (hôte) : se bâillonner soi-même à la prochaine réunion (test du chat en solo)
    internal static string DebugGagSelf()
    {
        if (!NetworkServer.active) return "Hôte uniquement";
        var me = RoleKit.LocalLobby();
        if (me == null) return "Joueur local introuvable";
        pendingVictim = me.netId;
        return "Bâillonné à la prochaine réunion (lance un meeting)";
    }

    static void SendToChanteur(float signal)
    {
        if (chanteurId == 0) return;
        var id = RoleKit.PlayerIdOf(RoleKit.LobbyById(chanteurId));
        if (id != null) NetSignal.SendToPlayer(id, signal);
    }

    static void Choose(NetworkIdentity player, PlayerInteractable target)
    {
        var me = RoleKit.LobbyOf(player);
        var victim = RoleKit.LobbyOf(target?.netIdentity);
        if (me == null || me.netId != chanteurId || victim == null || victim.netId == me.netId) return;
        if (victim.playerRole == PlayerRole.Manager || victim.isFired || victim.netId == lastVictim || pendingVictim != 0) return;
        if (GameManager.instance != null && GameManager.instance.IsState(GameStates.Meeting)) return;
        pendingVictim = victim.netId;
        NetSignal.SendToPlayer(player, ChosenSignal);
        Plugin.L.LogInfo("[CHANTEUR] victime choisie pour la prochaine réunion");   // jamais de nom
    }

    static void StartGag()
    {
        if (pendingVictim == 0) return;
        var lp = RoleKit.LobbyById(pendingVictim);
        if (chanteurId != 0) lastVictim = pendingVictim;
        pendingVictim = 0;
        if (lp == null || lp.isFired || lp.playerRole == PlayerRole.Manager) return;
        gaggedId = lp.netId;
        gaggedConn = lp.connectionToClient?.connectionId ?? -1;
        var id = RoleKit.PlayerIdOf(lp);
        if (id != null) NetSignal.SendToPlayer(id, GaggedSignal);
        Plugin.L.LogInfo($"[CHANTEUR] bâillon posé (connexion {gaggedConn})");
    }

    static void EndGag()
    {
        if (gaggedId != 0)
        {
            var id = RoleKit.PlayerIdOf(RoleKit.LobbyById(gaggedId));
            if (id != null) NetSignal.SendToPlayer(id, UngaggedSignal);
            Plugin.L.LogInfo("[CHANTEUR] bâillon retiré");
        }
        if (voiceBlocked > 0) Plugin.L.LogInfo($"[CHANTEUR] voix bloquée : {voiceBlocked} paquet(s) au total");
        voiceBlocked = 0;
        gaggedId = 0;
        gaggedConn = -1;
        SendToChanteur(RoundResetSignal);
    }

    static bool IsGagged(NetworkConnectionToClient conn) =>
        gaggedConn >= 0 && conn != null && conn.connectionId == gaggedConn && NetworkServer.active;

    // Chat : message de la victime jeté par le serveur (personne ne le reçoit)
    [HarmonyPatch(typeof(ChatManager), nameof(ChatManager.InvokeUserCode_CmdSendChatMessage__LobbyPlayer__String__NetworkConnectionToClient))]
    [HarmonyPrefix]
    static bool BlockChat(NetworkConnectionToClient senderConnection)
    {
        Plugin.L.LogInfo($"[CHANTEUR] chat reçu par le serveur, connexion {(senderConnection == null ? "aucune" : senderConnection.connectionId.ToString())}, bâillon {gaggedConn}");
        if (!IsGagged(senderConnection)) return true;
        Plugin.L.LogInfo("[CHANTEUR] message de chat bloqué");
        var id = RoleKit.PlayerIdOf(RoleKit.LobbyById(gaggedId));
        if (id != null) NetSignal.SendToPlayer(id, ChatBlockedSignal);
        return false;
    }

    // Le chat de la partie passe par LobbyManager (celui de ChatManager sert ailleurs) : les deux sont filtrés
    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.InvokeUserCode_CmdSendChatMessage__NetworkIdentity__String__String__NetworkConnectionToClient))]
    [HarmonyPrefix]
    static bool BlockLobbyChat(NetworkConnectionToClient senderConnection)
    {
        Plugin.L.LogInfo($"[CHANTEUR] chat (LobbyManager) reçu par le serveur, connexion {(senderConnection == null ? "aucune" : senderConnection.connectionId.ToString())}, bâillon {gaggedConn}");
        return BlockChat(senderConnection);
    }

    // Traces temporaires : par où passe un message de chat (envoi chez le client, réception chez tous)
    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.SendChatMessage))]
    [HarmonyPrefix]
    static void TraceSend() => Plugin.L.LogInfo("[CHANTEUR] chat : LobbyManager.SendChatMessage (envoi)");

    [HarmonyPatch(typeof(ChatTabController), nameof(ChatTabController.SendChatMessage))]
    [HarmonyPrefix]
    static void TraceSendTab() => Plugin.L.LogInfo("[CHANTEUR] chat : ChatTabController.SendChatMessage (envoi)");

    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.InvokeUserCode_RpcReceiveChatMessage__NetworkIdentity__String__String))]
    [HarmonyPrefix]
    static void TraceReceive() => Plugin.L.LogInfo("[CHANTEUR] chat : message reçu par un client (LobbyManager)");

    [HarmonyPatch(typeof(ChatManager), nameof(ChatManager.InvokeUserCode_RpcSendChatMessage__LobbyPlayer__String__String))]
    [HarmonyPrefix]
    static void TraceReceive2() => Plugin.L.LogInfo("[CHANTEUR] chat : message reçu par un client (ChatManager)");

    static int voiceBlocked;
    static bool voiceSeen0, voiceSeen1;

    static bool BlockVoice(NetworkConnectionToClient senderConnection)
    {
        if (!IsGagged(senderConnection)) return true;
        if (voiceBlocked++ % 100 == 0) Plugin.L.LogInfo($"[CHANTEUR] voix bloquée ({voiceBlocked} paquet(s))");
        return false;
    }

    // Voix : les paquets de la victime ne sont plus relayés. Les deux points d'entrée réseau de SteamVoiceChatPeer ont des
    // noms générés (commande et RPC) : on filtre les deux, seule la commande a une connexion d'envoi (RPC : null).
    [HarmonyPatch(typeof(SteamVoiceChatPeer), nameof(SteamVoiceChatPeer.Method_Protected_Static_Void_NetworkBehaviour_NetworkReader_NetworkConnectionToClient_PDM_0))]
    [HarmonyPrefix]
    static bool BlockVoice0(NetworkConnectionToClient senderConnection)
    {
        if (!voiceSeen0) { voiceSeen0 = true; Plugin.L.LogInfo($"[CHANTEUR] voix : point d'entrée 0 vu (connexion {(senderConnection == null ? "aucune" : senderConnection.connectionId.ToString())})"); }
        return BlockVoice(senderConnection);
    }

    [HarmonyPatch(typeof(SteamVoiceChatPeer), nameof(SteamVoiceChatPeer.Method_Protected_Static_Void_NetworkBehaviour_NetworkReader_NetworkConnectionToClient_PDM_1))]
    [HarmonyPrefix]
    static bool BlockVoice1(NetworkConnectionToClient senderConnection)
    {
        if (!voiceSeen1) { voiceSeen1 = true; Plugin.L.LogInfo($"[CHANTEUR] voix : point d'entrée 1 vu (connexion {(senderConnection == null ? "aucune" : senderConnection.connectionId.ToString())})"); }
        return BlockVoice(senderConnection);
    }

    // Voix coupée à la source, chez la victime : le filtrage serveur ci-dessus ne voyait passer aucun paquet (essai du
    // 2026-11-08 : la victime restait audible). Steam ne rend plus de voix au jeu, et l'envoi est bloqué au cas où.
    static bool voiceMutedLogged;

    static bool MutedHere()
    {
        if (!gaggedLocal) { voiceMutedLogged = false; return false; }
        if (!voiceMutedLogged) { voiceMutedLogged = true; Plugin.L.LogInfo("[CHANTEUR] micro coupé chez la victime"); }
        return true;
    }

    [HarmonyPatch(typeof(Steamworks.SteamUser), nameof(Steamworks.SteamUser.GetAvailableVoice))]
    [HarmonyPrefix]
    static bool MuteAvailable(ref uint pcbCompressed, ref Steamworks.EVoiceResult __result)
    {
        if (!MutedHere()) return true;
        pcbCompressed = 0;
        __result = Steamworks.EVoiceResult.k_EVoiceResultNoData;
        return false;
    }

    [HarmonyPatch(typeof(Steamworks.SteamUser), nameof(Steamworks.SteamUser.GetVoice))]
    [HarmonyPrefix]
    static bool MuteVoice(ref uint nBytesWritten, ref Steamworks.EVoiceResult __result)
    {
        if (!MutedHere()) return true;
        nBytesWritten = 0;
        __result = Steamworks.EVoiceResult.k_EVoiceResultNoData;
        return false;
    }

    [HarmonyPatch(typeof(SteamVoiceChatPeer), nameof(SteamVoiceChatPeer.CmdSendData))]
    [HarmonyPrefix]
    static bool MuteSend() => !MutedHere();

    // ===================== Touche E : viser un joueur à 1 m et maintenir 2 s (chez le Maître chanteur) =====================
    static bool mine, locked, gaggedLocal;
    static string chosenName;
    static uint chosenId, lastId;
    static float holdStart = -1f;
    static IntPtr holdTarget;
    static TMPro.TextMeshProUGUI hint;

    // Appelé par DebugMenu.Update chez tous
    internal static void Tick()
    {
        var target = mine && !locked && chosenId == 0 ? RoleKey.AimedPlayer(GagDistance) : null;
        if (target != null && !CanChoose(target)) target = null;
        bool onDummy = target == null && mine && !locked && chosenId == 0 && RoleKey.AimedDummy(GagDistance);
        float progress = 0f;
        if ((target != null || onDummy) && RoleKey.Held)
        {
            var key = target != null ? target.Pointer : new IntPtr(-1);
            if (holdStart < 0f || holdTarget != key) { holdStart = Time.time; holdTarget = key; }
            progress = Mathf.Clamp01((Time.time - holdStart) / GagSeconds);
            if (progress >= 1f) { holdStart = -1f; progress = 0f; if (target != null) Request(target); else RequestSelf(); }
        }
        else holdStart = -1f;
        if (mine) RoleKey.Bar(progress, Screen.height * 0.09f - 8f);
        if (onDummy) { ShowHint(T("Maintiens E pendant 2s pour faire chanter le mannequin (toi-même, test)", "Hold E for 2s to blackmail the dummy (yourself, test)")); return; }
        ShowHint(target == null ? null
            : T($"Maintiens E pendant 2s pour faire chanter {target.lobbyPlayer.username}", $"Hold E for 2s to blackmail {target.lobbyPlayer.username}"));
    }

    static bool CanChoose(Player.PlayerController target)
    {
        var lp = target?.lobbyPlayer;
        return lp != null && !lp.isFired && lp.playerRole != PlayerRole.Manager && lp.netId != lastId;
    }

    static void Request(Player.PlayerController target)
    {
        var via = RoleKey.InteractableOf(target);
        if (via == null) return;
        chosenId = target.lobbyPlayer.netId;
        chosenName = target.lobbyPlayer.username;
        NetSignal.SendToServer(via, GagAction);   // le serveur vérifie et confirme (ChosenSignal)
    }

    // Mannequin de test (Debug) : la victime est le joueur lui-même
    static void RequestSelf()
    {
        var me = RoleKit.LocalLobby();
        var via = NetSignal.PlayerInteractableOf(NetworkClient.localPlayer);
        if (me == null || via == null) return;
        chosenId = me.netId;
        chosenName = T("toi-même (mannequin)", "yourself (dummy)");
        NetSignal.SendToServer(via, GagSelfAction);
    }

    // Invite en bas de l'écran, dans la police du jeu, au-dessus de la barre
    static void ShowHint(string text)
    {
        if (text == null) { if (hint != null) hint.gameObject.SetActive(false); return; }
        if (hint == null)
        {
            hint = GameUi.NewText("DaleMod_Chanteur");
            var rt = hint.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, Screen.height * 0.09f);
            rt.sizeDelta = new Vector2(Screen.width * 0.6f, 80f);
            hint.alignment = TMPro.TextAlignmentOptions.Bottom;
        }
        GameUi.Style(hint);
        hint.fontSize = Mathf.Max(18f, Screen.height / 42f);
        hint.text = text;
        hint.color = new Color(1f, 1f, 1f, 0.9f);
        hint.gameObject.SetActive(true);
    }

    // ===================== Carte et aide =====================
    static void RoleCard(ShowRoleTab t)
    {
        if (t.roleText != null) t.roleText.text = T("Vous êtes le Maître chanteur !", "You are the Blackmailer!");
        if (t.roleDescriptionText != null)
            t.roleDescriptionText.text = T(
                "Fainéant. Fais chanter un joueur : à la prochaine réunion, il ne pourra ni parler ni écrire. Maintiens F1 pour l'aide.",
                "Slacker. Blackmail a player: at the next meeting, they can neither speak nor write. Hold F1 for help.");
    }

    static string HelpText()
    {
        string title = $"<size=170%><color={Hex}>{T("MAÎTRE CHANTEUR", "BLACKMAILER")}</color></size>\n\n";
        string state = locked ? T("Réunion en cours : choix impossible.", "Meeting in progress: no choice possible.")
                     : chosenName != null ? T($"Victime de la prochaine réunion : <b>{chosenName}</b>.", $"Next meeting's victim: <b>{chosenName}</b>.")
                     : T("Aucune victime choisie pour la prochaine réunion.", "No victim picked for the next meeting.");
        return title + state + "\n\n" + T(
            "Tu es fainéant et tu gagnes avec les fainéants.\n" +
            "Vise un joueur à 1 m maximum et maintiens E pendant 2s pour le faire chanter. Au vrai début de la prochaine réunion, il ne pourra ni parler ni écrire jusqu'à la fin de la réunion. Il ne l'apprend qu'à ce moment-là, et personne d'autre ne le sait.\n" +
            "Une victime par réunion, jamais le manager, jamais deux fois de suite la même personne.",
            "You're a slacker and win with the slackers.\n" +
            "Aim at a player within 1 m and hold E for 2s to blackmail them. When the next meeting really starts, they can neither speak nor write until it ends. They only find out then, and nobody else knows.\n" +
            "One victim per meeting, never the manager, never the same person twice in a row.");
    }
}
