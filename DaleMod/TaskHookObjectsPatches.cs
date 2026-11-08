using System.Linq;
using HarmonyLib;
using Objects.Scripts;
using Player.Tasks;
using Props.ServerRack;
using Props.WC.Toilet;
using UnityEngine;

namespace DaleMod;

// L'indicateur de direction (TaskHighlighter) pointe vers TaskHook.GetTaskHookObjects(étape en cours).
// Cette liste est remplie par les objets eux-mêmes (SetTaskHookObjects) et reconstruite par
// TaskController.SetAllTaskHookObjects : on y ajoute nos cibles après chaque reconstruction.
static class TaskHookObjectsPatches
{
    static TaskHook Hook(string action, string obj) => new TaskHook(null, obj, null, action, null, null);

    // Inscrit chaque cible absente de la liste ; renvoie true si quelque chose a été ajouté.
    // Appelé avant chaque vérification de tâche du jeu (une par joueur, très souvent) : les ~30 recherches dans la
    // scène coûtaient cher en images/s. On ne refait le travail que si la liste des cibles du jeu a changé depuis
    // (reconstruite par SetAllTaskHookObjects), et au plus tard toutes les 2 s par sécurité.
    const float RecheckSeconds = 2f;
    static int lastCount = -1;
    static float lastRun = -100f;

    internal static bool RegisterAll()
    {
        var all = TaskHook.taskHookObjects;
        int count = all?.Count ?? -1;
        if (count == lastCount && Time.unscaledTime - lastRun < RecheckSeconds) return false;
        long t0 = Perf.Start();
        try { return RegisterAllNow(); }
        finally
        {
            lastCount = TaskHook.taskHookObjects?.Count ?? -1;
            lastRun = Time.unscaledTime;
            Perf.Stop("HOOKOBJ", t0);
        }
    }

    static bool RegisterAllNow()
    {
        bool added = false;

        // "Prendre le sachet" -> son propre bureau
        var deskHook = Hook("Picked up", CokeBag.Name);
        if (TaskHook.GetTaskHookObjects(deskHook).Count == 0)
        {
            var desk = LobbyManager.instance?.GetLocalPlayer()?.workStationController;
            if (desk != null) { TaskHook.SetTaskHookObject(deskHook, desk.gameObject); added = true; }
        }

        // "Discours inspirants" -> son propre PC
        var watchHook = PcMod.WatchedHook();
        if (TaskHook.GetTaskHookObjects(watchHook).Count == 0)
        {
            var desk = LobbyManager.instance?.GetLocalPlayer()?.workStationController;
            if (desk != null) { TaskHook.SetTaskHookObject(watchHook, desk.gameObject); added = true; }
        }

        // "Le sniffer aux toilettes" -> les toilettes
        var toiletHook = Hook("Consumed", CokeBag.Name);
        if (TaskHook.GetTaskHookObjects(toiletHook).Count == 0)
            foreach (var t in Object.FindObjectsOfType<Toilet>()) { TaskHook.SetTaskHookObject(toiletHook, t.gameObject); added = true; }

        // "Lettre au boss" -> les bureaux des collègues
        var me = LobbyManager.instance?.GetLocalPlayer();
        foreach (var action in new[] { BossMail.HijackedAction, BossMail.InsultedAction })
        {
            var mailHook = BossMail.Hook(action, action == BossMail.HijackedAction ? BossMail.DeskObj : BossMail.ManagerObj);
            if (me == null || TaskHook.GetTaskHookObjects(mailHook).Count > 0) continue;
            foreach (var w in Object.FindObjectsOfType<WorkStationController>())
                if (w.ownerLobbyPlayer != null && w.ownerLobbyPlayer.netId != me.netId) { TaskHook.SetTaskHookObject(mailHook, w.gameObject); added = true; }
        }

        // "Inkish Coffee" -> les machines à café (l'étape cartouche utilise les cibles du jeu)
        var machineHook = InkCoffee.Hook(InkCoffee.PoisonedAction, InkCoffee.MachineObj);
        if (TaskHook.GetTaskHookObjects(machineHook).Count == 0)
            foreach (var m in Object.FindObjectsOfType<CoffeeMachine>()) { TaskHook.SetTaskHookObject(machineHook, m.gameObject); added = true; }

        // "Tupperware 2.0" -> frigo (prendre, remettre), éviers (laver)
        foreach (var action in new[] { Tupperware.TookAction, Tupperware.ReturnedAction, Tupperware.WashedAction })
        {
            var tuppHook = Tupperware.Hook(action);
            if (TaskHook.GetTaskHookObjects(tuppHook).Count > 0) continue;
            if (action == Tupperware.WashedAction)
            {
                foreach (var s in Object.FindObjectsOfType<SinkController>())
                    if (Tupperware.IsToiletSink(s)) { TaskHook.SetTaskHookObject(tuppHook, s.gameObject); added = true; }
            }
            else
            {
                var fridge = ModItem.Fridge();
                if (fridge != null) { TaskHook.SetTaskHookObject(tuppHook, fridge.gameObject); added = true; }
            }
        }

        // "Rafraîchissement d'équipe" -> les extincteurs, puis les collègues
        var sprayHook = FireExPrank.Hook(FireExPrank.SprayedAction, FireExPrank.CoworkerObj);
        if (TaskHook.GetTaskHookObjects(sprayHook).Count == 0)
            foreach (var p in Object.FindObjectsOfType<Player.Scripts.PlayerInteractable>())
                if (p.netIdentity != Mirror.NetworkClient.localPlayer) { TaskHook.SetTaskHookObject(sprayHook, p.gameObject); added = true; }

        // "Moulin à vent" -> son PC (dessiner, imprimer) ; le panneau d'affichage utilise les cibles du jeu
        var meDesk = LobbyManager.instance?.GetLocalPlayer()?.workStationController;
        foreach (var action in new[] { Windmill.DrewAction, Windmill.PrintedAction })
        {
            var drawHook = Windmill.Hook(action);
            if (meDesk != null && TaskHook.GetTaskHookObjects(drawHook).Count == 0) { TaskHook.SetTaskHookObject(drawHook, meDesk.gameObject); added = true; }
        }

        // "Recyclage" -> les toilettes
        var cacaHook = Caca.Hook(Caca.AteAction);
        if (TaskHook.GetTaskHookObjects(cacaHook).Count == 0)
            foreach (var t in Object.FindObjectsOfType<Toilet>()) { TaskHook.SetTaskHookObject(cacaHook, t.gameObject); added = true; }

        // "Déclencher la panne" -> les baies serveur
        var serverHook = Hook(Incident.HookAction, "Servers");
        if (TaskHook.GetTaskHookObjects(serverHook).Count == 0)
            foreach (var s in Object.FindObjectsOfType<ServerController>()) { TaskHook.SetTaskHookObject(serverHook, s.gameObject); added = true; }

        // --- Tâches ajoutées ensuite : même principe, via Targets() ---
        var myLobby = LobbyManager.instance?.GetLocalPlayer();
        var myDesk = myLobby?.workStationController;
        var desks = Object.FindObjectsOfType<WorkStationController>().ToArray();
        var otherDesks = desks.Where(w => myDesk == null || w.Pointer != myDesk.Pointer).ToArray();
        var ownedDesks = desks.Where(w => w.ownerLobbyPlayer != null).ToArray();
        var otherPlayers = Object.FindObjectsOfType<Player.Scripts.PlayerInteractable>().Where(p => p.netIdentity != Mirror.NetworkClient.localPlayer).ToArray();
        var fridgeObj = ModItem.Fridge();
        var mine = myDesk == null ? new Component[0] : new Component[] { myDesk };

        // Formation sécurité -> les extincteurs, puis son poste (mail)
        added |= Targets(Hook(SafetyCheck.CheckedAction, SafetyCheck.ExObj), Object.FindObjectsOfType<Props.FireEx.FireExController>());
        added |= Targets(Hook(SafetyCheck.ReportedAction, SafetyCheck.ManagerObj), mine);
        // Inventaire du frigo -> le frigo et les poubelles
        added |= Targets(Hook(FridgeCleanup.ThrownAction, FridgeCleanup.FoodObj),
                         Append(Object.FindObjectsOfType<Props.TrashBin.TrashBin>(), fridgeObj));
        // Café pour l'équipe -> machines à café et bureaux des autres
        added |= Targets(Hook(CoffeeRound.DeliveredAction, CoffeeRound.CoffeeObj), Append(Object.FindObjectsOfType<CoffeeMachine>(), otherDesks.Cast<Component>().ToArray()));
        // Audit Cybersécurité -> les postes des joueurs
        added |= Targets(Hook(CyberAudit.AuditedAction, CyberAudit.AntivirusObj), ownedDesks);
        // Autographe-gate -> les collègues (signature) ; STONKS -> le manager
        added |= Targets(Windmill.Hook(Autograph.SignedAction), otherPlayers);
        added |= Targets(Windmill.Hook(Autograph.ManagerSignedAction),
                         otherPlayers.Where(p => p.GetComponent<Player.PlayerController>()?.lobbyPlayer?.playerRole == Player.Lobby.PlayerRole.Manager).ToArray());
        // Gobelets -> fontaines à eau, puis collègues / PC
        added |= Targets(WaterCup.Hook(WaterCup.FilledAction, WaterCup.CupObj), Object.FindObjectsOfType<WaterCooler>());
        added |= Targets(WaterCup.Hook(ColdShower.SoakedAction, ColdShower.CoworkerObj), otherPlayers);
        added |= Targets(WaterCup.Hook(WaterDamage.SpilledAction, WaterDamage.ComputerObj), desks);
        // Power Nap, mails -> son poste
        added |= Targets(Hook(PowerNap.NappedAction, PowerNap.DeskObj), mine);
        added |= Targets(Hook(ChainMail.ChainedAction, ChainMail.ChainObj), mine);
        added |= Targets(Hook(ChainMail.ForwardedAction, ChainMail.WorkMailObj), mine);
        // Macron Explosion -> son poste (imprimer), puis les bacs des autres postes
        added |= Targets(Hook(UnionFlyer.PrintedAction, UnionFlyer.FlyerObj), mine);
        added |= Targets(Hook(UnionFlyer.DeliveredAction, UnionFlyer.FlyerObj), otherDesks);
        // 1 to 1 -> le poste des chefs (leur bureau) ; 1 to 1 Manager -> son propre poste
        added |= Targets(Hook(OneOnOne.MetAction, OneOnOne.ManagerObj),
                         desks.Where(w => OneOnOne.IsBoss(w.ownerLobbyPlayer?.GetComponent<Player.Lobby.LobbyPlayer>())).ToArray());
        added |= Targets(Hook(OneOnOne.MetAction, OneOnOne.EmployeeObj), mine);
        // Commande de fournitures -> son poste (mail), la zone de livraison (attente), le carton, puis l'armoire
        added |= Targets(Hook(SupplyOrder.OrderedAction, SupplyOrder.BoxObj), mine);
        var zone = SupplyOrder.Marker();
        if (zone != null) added |= Targets(Hook(SupplyOrder.ArrivedAction, SupplyOrder.BoxObj), new[] { zone });
        added |= Targets(Hook(SupplyOrder.CollectedAction, SupplyOrder.BoxObj), SupplyOrder.Boxes());
        added |= Targets(Hook(SupplyOrder.DeliveredAction, SupplyOrder.BoxObj), Object.FindObjectsOfType<Props.Shelf.ShelfController>());
        // Présentation client -> le tableau blanc (préparer, puis présenter dans sa salle)
        added |= Targets(Hook(ClientPresentation.PreparedAction, ClientPresentation.BoardObj), Object.FindObjectsOfType<WhiteBoardController>());
        added |= Targets(Hook(ClientPresentation.PresentedAction, ClientPresentation.BoardObj), Object.FindObjectsOfType<WhiteBoardController>());
        // Photocopie de fesses -> l'imprimante (s'asseoir) ; récupérer / punaiser : cibles du jeu
        added |= Targets(Hook(ButtCopy.SatAction, ButtCopy.CopierObj), Object.FindObjectsOfType<Props.Printer.Printer>());
        // Agrafeuse piégée -> les postes (agrafeuses)
        added |= Targets(Hook(StaplerTrap.SabotagedAction, StaplerTrap.StaplerObj), desks);
        // Karaoké -> la chaîne de la salle de pause (mettre le CD, puis chanter dans la pièce)
        added |= Targets(Hook(Karaoke.StartedAction, Karaoke.KaraokeObj), Object.FindObjectsOfType<StereoController>());
        added |= Targets(Hook(Karaoke.SangAction, Karaoke.KaraokeObj), Object.FindObjectsOfType<StereoController>());
        // Sauvegarde des serveurs -> les baies
        added |= Targets(Hook(ServerBackup.BackedUpAction, ServerBackup.ServersObj), Object.FindObjectsOfType<ServerController>());
        // Pascal Prout -> le placard de la gelée (prendre le coussin), puis le poste de la victime (le poser)
        var jelly = ModItem.JellyCabinet();
        if (jelly != null) added |= Targets(Hook(WhoopeeCushion.TookAction, WhoopeeCushion.Obj), new[] { jelly });
        added |= Targets(Hook(WhoopeeCushion.PlacedAction, WhoopeeCushion.Obj), WhoopeeCushion.TargetDesks());
        // Un poisson pour Racuch -> l'aquarium (pêcher), puis le chat (donner)
        added |= Targets(Hook(Racuch.TookAction, Racuch.Obj), Object.FindObjectsOfType<FishTankController>());
        added |= Targets(Hook(Racuch.GaveAction, Racuch.Obj), Object.FindObjectsOfType<CatController>());
        // Attention Mr. Zoomer -> les broyeurs
        added |= Targets(Hook(Zoomer.SabotagedAction, Zoomer.ShredderObj), Object.FindObjectsOfType<Props.PaperShredder.PaperShredder>());

        if (added) Plugin.L.LogInfo("[HOOKOBJ] cibles custom (ré)inscrites");
        return added;
    }

    // Inscrit les objets comme cibles de l'étape si elle n'en a aucune ; true si quelque chose a été ajouté
    static bool Targets<T>(TaskHook hook, System.Collections.Generic.IEnumerable<T> objs) where T : Component
    {
        if (TaskHook.GetTaskHookObjects(hook).Count > 0) return false;
        bool any = false;
        foreach (var o in objs) if (o != null) { TaskHook.SetTaskHookObject(hook, o.gameObject); any = true; }
        return any;
    }

    static Component[] Append<T>(System.Collections.Generic.IEnumerable<T> a, params Component[] b) where T : Component
    {
        var l = new System.Collections.Generic.List<Component>(a.Cast<Component>());
        l.AddRange(b);
        return l.ToArray();
    }

    // L'indicateur calcule ses cibles dans UpdateHighlighter : on s'assure qu'elles sont inscrites juste avant.
    [HarmonyPatch(typeof(TaskHighlighter), nameof(TaskHighlighter.UpdateHighlighter))]
    [HarmonyPrefix]
    static void BeforeHighlight() => RegisterAll();

    // Filet de sécurité : si SetAllTaskHookObjects a vidé la liste depuis, on réinscrit et on recalcule l'indicateur.
    [HarmonyPatch(typeof(TaskController), nameof(TaskController.CheckForTaskCompletion))]
    [HarmonyPrefix]
    static void EnsureRegistered()
    {
        if (RegisterAll()) TaskHighlighter.instance?.UpdateHighlighter();
    }

    internal static string Diagnose()
    {
        var tc = DebugMenu.LocalTC;
        var cur = tc?.currentTaskHook;
        var found = cur == null ? -1 : TaskHook.GetTaskHookObjects(cur).Count;
        var all = TaskHook.taskHookObjects;
        int custom = 0;
        if (all != null)
            foreach (var e in all)
                if (e.Item1 != null && (e.Item1.passiveObjectName == CokeBag.Name || e.Item1.action == Incident.HookAction)) custom++;
        Plugin.L.LogInfo($"[HOOKOBJ] étape en cours: {(cur == null ? "aucune" : SpyPatches.Hook(cur))} -> {found} objet(s) ; liste: {all?.Count} entrées dont {custom} custom");
        return $"Étape: {cur?.action} {cur?.passiveObjectName} -> {found} objet(s), {custom} cibles custom";
    }
}
