using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Objects.Scripts;
using Player;
using Player.Lobby;
using Player.Tasks;
using Props.Scripts;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Pascal Prout" (rouge, Slacker ; voir TaskPools) : un coussin péteur rose à poser sur la chaise d'un collègue.
//  1) le prendre dans le placard de la cuisine (celui de la gelée), seulement avec la tâche ;
//  2) le poser sur la chaise d'une victime nommée, tirée au sort à la mise en place de la tâche parmi les joueurs encore
//     en jeu, hors Janitors (son nom est dans l'étape) :
//     action "Poser le coussin péteur" sur SON poste, coussin en main. Le coussin apparaît sur sa chaise chez tous.
// Piège (serveur) : le premier joueur qui s'assoit à ce poste déclenche un des 5 pets d'Assets/farts (le même chez tous,
// joué sur la chaise) et se relève aussitôt de surprise (Usable.ServerStopUse, comme quand on se lève soi-même).
static class WhoopeeCushion
{
    internal const string Key = "dalemod.pascalProut";
    internal const string Obj = "WhoopeeCushion", TookAction = "Took", PlacedAction = "Placed";
    const float TakeSeconds = 1.5f, PlaceSeconds = 2f, StandUpDelay = 0.5f;
    const int FartCount = 5;
    // Signaux : coussin posé (à tous, par le poste) / étape validée (au poseur) / pet n° k (à tous, par le poste) /
    // victime (à tous, par son joueur) ; actions serveur : prendre au placard / poser sur le poste
    const float PlacedAllSignal = -96.2f, PlacedSignal = -96.3f, VictimSignal = -96.5f, TakeAction = -96.6f, PlaceAction = -96.7f;
    static float FartSignal(int k) => -96.41f - k * 0.01f;

    internal static readonly Dictionary<string, Color> Colors = new()
    {
        ["rubber"] = new Color(0.91f, 0.35f, 0.59f),
        ["rubber_dark"] = new Color(0.75f, 0.24f, 0.45f),
        ["rubber_light"] = new Color(0.96f, 0.53f, 0.73f),
        ["seam"] = new Color(0.78f, 0.25f, 0.49f),
        ["nozzle"] = new Color(0.84f, 0.29f, 0.53f),
        ["hole"] = new Color(0.24f, 0.08f, 0.14f),
    };
    // Modèle : tools/make_whoopee.py (22,5 cm de diamètre, 5 cm d'épaisseur : 3/4 de la taille d'origine)
    internal static readonly ModItem Cushion = new("Whoopee Cushion", 0xC0CA1008, "whoopee.obj", new Vector3(0.2025f, 0.039375f, 0.2025f), Colors)
        { ResetChildRotation = true, SitOnPivot = true, NeverDespawn = true };

    static TaskHook Hook(string action) => new(null, Obj, null, action, null, null);
    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    internal static void Register()
    {
        NetSignal.OnLocalPlayer(PlacedSignal, () =>
        {
            if (!TaskActive()) return;
            TaskHook.TriggerTaskHookCommandStatic(Hook(PlacedAction));
            Plugin.L.LogInfo("[PROUT] coussin posé : tâche validée");
        });
        NetSignal.On(PlacedAllSignal, it => ShowOnChair(it.TryCast<WorkStationController>()));
        for (int k = 0; k < FartCount; k++)
        {
            int n = k;
            NetSignal.On(FartSignal(n), it => Fart(it.TryCast<WorkStationController>(), n));
        }
        NetSignal.On(VictimSignal, it => { if (it.netIdentity == NetworkClient.localPlayer) standUpCheckAt = Time.time + StandUpDelay + 0.6f; });
    }

    // ===================== Victime (chez le joueur qui a la tâche, à la mise en place) =====================
    // Tâche -> netId du LobbyPlayer visé (0 : personne d'autre n'a de poste, n'importe quel poste d'un autre convient)
    static readonly Dictionary<IntPtr, uint> victims = new();

    // Victime : un joueur nommé, tiré au sort parmi ceux encore en jeu (pas viré, pas Janitor, pas moi), qui a un bureau
    // attribué ; c'est sur la chaise de CE bureau qu'il faut poser le coussin
    internal static string PickVictim(Task task)
    {
        var me = RoleKit.LocalLobby();
        var candidates = new List<LobbyPlayer>();
        int noDesk = 0;
        foreach (var lp in Object.FindObjectsOfType<LobbyPlayer>())
        {
            if (lp == null || lp.isFired || lp.playerRole == PlayerRole.None || lp.playerRole == PlayerRole.Janitor) continue;
            if (me != null && lp.netId == me.netId) continue;
            if (DeskOf(lp.netId) == null) { noDesk++; continue; }
            candidates.Add(lp);
        }
        if (noDesk > 0) Plugin.L.LogInfo($"[PROUT] {noDesk} joueur(s) sans bureau attribué écarté(s)");
        if (candidates.Count == 0)
        {
            // Seul en jeu (test solo) : comme la tâche du jeu, un nom quand même, le sien
            if (me != null && DeskOf(me.netId) != null)
            {
                victims[task.Pointer] = me.netId;
                Plugin.L.LogInfo("[PROUT] aucun autre joueur avec un bureau : victime = soi-même (test solo)");
                return Colored(me);
            }
            victims[task.Pointer] = 0;
            Plugin.L.LogInfo("[PROUT] aucun joueur avec un bureau : n'importe quel poste");
            return CustomTaskPatches.Text("un collègue", "a coworker");
        }
        var v = candidates[new System.Random().Next(candidates.Count)];
        victims[task.Pointer] = v.netId;
        Plugin.L.LogInfo($"[PROUT] victime tirée parmi {candidates.Count} joueur(s)");   // jamais de nom
        return Colored(v);
    }

    // Nom à la couleur du joueur, comme la cible de la tâche du jeu "Agrafeuse en gelée"
    static string Colored(LobbyPlayer lp)
    {
        var colors = LobbyManager.instance?.playerColors;
        if (colors == null || lp.playerColorIndex < 0 || lp.playerColorIndex >= colors.Count) return lp.username;
        return $"<color=#{ColorUtility.ToHtmlStringRGB(colors[lp.playerColorIndex])}>{lp.username}</color>";
    }

    // Bureau attribué à ce joueur (LobbyPlayer)
    static WorkStationController DeskOf(uint lobbyNetId)
    {
        foreach (var ws in Object.FindObjectsOfType<WorkStationController>())
            if (ws.ownerLobbyPlayer != null && ws.ownerLobbyPlayer.netId == lobbyNetId) return ws;
        return null;
    }

    // Pour l'indicateur : le(s) poste(s) où poser le coussin (aucun si la tâche n'est pas active chez moi)
    internal static List<WorkStationController> TargetDesks()
    {
        var l = new List<WorkStationController>();
        if (!TaskActive()) return l;
        foreach (var ws in Object.FindObjectsOfType<WorkStationController>()) if (IsTargetDesk(ws)) l.Add(ws);
        return l;
    }

    static bool IsTargetDesk(WorkStationController ws)
    {
        var task = CustomTaskPatches.ActiveTask();
        if (task == null || !victims.TryGetValue(task.Pointer, out var victim)) return false;
        var owner = ws.ownerLobbyPlayer;
        if (victim != 0) return owner != null && owner.netId == victim;
        var me = RoleKit.LocalLobby();
        return owner == null || me == null || owner.netId != me.netId;   // repli (personne d'autre) : tout poste qui n'est pas le mien
    }

    // ===================== Actions =====================
    static bool tookSent;

    // Placard de la cuisine : celui qui sort la gelée
    [HarmonyPatch(typeof(KitchenCabinetController), nameof(KitchenCabinetController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void CupboardAction(KitchenCabinetController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var cab = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Prendre le coussin péteur", "Take the whoopee cushion"), InteractionPermission.All,
            NetSignal.ServerAction(cab, TakeAction, player => { if (!HoldsCushion(player)) Cushion.GiveTo(player); }), false,
            Show(() => TaskActive() && !tookSent && !Cushion.LocalHolds() && IsJellyCabinet(cab)), TakeSeconds, NoTimer(), NoStart(), false, false));
    }

    static IntPtr jellyCabinet;
    static bool IsJellyCabinet(KitchenCabinetController cab)
    {
        if (jellyCabinet == IntPtr.Zero || ModItem.JellyCabinet()?.Pointer != jellyCabinet)
        {
            var k = ModItem.JellyCabinet();
            jellyCabinet = k == null ? IntPtr.Zero : k.Pointer;
            Plugin.L.LogInfo($"[PROUT] placard de la gelée : {(k == null ? "introuvable" : $"'{k.interactableName}' ({k.name})")}");
        }
        return cab.Pointer == jellyCabinet;
    }

    // Poste de la victime : poser le coussin (coussin en main)
    [HarmonyPatch(typeof(WorkStationController), nameof(WorkStationController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void DeskAction(WorkStationController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var ws = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Poser le coussin péteur sur la chaise", "Put the whoopee cushion on the chair"), InteractionPermission.All,
            NetSignal.ServerAction(ws, PlaceAction, player => ServerPlace(ws, player)), HeldItem, false,
            Show(() => TaskActive() && Cushion.LocalHolds() && IsTargetDesk(ws)), PlaceSeconds, false, NoStart(), false));
    }

    static bool HoldsCushion(NetworkIdentity player)
    {
        foreach (var c in Object.FindObjectsOfType<Consumable>())
            if (Cushion.Is(c) && ModItem.HeldBy(c, player)) return true;
        return false;
    }

    // Objet tenu requis par l'action : sans ça, le jeu masque l'action quand on a quelque chose en main
    static Il2CppSystem.Type HeldItem => Il2CppType.Of<Consumable>();

    // Pas d'action "Consommer" sur le coussin (copie du soda)
    [HarmonyPatch(typeof(Consumable), nameof(Consumable.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void NoConsume(Consumable __instance)
    {
        var alts = __instance.alternatives;
        if (alts != null && alts.Count > 0 && Cushion.Is(__instance)) alts.RemoveAt(alts.Count - 1);
    }

    // Étape 1 : chez le joueur, dès qu'il tient un coussin
    internal static void Tick()
    {
        if (!TaskActive()) tookSent = false;
        else if (!tookSent && Cushion.LocalHolds()) { tookSent = true; TaskHook.TriggerTaskHookCommandStatic(Hook(TookAction)); Plugin.L.LogInfo("[PROUT] coussin pris"); }
        ServerTick();
        ClientStandUpCheck();
    }

    // ===================== Serveur : pose et piège =====================
    static readonly HashSet<IntPtr> armed = new();                        // postes piégés
    static readonly Dictionary<IntPtr, NetworkIdentity> lastUser = new();  // occupant précédent de chaque poste piégé
    static readonly List<(WorkStationController ws, NetworkIdentity player, float at)> standUps = new();
    static float nextPoll;

    static void ServerPlace(WorkStationController ws, NetworkIdentity player)
    {
        if (!NetworkServer.active || armed.Contains(ws.Pointer) || !Cushion.ServerRemoveHeldBy(player)) return;
        armed.Add(ws.Pointer);
        lastUser[ws.Pointer] = ws.usingPlayerController;   // déjà assis là : ne compte qu'au prochain passage
        NetSignal.Send(ws, PlacedAllSignal);
        NetSignal.SendToPlayer(player, PlacedSignal);
        Plugin.L.LogInfo("[PROUT] coussin posé sur une chaise");
    }

    static void ServerTick()
    {
        if (!NetworkServer.active) return;
        for (int i = standUps.Count - 1; i >= 0; i--)
        {
            var (ws, player, at) = standUps[i];
            if (Time.time < at) continue;
            standUps.RemoveAt(i);
            try
            {
                if (ws != null && player != null && ws.usingPlayerController == player) { ws.ServerStopUse(player); Plugin.L.LogInfo("[PROUT] victime relevée"); }
            }
            catch (Exception e) { Plugin.L.LogError("[PROUT] relever la victime : " + e); }
        }
        if (armed.Count == 0 || Time.time < nextPoll) return;
        nextPoll = Time.time + 0.2f;
        foreach (var ws in Object.FindObjectsOfType<WorkStationController>())
        {
            if (!armed.Contains(ws.Pointer)) continue;
            var user = ws.usingPlayerController;
            lastUser.TryGetValue(ws.Pointer, out var before);
            lastUser[ws.Pointer] = user;
            if (user == null || user == before) continue;
            armed.Remove(ws.Pointer);
            int k = new System.Random().Next(FartCount);
            NetSignal.Send(ws, FartSignal(k));
            var pi = NetSignal.PlayerInteractableOf(user);
            if (pi != null) NetSignal.Send(pi, VictimSignal);
            standUps.Add((ws, user, Time.time + StandUpDelay));
            Plugin.L.LogInfo($"[PROUT] quelqu'un s'assoit sur le coussin : pet n°{k + 1}");
        }
    }

    // ===================== Clients : coussin sur la chaise, pet =====================
    static readonly Dictionary<IntPtr, GameObject> shown = new();   // poste -> coussin visible sur sa chaise
    static readonly AudioClip[] farts = new AudioClip[FartCount];

    static void ShowOnChair(WorkStationController ws)
    {
        if (ws == null) return;
        if (shown.TryGetValue(ws.Pointer, out var old) && old != null) Object.Destroy(old);
        var vis = Cushion.CreateVisual();
        if (vis == null) { Plugin.L.LogWarning("[PROUT] modèle du coussin indisponible"); return; }
        var (pos, anchor) = SeatOf(ws);
        vis.transform.position = pos;
        vis.transform.rotation = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);
        vis.transform.SetParent(anchor, true);   // une chaise à roulettes emporte le coussin
        shown[ws.Pointer] = vis;
    }

    // Dessus de l'assise, sous la place assise du poste (sitPos) : le composant Chair du jeu est sur le poste entier
    // (bureau + PC), on vise donc la pièce "chaise" du poste et on cherche l'assise par un rayon vertical à la place
    // assise, entre 25 et 52 cm du sol (repli : 45 cm)
    static (Vector3 pos, Transform anchor) SeatOf(WorkStationController ws)
    {
        var sit = ws.sitPos != null ? ws.sitPos.position : ws.transform.position;
        float floor = Blood.Bounds(ws).floor;
        Renderer chairPart = null; float best = float.MaxValue;
        foreach (var r in ws.GetComponentsInChildren<Renderer>(true))
        {
            var n = r.gameObject.name.ToLowerInvariant();
            if (!n.Contains("chair") && !n.Contains("seat") && !n.Contains("chaise")) continue;
            float d = Vector3.Distance(Flat(r.bounds.center), Flat(sit));
            Plugin.L.LogInfo($"[PROUT] pièce '{r.gameObject.name}' : bas {r.bounds.min.y:0.00}, haut {r.bounds.max.y:0.00}, à {d:0.00} m de la place assise");
            if (d < best) { best = d; chairPart = r; }
        }
        float seatY = -1f;
        foreach (var h in Physics.RaycastAll(new Vector3(sit.x, floor + 1.0f, sit.z), Vector3.down, 1.0f))
        {
            if (h.collider == null) continue;
            Plugin.L.LogInfo($"[PROUT] rayon : '{h.collider.name}' à {h.point.y - floor:0.00} m du sol");
            if (h.point.y < floor + 0.25f || h.point.y > floor + 0.52f) continue;   // ni le plateau du bureau, ni le haut de la chaise (0,61 trop haut)
            if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
            if (h.point.y > seatY) seatY = h.point.y;
        }
        if (seatY < 0f) seatY = floor + 0.45f;
        var anchor = chairPart != null ? chairPart.transform : ws.sitPos != null ? ws.sitPos : ws.transform;
        Plugin.L.LogInfo($"[PROUT] poste '{ws.name}' : sol {floor:0.00}, place assise {sit.y:0.00}, assise retenue {seatY:0.00}, " +
                         $"accroché à '{anchor.name}'");
        return (new Vector3(sit.x, seatY, sit.z), anchor);
    }

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    static void Fart(WorkStationController ws, int k)
    {
        if (ws == null) return;
        Vector3 at = ws.sitPos != null ? ws.sitPos.position : ws.transform.position;
        if (shown.TryGetValue(ws.Pointer, out var vis) && vis != null) { at = vis.transform.position; Object.Destroy(vis); }
        shown.Remove(ws.Pointer);
        farts[k] ??= ModAssets.LoadWav($"farts/{k + 1}.wav");
        if (farts[k] != null) ModAudio.Play(farts[k], at);
        Plugin.L.LogInfo($"[PROUT] PRRRT (n°{k + 1})");
    }

    // Repli chez la victime : si le jeu ne l'a pas relevée, on force la sortie de l'état assis
    static float standUpCheckAt = -1f;
    static void ClientStandUpCheck()
    {
        if (standUpCheckAt < 0f || Time.time < standUpCheckAt) return;
        standUpCheckAt = -1f;
        var pc = NetworkClient.localPlayer?.GetComponent<PlayerController>();
        if (pc == null || !pc.isInChair) return;
        Plugin.L.LogWarning("[PROUT] toujours assis après le pet : sortie forcée");
        try { pc.isInChair = false; pc.SetState((int)Player.StateMachineLogic.States.Idle); }
        catch (Exception e) { Plugin.L.LogError("[PROUT] sortie forcée : " + e.Message); }
    }

    static Il2CppSystem.Func<bool> Show(Func<bool> f) => DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(f);
    static UnityAction<float> NoTimer() => DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { }));
    static UnityAction<NetworkIdentity> NoStart() => DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { }));
}
