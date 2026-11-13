using System;
using System.Collections.Generic;
using Computer.Executables.Email;
using Computer.Scripts.System;
using HarmonyLib;
using Mirror;
using Objects.Scripts;
using Player;
using Player.Tasks;
using Props.Scripts;
using Props.Shelf;
using Room;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Commande de fournitures" (bleue, Specialist ; voir TaskPools) :
// 1) envoyer la commande au fournisseur (mail prérempli, destinataire fictif) ;
// 2) attendre la livraison : le carton arrive 10 s plus tard au point de livraison, à l'accueil
//    (ou, sur les cartes sans accueil, dans la salle de réunion) ;
// 3) ramasser le carton ; 4) le ranger dans l'armoire de l'entrepôt (posé sur une étagère, ou au pied).
// Nom du carton : les 15 premiers caractères tapés par le joueur dans le mail (sinon un nom tiré au hasard). Le nom
// alourdit le carton : 5 % de vitesse en moins par caractère pour celui qui le porte (75 % max), tapé ou non. Le carton disparaît
// au bout de 3 min (sinon il n'y aurait jamais assez de place).
// Le nom arrive au serveur par le mail lui-même (ligne "Réf. carton"), lu au passage de la commande d'envoi du jeu.
static class SupplyOrder
{
    internal const string Key = "dalemod.commandeFournitures";
    internal const string OrderedAction = "Ordered", ArrivedAction = "Arrived", CollectedAction = "Collected", DeliveredAction = "Stored", BoxObj = "Supply Box";
    const string Supplier = "commandes@fournitures-pro.fr", RefTag = "Réf. carton : ", CustomMark = "*";
    const uint AssetId = 0xC0CA1007;
    const int MaxChars = 15;
    const float DeliveryDelay = 10f, BoxLife = 180f, MalusPerChar = 0.05f, ChairClearance = 1.2f, SeatClearance = 1f;
    const float DeliveredSignal = -86.1f, ArrivedSignal = -86.2f, ShelfReach = 1.5f;

    static readonly string[] RandomNames = { "Post-its", "Claviers", "Écran de PC", "Disquettes", "Agrafes", "Toner", "Ramettes de papier", "Gobelets" };

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    internal static void Register()
    {
        NetSignal.OnLocalPlayer(ArrivedSignal, () =>
        {
            SyncTask();
            if (!TaskActive() || arrived) return;
            arrived = true;
            TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, BoxObj, null, ArrivedAction, null, null));
            Hud.Show(CustomTaskPatches.Text("Ton colis est arrivé ! Va le chercher.", "Your parcel has arrived! Go pick it up."), 4f);
            Plugin.L.LogInfo("[SUPPLY] colis arrivé");
        });
        NetSignal.OnLocalPlayer(DeliveredSignal, () =>
        {
            if (!TaskActive()) return;
            TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, BoxObj, null, DeliveredAction, null, null));
            Hud.Show(CustomTaskPatches.Text("Fournitures rangées à l'entrepôt.", "Supplies stored in the warehouse."), 3f);
            Plugin.L.LogInfo("[SUPPLY] carton rangé à l'entrepôt");
        });
    }

    // ===================== Commande (mail) =====================
    static readonly HashSet<IntPtr> seen = new(), ours = new();
    static IntPtr taskPtr;
    static bool ordered, arrived, collected;

    static void SyncTask()
    {
        var p = CustomTaskPatches.ActiveTask()?.Pointer ?? IntPtr.Zero;
        if (p == taskPtr) return;
        taskPtr = p; ordered = arrived = collected = false;
    }

    static string Subject() => CustomTaskPatches.Text("Commande de fournitures", "Supply order");
    const string AskFr = "Merci de nous livrer : ", AskEn = "Please deliver: ";
    static string Body() => CustomTaskPatches.Text("Bonjour,\n\n" + AskFr, "Hello,\n\n" + AskEn);   // on tape juste après les deux-points

    static void TickMail()
    {
        SyncTask();
        if (!TaskActive() || ordered) return;
        foreach (var w in Object.FindObjectsOfType<WriteEmail>())
        {
            if (!seen.Add(w.Pointer)) continue;
            var pc = w.GetComponentInParent<ComputerController>(true);
            if (pc == null || !pc.isLocalPlayerUsingComputer) continue;
            ours.Add(w.Pointer);
            if (w.sender != null) w.sender.text = pc.emailAddress;
            if (w.receiver != null) w.receiver.text = Supplier;
            if (w.subject != null) w.subject.text = Subject();
            if (w.text != null) w.text.text = Body();
            Plugin.L.LogInfo("[SUPPLY] bon de commande prérempli");
        }
    }

    [HarmonyPatch(typeof(WriteEmail), nameof(WriteEmail.SendMail))]
    [HarmonyPrefix]
    static bool OnSendMail(WriteEmail __instance)
    {
        if (!ours.Remove(__instance.Pointer)) return true;
        var pc = __instance.GetComponentInParent<ComputerController>(true);
        if (pc == null || !TaskActive()) return true;

        // Ce que le joueur a tapé après "Merci de nous livrer :" (ou tout ce qui diffère du modèle)
        string typed = Typed(__instance.text?.text ?? "");
        bool custom = typed.Length > 0;
        string name = custom ? typed.Substring(0, Math.Min(MaxChars, typed.Length)) : RandomNames[UnityEngine.Random.Range(0, RandomNames.Length)];
        string body = (__instance.text?.text ?? Body()) + "\n\n" + RefTag + (custom ? CustomMark : "") + name;
        EmailManager.instance.SendEmail(pc.emailAddress, Supplier, Subject(), body);
        ordered = true;
        TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, BoxObj, null, OrderedAction, null, null));
        pc.ShowPopup(CustomTaskPatches.Text($"Commande envoyée : « {name} ». Livraison à l'accueil dans 10 s.",
                                            $"Order sent: \"{name}\". Delivery at reception in 10 s."), "OK");
        __instance.applicationWindow?.Close();
        Plugin.L.LogInfo($"[SUPPLY] commande '{name}' (saisie={custom})");
        return false;
    }

    static string Typed(string text)
    {
        foreach (var ask in new[] { AskFr.Trim(), AskEn.Trim() })
        {
            int i = text.IndexOf(ask, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var rest = text.Substring(i + ask.Length);
            int nl = rest.IndexOf('\n');
            return (nl >= 0 ? rest.Substring(0, nl) : rest).Replace("​", "").Trim();
        }
        return "";   // modèle effacé : on ne devine pas, nom au hasard
    }

    // Serveur : la commande passe par le mail (CmdSendEmail) ; on y lit le nom du carton, sans consommer le message
    static readonly List<(float at, string name, bool custom, NetworkIdentity owner)> deliveries = new();

    [HarmonyPatch(typeof(EmailManager), nameof(EmailManager.InvokeUserCode_CmdSendEmail__String__String__String__String__String))]
    [HarmonyPrefix]
    static void OnServerMail(NetworkReader reader, NetworkConnectionToClient senderConnection)
    {
        if (reader == null || !NetworkServer.active) return;
        int pos = reader.Position;
        string recipient, message;
        try
        {
            NetworkReaderExtensions.ReadString(reader);                 // expéditeur
            recipient = NetworkReaderExtensions.ReadString(reader);
            NetworkReaderExtensions.ReadString(reader);                 // objet
            message = NetworkReaderExtensions.ReadString(reader);
        }
        catch { return; }
        finally { reader.Position = pos; }
        if (recipient != Supplier || message == null) return;
        int i = message.LastIndexOf(RefTag, StringComparison.Ordinal);
        if (i < 0) return;
        var name = message.Substring(i + RefTag.Length).Trim();
        bool custom = name.StartsWith(CustomMark);
        if (custom) name = name.Substring(CustomMark.Length);
        deliveries.Add((Time.time + DeliveryDelay, name, custom, senderConnection?.identity));
        Plugin.L.LogInfo($"[SUPPLY] commande reçue par le fournisseur : '{name}', livraison dans {DeliveryDelay}s");
    }

    // ===================== Carton (objet réseau cloné du StorageBox du jeu) =====================
    static GameObject template;

    static void EnsureRegistered()
    {
        if (!NetworkClient.active && !NetworkServer.active) return;
        if (NetworkClient.prefabs.ContainsKey(AssetId)) return;
        if (template == null)
        {
            GameObject source = null;
            foreach (var p in NetworkManager.singleton.spawnPrefabs)
                if (p != null && p.GetComponent<StorageBox>() != null) { source = p; break; }
            if (source == null) { Plugin.L.LogWarning("[SUPPLY] prefab de carton (StorageBox) introuvable"); return; }
            bool was = source.activeSelf;
            source.SetActive(false);
            template = Object.Instantiate(source);
            source.SetActive(was);
            Object.DontDestroyOnLoad(template);
            template.name = "DaleMod_SupplyBox";
            template.GetComponent<NetworkIdentity>()._assetId = AssetId;
            var sb = template.GetComponent<StorageBox>();
            sb.interactableName = BoxObj;
            sb.maxItemCount = 0;              // vide : rien à en sortir
            sb.despawnAfterIdle = false;      // on gère la disparition (3 min)
        }
        NetworkClient.RegisterPrefab(template, AssetId);
        Plugin.L.LogInfo("[SUPPLY] prefab du carton enregistré");
    }

    static bool IsSupply(Interactable it) => it != null && it.interactableName == BoxObj;
    // Nom synchronisé : le champ texte "label" de l'Interactable (sur StorageBox, "label" désigne l'étiquette TMP affichée)
    static string NameOf(StorageBox b) => (b.Cast<Interactable>().label ?? "").Replace("​", "");

    // Serveur
    static readonly Dictionary<uint, (float born, NetworkIdentity owner, bool stored)> boxes = new();

    static void ServerSpawn(string name, bool custom, NetworkIdentity owner)
    {
        EnsureRegistered();
        if (template == null) return;
        var at = DeliveryPoint() ?? Vector3.zero;
        var go = Object.Instantiate(template, at + Vector3.up * 0.4f, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
        go.SetActive(true);
        var sb = go.GetComponent<StorageBox>();
        sb.despawnAfterIdle = false;
        sb.NetworkinteractableName = BoxObj;
        sb.Networklabel = (custom ? "​" : "") + name;   // SyncVar : le nom (et le marqueur "lourd") chez tous
        NetworkServer.Spawn(go, AssetId, null);
        boxes[go.GetComponent<NetworkIdentity>().netId] = (Time.time, owner, false);
        if (owner != null) NetSignal.SendToPlayer(owner, ArrivedSignal);
        Plugin.L.LogInfo($"[SUPPLY] carton '{name}' livré à l'accueil en {at}");
    }

    static float nextServerPoll;
    static void TickServer()
    {
        for (int i = deliveries.Count - 1; i >= 0; i--)
            if (Time.time >= deliveries[i].at) { var d = deliveries[i]; deliveries.RemoveAt(i); ServerSpawn(d.name, d.custom, d.owner); }

        if (boxes.Count == 0 || Time.time < nextServerPoll) return;
        nextServerPoll = Time.time + 0.5f;
        var shelves = Object.FindObjectsOfType<ShelfController>();
        foreach (var b in Object.FindObjectsOfType<StorageBox>())
        {
            if (!IsSupply(b) || !boxes.TryGetValue(b.netId, out var info)) continue;
            if (Time.time - info.born > BoxLife) { boxes.Remove(b.netId); b.ServerDestroyCollectible(); continue; }   // 3 min
            if (info.stored || b.usingPlayerController != null || !InCabinet(b, shelves)) continue;
            boxes[b.netId] = (info.born, info.owner, true);
            if (info.owner != null) NetSignal.SendToPlayer(info.owner, DeliveredSignal);
        }
    }

    // ===================== Chez chaque client : étiquette, lourdeur, marquage au sol =====================
    static float savedMove = -1f, savedSprint;
    static float nextClientPoll;

    static void TickClient()
    {
        if (Time.time < nextClientPoll) return;
        nextClientPoll = Time.time + 0.2f;
        var me = NetworkClient.localPlayer;
        var pc = me?.GetComponent<PlayerController>();
        float malus = 0f;
        foreach (var b in Object.FindObjectsOfType<StorageBox>())
        {
            if (!IsSupply(b)) continue;
            var name = NameOf(b);
            var tmp = b.label ?? b.GetComponentInChildren<TMPro.TextMeshPro>(true);   // étiquette du carton
            if (tmp != null && tmp.text != name) tmp.text = name;
            // Lourdeur : 5 % par caractère du nom (75 % max), nom tapé ou tiré au hasard
            if (me != null && ModItem.HeldBy(b, me))
            {
                malus = Mathf.Min(0.75f, name.Length * MalusPerChar);
                SyncTask();
                if (TaskActive() && arrived && !collected)
                {
                    collected = true;
                    TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, BoxObj, null, CollectedAction, null, null));
                    Plugin.L.LogInfo("[SUPPLY] colis ramassé");
                }
            }
        }
        if (pc == null) return;
        // Lourdeur : vitesse réduite tant que je porte un carton "lourd", rétablie ensuite
        if (malus > 0f)
        {
            if (savedMove < 0f) { savedMove = pc.moveSpeed; savedSprint = pc.sprintSpeed; }
            pc.moveSpeed = savedMove * (1f - malus);
            pc.sprintSpeed = savedSprint * (1f - malus);
        }
        else if (savedMove >= 0f)
        {
            pc.moveSpeed = savedMove; pc.sprintSpeed = savedSprint;
            savedMove = -1f;
        }
        EnsureMarker();
    }

    internal static void Tick()
    {
        EnsureRegistered();
        TickMail();
        if (NetworkServer.active) TickServer();
        TickClient();
    }

    // ===================== Zones =====================
    static RoomController RoomContaining(Vector3 p)
    {
        foreach (var room in Object.FindObjectsOfType<RoomController>())
            foreach (var col in room.GetComponentsInChildren<Collider>(true))
                if (col.bounds.Contains(p)) return room;
        return null;
    }

    static bool InRoom(RoomController room, Vector3 p)
    {
        foreach (var col in room.GetComponentsInChildren<Collider>(true)) if (col.bounds.Contains(p)) return true;
        return false;
    }

    static Bounds? RoomBounds(RoomController room)
    {
        Bounds? b = null;
        foreach (var col in room.GetComponentsInChildren<Collider>(true))
        {
            if (b == null) b = col.bounds; else { var x = b.Value; x.Encapsulate(col.bounds); b = x; }
        }
        return b;
    }

    // Rangé dans l'armoire : posé sur une étagère, ou au sol juste à côté
    static bool InCabinet(StorageBox b, ShelfController[] shelves)
    {
        if (b.currentHolder?.TryCast<ShelfController>() != null) return true;
        foreach (var s in shelves)
            if (Vector3.Distance(s.transform.position, b.transform.position) < ShelfReach) return true;
        return false;
    }

    // Pour les indicateurs
    internal static Component Marker() => marker != null ? marker.transform : null;
    internal static Component[] Boxes()
    {
        var l = new List<Component>();
        foreach (var b in Object.FindObjectsOfType<StorageBox>()) if (IsSupply(b)) l.Add(b);
        return l.ToArray();
    }

    static RoomController storageRoom;
    static bool storageLogged;
    static RoomController StorageRoom()
    {
        if (storageRoom != null) return storageRoom;
        var shelf = Object.FindObjectOfType<ShelfController>();
        if (shelf != null) storageRoom = RoomContaining(shelf.transform.position);
        if (!storageLogged) { storageLogged = true; Plugin.L.LogInfo($"[SUPPLY] zone de stockage : {(storageRoom == null ? "introuvable" : $"'{storageRoom.roomName}' / '{storageRoom.subRoomName}'")}"); }
        return storageRoom;
    }

    // Point de livraison : dans la pièce de l'accueil (ou, sans accueil, la salle de réunion du tableau blanc), sur le
    // côté de la pièce (le long d'un mur), au sol, sans rien autour (murs, guichet, table), à 1 m de toute chaise.
    // Même calcul chez tous les joueurs (les joueurs eux-mêmes sont ignorés) : le marquage est au même endroit partout.
    static Vector3? delivery;
    static bool deliveryComputed;
    static Vector3? DeliveryPoint()
    {
        if (deliveryComputed) return delivery;
        Component chair = Object.FindObjectOfType<ReceptionChair>();
        if (chair == null) chair = Object.FindObjectOfType<WhiteBoardController>();   // carte sans accueil : salle de réunion
        if (chair == null) return null;   // carte pas encore chargée : on réessaiera
        deliveryComputed = true;
        var room = RoomContaining(chair.transform.position);
        var rb = room != null ? RoomBounds(room) : null;
        var center = rb?.center ?? chair.transform.position;
        var ext = rb?.extents ?? new Vector3(4f, 2f, 4f);
        // Sur le côté de la pièce : on essaie d'abord les points le long des murs (plus petite distance au bord),
        // puis vers le centre ; à distance égale, le plus proche du milieu du mur
        var candidates = new List<Vector3>();
        for (float x = -ext.x + 0.5f; x <= ext.x - 0.5f; x += 0.4f)
            for (float z = -ext.z + 0.5f; z <= ext.z - 0.5f; z += 0.4f)
                candidates.Add(new Vector3(center.x + x, center.y, center.z + z));
        float EdgeDist(Vector3 p) => Mathf.Min(Mathf.Min(p.x - (center.x - ext.x), (center.x + ext.x) - p.x),
                                               Mathf.Min(p.z - (center.z - ext.z), (center.z + ext.z) - p.z));
        candidates.Sort((a, b) =>
        {
            int c = Mathf.RoundToInt(EdgeDist(a) * 5f).CompareTo(Mathf.RoundToInt(EdgeDist(b) * 5f));
            return c != 0 ? c : Vector3.Distance(a, center).CompareTo(Vector3.Distance(b, center));
        });
        var seats = new List<Vector3>();   // toutes les chaises (accueil, réunion, bureaux) : on garde ses distances
        foreach (var ch in Object.FindObjectsOfType<Chair>()) seats.Add(ch.transform.position);
        foreach (var ch in Object.FindObjectsOfType<ReceptionChair>()) seats.Add(ch.transform.position);
        float top = rb?.max.y ?? center.y + 2f;
        // Sol de la pièce : sous la chaise (ou le tableau)
        float floorY = Physics.Raycast(chair.transform.position + Vector3.up * 0.5f, Vector3.down, out var fh, 3f, ~0, QueryTriggerInteraction.Ignore)
            ? fh.point.y : chair.transform.position.y;
        var eye = new Vector3(chair.transform.position.x, floorY + 1.7f, chair.transform.position.z);
        foreach (var c in candidates)
        {
            if (!Physics.Raycast(new Vector3(c.x, top - 0.1f, c.z), Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.9f) continue;                                                   // pas un sol
            // Le rectangle de la pièce peut déborder hors du bâtiment (Springfield) : dans la pièce, au niveau de son sol,
            // et visible depuis la chaise / le tableau (pas de mur entre les deux)
            if (room != null && !InRoom(room, hit.point + Vector3.up * 0.5f)) continue;
            if (Mathf.Abs(hit.point.y - floorY) > 0.4f) continue;
            // (tracé depuis le point : le tableau est contre un mur, un obstacle tout près de lui ne compte pas)
            if (Physics.Linecast(hit.point + Vector3.up * 1.7f, eye, out var wall, ~0, QueryTriggerInteraction.Ignore)
                && Vector3.Distance(wall.point, eye) > 0.8f && wall.collider.GetComponentInParent<PlayerController>() == null) continue;
            if (Vector3.Distance(new Vector3(hit.point.x, 0, hit.point.z), new Vector3(chair.transform.position.x, 0, chair.transform.position.z)) < ChairClearance) continue;
            if (NearSeat(hit.point, seats)) continue;                                                    // pas contre une chaise
            if (Blocked(hit.point + Vector3.up * 0.45f, new Vector3(0.45f, 0.4f, 0.45f))) continue;   // murs, guichet, meubles
            delivery = hit.point;
            break;
        }
        delivery ??= chair.transform.position + chair.transform.forward * 1.5f;
        Plugin.L.LogInfo($"[SUPPLY] zone de livraison ({(chair.TryCast<ReceptionChair>() != null ? "accueil" : "salle de réunion")}) : pièce {(room == null ? "introuvable" : $"'{room.roomName}'")}, point {delivery}");
        return delivery;
    }

    static bool NearSeat(Vector3 p, List<Vector3> seats)
    {
        foreach (var s in seats)
            if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(s.x, s.z)) < SeatClearance) return true;
        return false;
    }

    static bool Blocked(Vector3 center, Vector3 half)
    {
        foreach (var col in Physics.OverlapBox(center, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col.GetComponentInParent<PlayerController>() != null) continue;   // un joueur de passage ne compte pas
            if (col.GetComponentInParent<Collectible>() != null) continue;        // objets ramassables non plus
            return true;
        }
        return false;
    }

    // Point de livraison (chez chaque client) : simple repère invisible, cible de l'indicateur de l'étape "attendre le
    // colis" ; une fois le carton arrivé, l'indicateur passe sur le carton lui-même
    static GameObject marker;
    static void EnsureMarker()
    {
        if (marker != null) return;
        var p = DeliveryPoint();
        if (p == null) return;
        marker = new GameObject("DaleMod_DeliveryPoint");
        marker.transform.position = p.Value;
    }
}
