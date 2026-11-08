using System;
using System.Collections.Generic;
using Computer.Scripts.System;
using HarmonyLib;
using Il2CppSystem.FileSystem;
using Mirror;
using Objects.Scripts;
using Player.Lobby;
using Player.Tasks;
using Props.Misc.PaperTray;
using Props.Printer;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Macron Explosion" (bleue, Specialist ; voir TaskPools) : un tract syndical FO à imprimer 3 fois (3 étapes) puis
// à déposer dans 3 bacs à papier de postes différents (3 étapes ; pas le sien, les bureaux vides comptent).
// - Le tract est un document du PC : icône "Tract_FO.txt" sur le bureau, qui ouvre le bloc-notes rempli avec le texte.
//   Il apparaît sur le PC du joueur qui a la tâche, et de temps en temps au hasard sur un PC, comme les autres documents.
//   Le serveur décide et prévient tout le monde (l'icône doit exister chez chacun : les clics du PC sont rejoués partout).
// - Impression : bouton Print du bloc-notes (imprimante du jeu), depuis mon PC. Dépôt : n'importe quel tract que j'ai eu
//   en main, peu importe qui l'a imprimé (deux joueurs avec la tâche ne se gênent plus à l'imprimante).
static class UnionFlyer
{
    internal const string Key = "dalemod.macronExplosion";
    internal const string PrintedAction = "Printed", DeliveredAction = "Delivered", FlyerObj = "Union Flyer";
    const string Label = "Tract_FO.txt", Marker = "MACRON EXPLOSION";
    const int Needed = 3;
    const float AddUp = -85.1f, AddIcon = -85.2f, NearTray = 0.3f;

    static string Text() => CustomTaskPatches.Text(
        "FORCE OUVRIÈRE - SECTION DALE & DAWSON\n\n*** " + Marker + " ***\n\n" +
        "Camarades !\n\nLa direction veut supprimer la DEUXIÈME pause café de l'après-midi.\n" +
        "C'est NON ! Le café est un droit, pas un privilège !\n\n" +
        "Nos revendications :\n- Retour du café gratuit à volonté\n- Une sieste réglementaire de 20 minutes\n" +
        "- La fin des réunions qui auraient pu être des mails\n- Le droit de GlucksTube au poste de travail\n\n" +
        "RASSEMBLEMENT devant la machine à café à 15h !\nOn lâche rien ! Tous ensemble !",
        "WORKERS' UNION - DALE & DAWSON BRANCH\n\n*** " + Marker + " ***\n\n" +
        "Comrades!\n\nManagement wants to cut the SECOND afternoon coffee break.\n" +
        "We say NO! Coffee is a right, not a privilege!\n\n" +
        "Our demands:\n- Free unlimited coffee is back\n- A mandatory 20-minute nap\n" +
        "- No more meetings that could have been e-mails\n- The right to GlucksTube at our desks\n\n" +
        "RALLY at the coffee machine at 3pm!\nWe won't back down! All together!");

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    internal static void Register()
    {
        // Le joueur qui a la tâche demande le tract sur son PC ; le serveur prévient tout le monde
        NetSignal.OnServer(AddUp, (it, _) => NetSignal.Send(it, AddIcon));
        NetSignal.On(AddIcon, it =>
        {
            var ws = it.TryCast<WorkStationController>();
            if (ws?.computerController != null) EnsureIcon(ws.computerController);
        });
    }

    // --- Icône du tract sur le bureau d'un PC (chez ce client) ---
    static void EnsureIcon(ComputerController pc)
    {
        var fs = pc.GetComponentInChildren<FileSystemManager>(true);
        if (fs?.root == null) return;
        var all = new List<FsObject>();
        PcMod.Walk(fs.root, all);
        FsObject notepad = null, textDoc = null;
        Directory desktop = null;
        foreach (var o in all)
        {
            if (o.label == Label) return;   // déjà là
            var dirName = PcMod.Safe(() => o.directory?.name);
            if (desktop == null && dirName.IndexOf("desk", StringComparison.OrdinalIgnoreCase) >= 0) desktop = o.directory;
            if (notepad == null && PcMod.Safe(o.GetFileName).IndexOf("notepad", StringComparison.OrdinalIgnoreCase) >= 0) notepad = o;
            if (textDoc == null && (o.label ?? "").EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) textDoc = o;
        }
        if (notepad == null || desktop?.contents == null) { Plugin.L.LogWarning("[TRACT] bloc-notes ou bureau introuvable"); return; }
        // Même nom que le bloc-notes (l'icône l'ouvre), libellé et icône d'un document texte
        var file = new File(notepad.GetFileName(), Label) { label = Label, icon = textDoc?.icon ?? notepad.icon, directory = desktop };
        desktop.contents.Add(file);
        fs.onFsObjectChanged?.Invoke();
        foreach (var d in Object.FindObjectsOfType<Computer.Desktop.Desktop>())
            if (d.fileSystemManager != null && d.fileSystemManager.Pointer == fs.Pointer) d.UpdateDesktop();
        Plugin.L.LogInfo($"[TRACT] tract ajouté sur le bureau du PC de {pc.user?.username ?? "personne"}");
    }

    // --- Ouverture : le bloc-notes qui s'ouvre juste après un clic sur notre icône est le tract ---
    static readonly Dictionary<IntPtr, float> clicks = new();   // PC -> heure du clic sur l'icône du tract
    static readonly HashSet<IntPtr> seenPads = new(), flyerPads = new();

    [HarmonyPatch(typeof(DesktopIcon), nameof(DesktopIcon.OnCursorClick))]
    [HarmonyPrefix]
    static void OnIconClick(DesktopIcon __instance)
    {
        if (__instance.fsObject?.label != Label) return;
        var pc = __instance.GetComponentInParent<ComputerController>(true);
        if (pc != null) clicks[pc.Pointer] = Time.unscaledTime;
    }

    // Impression depuis le bloc-notes du tract, sur MON PC
    static int printed, delivered;
    static IntPtr taskPtr;

    [HarmonyPatch(typeof(Executables.NotePad), nameof(Executables.NotePad.PrintDocument))]
    [HarmonyPostfix]
    static void OnPrint(Executables.NotePad __instance)
    {
        if (!flyerPads.Contains(__instance.Pointer)) return;
        var pc = __instance.GetComponentInParent<ComputerController>(true);
        var local = PcMod.LocalComputer();
        if (pc == null || local == null || pc.Pointer != local.Pointer) return;
        SyncTask();
        if (!TaskActive() || printed >= Needed) return;
        printed++;
        TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, FlyerObj, null, PrintedAction, null, null));
        Plugin.L.LogInfo($"[TRACT] tract imprimé ({printed}/{Needed})");
    }

    static void SyncTask()
    {
        var p = CustomTaskPatches.ActiveTask()?.Pointer ?? IntPtr.Zero;
        if (p == taskPtr) return;
        taskPtr = p;
        printed = delivered = 0;
        carried.Clear(); usedTrays.Clear(); counted.Clear();
        iconAsked = false;
    }

    // --- Chaque frame ---
    static readonly HashSet<uint> carried = new(), counted = new();
    static readonly HashSet<IntPtr> usedTrays = new();
    static bool iconAsked;
    static float nextRandom = -1f, nextPoll;

    internal static void Tick()
    {
        SyncTask();
        RandomAppearance();

        // Tract sur mon PC quand j'ai la tâche (une demande par tâche)
        if (TaskActive() && !iconAsked)
        {
            var ws = LobbyManager.instance?.GetLocalPlayer()?.workStationController;
            if (ws != null)
            {
                iconAsked = true;
                if (NetworkServer.active) NetSignal.Send(ws, AddIcon); else NetSignal.SendToServer(ws, AddUp);
            }
        }

        // Bloc-notes du tract : texte et titre (réappliqués tant qu'il est ouvert)
        foreach (var pad in SceneCache.All<Executables.NotePad>())
        {
            if (seenPads.Add(pad.Pointer))
            {
                var pc = pad.GetComponentInParent<ComputerController>(true);
                if (pc != null && clicks.TryGetValue(pc.Pointer, out var t) && Time.unscaledTime - t < 3f) flyerPads.Add(pad.Pointer);
            }
            if (!flyerPads.Contains(pad.Pointer)) continue;
            var text = Text();
            if (pad.text != null && pad.text.text != text) pad.text.text = text;
            var win = pad.applicationWindow;
            if (win != null && win.windowTitle != Label) { win.windowTitle = Label; if (win.title != null) win.title.text = Label; }
        }

        if (Time.time < nextPoll) return;
        nextPoll = Time.time + 0.25f;
        if (!TaskActive()) return;

        // N'importe quel tract que j'ai eu en main (peu importe qui l'a imprimé), puis déposé dans le bac d'un autre poste
        var myLobby = LobbyManager.instance?.GetLocalPlayer()?.netIdentity;
        var me = NetworkClient.localPlayer?.GetComponent<Player.PlayerController>();
        PaperTray[] trays = null;
        foreach (var doc in Object.FindObjectsOfType<Document>())
        {
            if (doc.TryCast<PrintedImage>() != null || !(doc.text ?? "").Contains(Marker)) continue;
            var holder = doc.usingPlayerController;
            if (holder != null)
            {
                if (me != null && holder.Pointer == me.Pointer && carried.Add(doc.netId)) Plugin.L.LogInfo("[TRACT] tract en main");
                continue;
            }
            if (!carried.Contains(doc.netId) || counted.Contains(doc.netId) || delivered >= Needed) continue;
            trays ??= Object.FindObjectsOfType<PaperTray>();
            var tray = TrayOf(doc, trays);
            if (tray == null) continue;
            if (tray.lobbyPlayerOwner != null && myLobby != null && tray.lobbyPlayerOwner == myLobby) continue;   // pas son propre bac
            if (!usedTrays.Add(tray.Pointer)) continue;   // 3 bacs différents
            counted.Add(doc.netId);
            delivered++;
            TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, FlyerObj, null, DeliveredAction, null, null));
            Plugin.L.LogInfo($"[TRACT] tract déposé dans le bac de {(tray.lobbyPlayerOwner == null ? "un bureau vide" : tray.lobbyPlayerOwner.name)} ({delivered}/{Needed})");
        }
    }

    static PaperTray TrayOf(Document doc, PaperTray[] trays)
    {
        var t = doc.currentHolder?.TryCast<PaperTray>();
        if (t != null) return t;
        foreach (var x in trays)
            if (Vector3.Distance(x.transform.position, doc.transform.position) < NearTray) return x;
        return null;
    }

    // Serveur : de temps en temps, un tract apparaît au hasard sur le PC d'un joueur, comme les autres documents
    static void RandomAppearance()
    {
        if (!NetworkServer.active) return;
        if (nextRandom < 0f) { nextRandom = Time.time + UnityEngine.Random.Range(240f, 480f); return; }
        if (Time.time < nextRandom) return;
        nextRandom = Time.time + UnityEngine.Random.Range(240f, 480f);
        if (UnityEngine.Random.value > 0.35f) return;
        var desks = new List<WorkStationController>();
        foreach (var w in Object.FindObjectsOfType<WorkStationController>()) if (w.ownerLobbyPlayer != null && w.computerController != null) desks.Add(w);
        if (desks.Count > 0) NetSignal.Send(desks[UnityEngine.Random.Range(0, desks.Count)], AddIcon);
    }
}
