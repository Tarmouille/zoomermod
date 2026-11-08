using System;
using System.Collections.Generic;
using GameManagement;
using GameManagement.StateMachine;
using Mirror;
using Player.Lobby;
using UnityEngine;

namespace DaleMod;

// Rôle bonus "Cost Killer" (RoleKit) : un SPÉCIALISTE qui peut virer lui-même un joueur, une seule fois par partie.
// - Viser un joueur à 1 m maximum et maintenir E 5 s, hors réunion, à partir de la fin de la première réunion.
//   Jamais le manager. Le renvoi passe par le circuit du jeu : même écran que depuis le tableau blanc ("était l'un des
//   meilleurs…"), Janitor ou spectateur selon le réglage, et il compte comme un licenciement.
// - Fainéant viré : mission réussie, il continue en simple spécialiste.
// - Spécialiste viré : erreur, il est viré à son tour juste après (son écran suit celui de sa victime). Sa démission ne
//   compte pas : la limite de licenciements est relevée de 1 AVANT son renvoi, pour que le jeu ne voie jamais la limite
//   atteinte entre les deux.
// - Jester viré : rôle neutre, pas de sanction (le Jester ne gagne pas : il n'a pas été viré par le manager).
// - Pistonné (1er renvoi) : protégé, le tir est perdu, pas de sanction.
static class CostKiller
{
    const float YouSignal = -9500f, ReadySignal = -9501f, LockedSignal = -9502f, HitSignal = -9503f, MissSignal = -9504f,
                SavedSignal = -9505f, NeutralSignal = -9506f, FireAction = -9507f, FireSelfAction = -9508f, ResignedSignal = -9509f;
    static readonly Color Teal = new(0.122f, 0.639f, 0.710f);   // #1FA3B5
    const string Hex = "#1FA3B5";
    const float FireDistance = 1f, FireSeconds = 5f;
    static string T(string fr, string en) => RoleKit.T(fr, en);

    internal static BonusRole Role;

    // Renvoi en cours lancé par le Cost Killer (les patchs de ServerFirePlayer du Jester et du Pistonné le lisent)
    internal static bool FiringNow { get; private set; }
    // Sa propre démission (ne compte pas : pas de tour du Jester consommé)
    internal static bool Resigning { get; private set; }

    internal static void Register()
    {
        Role = new BonusRole
        {
            Id = "costkiller", Name = "Cost Killer", SettingKey = "ZoomerMod CostKiller", Color = Teal, SignalBase = -9570f,
            Pick = (players, taken, _) => PickOf(players, taken),
            Assign = (lp, _) => Make(lp),
            ServerTick = ServerTick,
            ServerReset = () => { ckId = 0; shot = Shot.None; meetingsDone = 0; inMeeting = false; resignAt = -1f; },
            ServerOutcome = () => ckId == 0 ? null : shot == Shot.Hit,
            ServerHolder = () => ckId,
            ClientReset = () => { ShowHint(null); RoleKey.Bar(0f, 0f); holdStart = -1f; mine = ready = false; state = Shot.None; victimName = null; },
            LocalIsHolder = () => mine,
            LocalHasHelp = () => mine,
            HelpText = HelpText,
            RoleCard = RoleCard,
            ResultLine = won => won ? T("Cost Killer : {0}, a viré un fainéant", "Cost Killer: {0}, fired a slacker")
                                    : T("Cost Killer : {0}", "Cost Killer: {0}"),
        };
        RoleKit.Add(Role);

        NetSignal.OnLocalPlayer(YouSignal, () =>
        {
            mine = true;
            RoleKit.RefreshRoleTabs();
            RoleKit.ShowAfterCard(T("Tu es le COST KILLER ! Après la première réunion, tu pourras virer toi-même un joueur, une seule fois. Vise juste : virer un spécialiste te coûtera ta place. (F1 : aide)",
                       "You are the COST KILLER! After the first meeting, you can fire a player yourself, once. Aim well: firing a specialist costs you your job. (F1: help)"), 9f);
        });
        NetSignal.OnLocalPlayer(ReadySignal, () =>
        {
            bool first = !ready && state == Shot.None;
            ready = true;
            if (first) Hud.Show(T("Cost Killer : tu peux maintenant virer quelqu'un (vise-le à 1 m et maintiens E 5 s).",
                                  "Cost Killer: you can now fire someone (aim at them within 1 m and hold E for 5 s)."), 6f);
        });
        NetSignal.OnLocalPlayer(LockedSignal, () => ready = false);
        NetSignal.OnLocalPlayer(HitSignal, () => Done(Shot.Hit, T($"Bien vu : {victimName} était un fainéant ! Ta mission est accomplie, tu continues en spécialiste.",
                                                                   $"Good call: {victimName} was a slacker! Mission accomplished, you carry on as a specialist.")));
        NetSignal.OnLocalPlayer(MissSignal, () => Done(Shot.Missed, T($"Erreur : {victimName} était un spécialiste… Tu vas être viré(e) à ton tour.",
                                                                      $"Mistake: {victimName} was a specialist… You're getting fired too.")));
        NetSignal.OnLocalPlayer(SavedSignal, () => Done(Shot.Saved, T("Le neveu du patron est intouchable : ton tir est perdu, mais tu gardes ta place.",
                                                                      "The boss's nephew is untouchable: your shot is wasted, but you keep your job.")));
        NetSignal.OnLocalPlayer(NeutralSignal, () => Done(Shot.Neutral, T($"{victimName} n'était ni fainéant ni spécialiste : pas de sanction. Ta mission s'arrête là.",
                                                                          $"{victimName} was neither slacker nor specialist: no penalty. Your mission ends here.")));
        NetSignal.OnLocalPlayer(ResignedSignal, () => { state = Shot.Resigned; RoleKit.RefreshRoleTabs(); });

        NetSignal.OnServer(FireAction, (it, sender) => ServerFire(sender, RoleKit.LobbyOf(it?.TryCast<Player.Scripts.PlayerInteractable>()?.netIdentity)));
#if DEBUG
        // Mannequin de test : compte comme un spécialiste (teste l'erreur et la démission en solo, sans victime réelle)
        NetSignal.OnServer(FireSelfAction, (_, sender) => ServerFire(sender, null, dummy: true));
#endif
        Meetings.Called += _ => { inMeeting = true; SendToCk(LockedSignal); };
        Meetings.Ended += _ => { inMeeting = false; meetingsDone++; if (shot == Shot.None) SendToCk(ReadySignal); };
    }

    enum Shot { None, Hit, Missed, Saved, Neutral, Resigned }

    // ===================== Serveur (hôte) =====================
    static uint ckId;
    static Shot shot;
    static int meetingsDone;
    static bool inMeeting;
    static float resignAt = -1f;

    static bool Playable(LobbyPlayer lp) =>
        lp != null && !lp.isFired && lp.playerRole == PlayerRole.Specialist && lp.subRole != SubRole.Assistant;

    // Spécialiste sans sous-rôle de préférence, sinon RH ou analyste (jamais assistant)
    static LobbyPlayer PickOf(List<LobbyPlayer> players, HashSet<uint> taken)
    {
        var plain = new List<LobbyPlayer>();
        var sub = new List<LobbyPlayer>();
        foreach (var lp in players)
        {
            if (!Playable(lp) || taken.Contains(lp.netId)) continue;
            (lp.subRole == SubRole.None ? plain : sub).Add(lp);
        }
        var c = plain.Count > 0 ? plain : sub;
        return c.Count == 0 ? null : c[new System.Random().Next(c.Count)];
    }

    static void Make(LobbyPlayer lp)
    {
        ckId = lp.netId;
        shot = Shot.None;
        NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), YouSignal);
        if (meetingsDone > 0 && !inMeeting) NetSignal.SendToPlayer(RoleKit.PlayerIdOf(lp), ReadySignal);
        Plugin.L.LogInfo("[COSTKILLER] Cost Killer tiré");   // jamais de nom
    }

    // Menu F7 (hôte). Le test peut se faire tout de suite, sans attendre la fin d'une réunion.
    internal static string Force(LobbyPlayer lp)
    {
        if (!NetworkServer.active || lp == null) return "Hôte uniquement";
        if (!Playable(lp)) return "Le Cost Killer doit être un spécialiste (pas assistant)";
        meetingsDone = Mathf.Max(meetingsDone, 1);
        Make(lp);
        RoleKit.Announce(Role, lp);
        return null;
    }

    static void SendToCk(float signal)
    {
        var id = RoleKit.PlayerIdOf(RoleKit.LobbyById(ckId));
        if (id != null) NetSignal.SendToPlayer(id, signal);
    }

    static void ServerFire(NetworkIdentity sender, LobbyPlayer victim, bool dummy = false)
    {
        var gm = GameManager.instance;
        var me = RoleKit.LobbyOf(sender);
        if (gm == null || me == null || me.netId != ckId || shot != Shot.None) return;
        if (me.isFired || me.playerRole != PlayerRole.Specialist) return;   // viré, ou promu manager
        if (meetingsDone == 0 || inMeeting || gm.IsState(GameStates.Meeting) || !gm.IsState(GameStates.InGame)) return;
        if (!dummy && (victim == null || victim.netId == me.netId || victim.isFired || victim.playerRole == PlayerRole.Manager)) return;

        if (dummy)
        {
            shot = Shot.Missed;
            SendToCk(MissSignal);
            resignAt = Time.time + 1f;
            Plugin.L.LogInfo("[COSTKILLER] mannequin visé (compte comme un spécialiste) : démission à suivre");
            return;
        }

        bool slacker = victim.playerRole == PlayerRole.Slacker;
        bool jester = Jester.IsJester(victim.netId);
        int firedBefore = SafeFired(gm);
        FiringNow = true;
        try { gm.ServerFirePlayer(victim.netIdentity, false, true); }
        catch (Exception e) { Plugin.L.LogError("[COSTKILLER] renvoi : " + e); }
        finally { FiringNow = false; }

        if (Pistonne.BlockedFrame == Time.frameCount)
        {
            shot = Shot.Saved;
            SendToCk(SavedSignal);
            Plugin.L.LogInfo("[COSTKILLER] cible protégée (Pistonné) : tir perdu");
            return;
        }
        if (!victim.isFired) { Plugin.L.LogWarning("[COSTKILLER] la cible n'a pas été virée"); shot = Shot.None; return; }
        Plugin.L.LogInfo($"[COSTKILLER] renvoi effectué ({firedBefore} -> {SafeFired(gm)} / {gm.fireLimit} licenciements, état {(gm.IsState(GameStates.InGame) ? "en jeu" : "autre")})");
        if (slacker) { shot = Shot.Hit; SendToCk(HitSignal); Plugin.L.LogInfo("[COSTKILLER] fainéant viré : réussi"); }
        else if (jester) { shot = Shot.Neutral; SendToCk(NeutralSignal); Plugin.L.LogInfo("[COSTKILLER] Jester viré : pas de sanction"); }
        else
        {
            shot = Shot.Missed;
            SendToCk(MissSignal);
            resignAt = Time.time + CardSeconds();   // son écran suit celui de sa victime
            Plugin.L.LogInfo("[COSTKILLER] spécialiste viré : démission à suivre");
        }
    }

    // Durée de l'écran de renvoi (lue chez l'hôte), pour enchaîner le second sans couvrir le premier
    static float CardSeconds()
    {
        float s = 0f;
        try { var tab = PlayerFiredTab.instance; if (tab != null && tab.closeAfterSeconds) s = tab.closeAfterSecondsTime; } catch { }
        return Mathf.Clamp(s > 0f ? s + 1f : 6f, 3f, 12f);
    }

    static int SafeFired(GameManager gm) { try { return gm.GetNumberOfFiredEmployees(); } catch { return -1; } }

    static void ServerTick(GameManager gm)
    {
        if (resignAt < 0f || Time.time < resignAt) return;
        resignAt = -1f;
        var me = RoleKit.LobbyById(ckId);
        if (me == null || me.isFired) return;
        if (!gm.IsState(GameStates.InGame) && !gm.IsState(GameStates.Meeting)) { Plugin.L.LogInfo("[COSTKILLER] partie finie : pas de démission"); return; }
        // Ne compte pas : la limite monte AVANT le renvoi, le jeu ne la voit donc jamais atteinte par cette démission
        int limit = gm.fireLimit;
        gm.NetworkfireLimit = limit + 1;
        FiringNow = Resigning = true;
        try { gm.ServerFirePlayer(me.netIdentity, false, true); }
        catch (Exception e) { Plugin.L.LogError("[COSTKILLER] démission : " + e); }
        finally { FiringNow = Resigning = false; }
        if (!me.isFired)
        {
            gm.NetworkfireLimit = limit;   // renvoi refusé (Pistonné ne peut pas l'être : rôles exclusifs) : on remet la limite
            Plugin.L.LogWarning("[COSTKILLER] démission non appliquée, limite remise");
            return;
        }
        shot = Shot.Resigned;
        SendToCk(ResignedSignal);
        Plugin.L.LogInfo($"[COSTKILLER] démission : limite {limit} -> {gm.fireLimit}, {SafeFired(gm)} licenciement(s)");
    }

    // ===================== Touche E (chez le Cost Killer) =====================
    static bool mine, ready;
    static Shot state;
    static string victimName;
    static float holdStart = -1f, requestAt;
    static IntPtr holdTarget;
    static TMPro.TextMeshProUGUI hint;

    static void Done(Shot s, string message)
    {
        state = s;
        holdStart = -1f;
        RoleKit.RefreshRoleTabs();
        Hud.Show(message, 7f);
    }

    // Appelé par DebugMenu.Update chez tous
    internal static void Tick()
    {
        // Demande restée sans réponse (refusée par le serveur) : on peut réessayer
        if (state == Shot.None && victimName != null && Time.time - requestAt > 3f) victimName = null;
        bool can = mine && ready && state == Shot.None && victimName == null;
        var target = can ? RoleKey.AimedPlayer(FireDistance) : null;
        if (target != null && !CanFire(target)) target = null;
        bool onDummy = can && target == null && RoleKey.AimedDummy(FireDistance);
        float progress = 0f;
        if ((target != null || onDummy) && RoleKey.Held)
        {
            var key = target != null ? target.Pointer : new IntPtr(-1);
            if (holdStart < 0f || holdTarget != key) { holdStart = Time.time; holdTarget = key; }
            progress = Mathf.Clamp01((Time.time - holdStart) / FireSeconds);
            if (progress >= 1f) { holdStart = -1f; progress = 0f; if (target != null) Request(target); else RequestDummy(); }
        }
        else holdStart = -1f;
        if (mine) RoleKey.Bar(progress, Screen.height * 0.09f - 8f);
        if (onDummy) { ShowHint(T("Maintiens E pendant 5s pour virer le mannequin (compte comme un spécialiste, test)", "Hold E for 5s to fire the dummy (counts as a specialist, test)")); return; }
        ShowHint(target == null ? null
            : T($"Maintiens E pendant 5s pour virer {target.lobbyPlayer.username}", $"Hold E for 5s to fire {target.lobbyPlayer.username}"));
    }

    static bool CanFire(Player.PlayerController target)
    {
        var lp = target?.lobbyPlayer;
        return lp != null && !lp.isFired && lp.playerRole != PlayerRole.Manager;
    }

    static void Request(Player.PlayerController target)
    {
        var via = RoleKey.InteractableOf(target);
        if (via == null) return;
        victimName = target.lobbyPlayer.username;   // bloque un second appui en attendant la réponse du serveur
        requestAt = Time.time;
        NetSignal.SendToServer(via, FireAction);
    }

    static void RequestDummy()
    {
        var via = NetSignal.PlayerInteractableOf(NetworkClient.localPlayer);
        if (via == null) return;
        victimName = T("le mannequin", "the dummy");
        requestAt = Time.time;
        NetSignal.SendToServer(via, FireSelfAction);
    }

    // Invite en bas de l'écran, dans la police du jeu, au-dessus de la barre
    static void ShowHint(string text)
    {
        if (text == null) { if (hint != null) hint.gameObject.SetActive(false); return; }
        if (hint == null)
        {
            hint = GameUi.NewText("DaleMod_CostKiller");
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
    static string StateText() => state switch
    {
        Shot.Hit => T($"<color={Hex}>Réussi !</color> {victimName} était un fainéant. Tu continues en simple spécialiste.",
                      $"<color={Hex}>Success!</color> {victimName} was a slacker. You carry on as a plain specialist."),
        Shot.Missed => T($"<color=#FF4040>Erreur :</color> {victimName} était un spécialiste. Tu vas être viré(e).",
                         $"<color=#FF4040>Mistake:</color> {victimName} was a specialist. You're getting fired."),
        Shot.Resigned => T($"<color=#FF4040>Viré(e)</color> pour avoir renvoyé {victimName}, un spécialiste.",
                           $"<color=#FF4040>Fired</color> for firing {victimName}, a specialist."),
        Shot.Saved => T("Ta cible était le neveu du patron : tir perdu, sans sanction. Tu continues en simple spécialiste.",
                        "Your target was the boss's nephew: shot wasted, no penalty. You carry on as a plain specialist."),
        Shot.Neutral => T($"{victimName} était le Jester : pas de sanction. Tu continues en simple spécialiste.",
                          $"{victimName} was the Jester: no penalty. You carry on as a plain specialist."),
        _ => ready ? T("Prêt : vise un joueur à 1 m et maintiens E pendant 5s pour le virer.", "Ready: aim at a player within 1 m and hold E for 5s to fire them.")
                   : T("Disponible après la première réunion, et jamais pendant une réunion.", "Available after the first meeting, and never during a meeting."),
    };

    static void RoleCard(ShowRoleTab t)
    {
        if (t.roleText != null) t.roleText.text = T("Vous êtes le Cost Killer !", "You are the Cost Killer!");
        if (t.roleDescriptionText != null)
            t.roleDescriptionText.text = state == Shot.None
                ? T("Spécialiste. Une fois par partie, vire toi-même un joueur. Un fainéant : bravo. Un spécialiste : tu pars avec lui. Maintiens F1 pour l'aide.",
                    "Specialist. Once per game, fire a player yourself. A slacker: well done. A specialist: you go with them. Hold F1 for help.")
                : StateText() + T(" Maintiens F1 pour l'aide.", " Hold F1 for help.");
    }

    static string HelpText()
    {
        return $"<size=170%><color={Hex}>COST KILLER</color></size>\n\n" + StateText() + "\n\n" + T(
            "Tu es spécialiste et tu gagnes avec les spécialistes.\n" +
            "Une seule fois par partie, après la première réunion et hors réunion : vise un joueur à 1 m maximum et maintiens E pendant 5s. Il est viré sur-le-champ, comme par le manager, et ça compte comme un licenciement.\n" +
            "Un fainéant : mission réussie. Un spécialiste : tu es viré(e) juste après lui (ta démission ne compte pas comme un licenciement).\n" +
            "Le Jester : pas de sanction. Le neveu du patron : il est protégé, ton tir est perdu. Jamais le manager.",
            "You're a specialist and win with the specialists.\n" +
            "Once per game, after the first meeting and outside meetings: aim at a player within 1 m and hold E for 5s. They're fired on the spot, as if by the manager, and it counts as a firing.\n" +
            "A slacker: mission accomplished. A specialist: you get fired right after them (your resignation doesn't count as a firing).\n" +
            "The Jester: no penalty. The boss's nephew: protected, your shot is wasted. Never the manager.");
    }
}
