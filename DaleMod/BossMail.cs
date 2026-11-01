using System;
using System.Collections.Generic;
using Computer.Executables.Email;
using Computer.Scripts.System;
using HarmonyLib;
using Player.Lobby;
using Player.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Lettre au boss" (Slacker) : s'installer au PC d'un collègue puis, avec l'app Email du jeu, envoyer au manager
// un e-mail injurieux qui part de l'adresse du collègue. On préremplit la fenêtre "écrire un e-mail" et on remplace l'envoi.
// Les clics PC sont rejoués chez tous les clients : seul le client du joueur assis à ce PC envoie.
static class BossMail
{
    internal const string Key = "dalemod.lettreAuBoss";
    internal const string HijackedAction = "Hijacked", DeskObj = "WorkStation", InsultedAction = "Insulted", ManagerObj = "Manager";
    internal static bool OwnPcCounts;   // debug : test solo sur son propre PC

    internal static TaskHook Hook(string action, string obj) => new(null, obj, null, action, null, null);

    static string currentDeskOwner;     // propriétaire du bureau où le joueur local est assis

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);
    static string MyName() => LobbyManager.instance?.GetLocalPlayer()?.username;
    static bool IsHijack(string owner) => !string.IsNullOrEmpty(owner) && (OwnPcCounts || owner != MyName());

    // --- Bureau occupé (hooks locaux du jeu) ---
    [HarmonyPatch(typeof(TaskHook), nameof(TaskHook.TriggerTaskHookCommandStatic))]
    [HarmonyPrefix]
    static void TrackDesk(TaskHook taskHook)
    {
        if (taskHook == null || taskHook.passiveObjectName != DeskObj) return;
        if (taskHook.action == "Enter" && !string.IsNullOrEmpty(taskHook.passiveObjectOwner))
        {
            currentDeskOwner = taskHook.passiveObjectOwner;
            Plugin.L.LogInfo($"[MAIL] assis au bureau de '{currentDeskOwner}' (piratage={IsHijack(currentDeskOwner)})");
            if (IsHijack(currentDeskOwner) && TaskActive()) TaskHook.TriggerTaskHookCommandStatic(Hook(HijackedAction, DeskObj));
        }
        else if (taskHook.action == "Stopped Using") currentDeskOwner = null;
    }

    // PC du bureau où le joueur local est assis, s'il compte comme piraté
    static ComputerController HijackedPc()
    {
        if (!IsHijack(currentDeskOwner)) return null;
        foreach (var pc in Object.FindObjectsOfType<ComputerController>())
            if (pc.user != null && pc.user.username == currentDeskOwner) return pc;
        return null;
    }

    internal static string ManagerAddress()
    {
        foreach (var pc in Object.FindObjectsOfType<ComputerController>())
            if (pc.user != null && pc.user.playerRole == PlayerRole.Manager && !string.IsNullOrEmpty(pc.emailAddress)) return pc.emailAddress;
        foreach (var p in Object.FindObjectsOfType<LobbyPlayer>())
            if (p.playerRole == PlayerRole.Manager) return p.username + "@DaleDawson.com";
        return null;
    }

    // --- Contenu ---
    static readonly (string fr, string en)[] Insults =
    {
        ("Même la machine à café a plus de leadership que vous.", "Even the coffee machine shows more leadership than you."),
        ("Vos réunions auraient pu être des e-mails. Celui-ci aussi, d'ailleurs.", "Your meetings could have been e-mails. So could this one."),
        ("Le poisson rouge de l'accueil gère mieux son aquarium que vous l'équipe.", "The lobby goldfish runs its tank better than you run this team."),
        ("J'ai vu des imprimantes bourrées plus inspirantes que vos discours.", "I've seen jammed printers more inspiring than your speeches."),
        ("Votre cravate est la seule chose qui tienne la route dans ce bureau.", "Your tie is the only thing holding this office together."),
        ("Si l'incompétence était un KPI, vous seriez employé du mois.", "If incompetence were a KPI, you'd be employee of the month."),
        ("Le chat de l'étage a plus de charisme et il dort 20 h par jour.", "The office cat has more charisma, and it sleeps 20 hours a day."),
        ("Je démissionne. Ah non, en fait je reste, juste pour vous embêter.", "I quit. Actually no, I'm staying, just to annoy you."),
    };
    static string Subject() => CustomTaskPatches.Text("Feedback anonyme… ou pas", "Anonymous feedback… or not");
    static string current;              // insulte préremplie pour la fenêtre en cours

    // --- Fenêtre "écrire un e-mail" : préremplie si elle s'ouvre sur le PC piraté ---
    static readonly HashSet<IntPtr> seen = new();

    internal static void Tick()
    {
        foreach (var w in Object.FindObjectsOfType<WriteEmail>())
        {
            if (!seen.Add(w.Pointer)) continue;
            var pc = w.GetComponentInParent<ComputerController>(true);
            var target = HijackedPc();
            bool mine = pc != null && target != null && pc.Pointer == target.Pointer && TaskActive();
            Plugin.L.LogInfo($"[MAIL] fenêtre 'écrire' ouverte sur {(pc == null ? "?" : pc.emailAddress)} -> {(mine ? "lettre au boss" : "normale")}");
            if (!mine) continue;
            var manager = ManagerAddress();
            if (manager == null) { pc.ShowPopup(CustomTaskPatches.Text("Aucun manager dans le bureau…", "No manager in the office…"), "OK"); continue; }
            var pick = Insults[UnityEngine.Random.Range(0, Insults.Length)];
            current = CustomTaskPatches.Text(pick.fr, pick.en);
            if (w.sender != null) w.sender.text = pc.emailAddress;
            if (w.receiver != null) w.receiver.text = manager;
            if (w.subject != null) w.subject.text = Subject();
            if (w.text != null) w.text.text = current;
        }
    }

    // --- Envoi : remplacé par le nôtre sur le PC piraté (côté joueur assis uniquement) ---
    [HarmonyPatch(typeof(WriteEmail), nameof(WriteEmail.SendMail))]
    [HarmonyPrefix]
    static bool OnSendMail(WriteEmail __instance)
    {
        var pc = __instance.GetComponentInParent<ComputerController>(true);
        var target = HijackedPc();
        Plugin.L.LogInfo($"[MAIL] SendMail sur {(pc == null ? "?" : pc.emailAddress)}");
        if (pc == null || target == null || pc.Pointer != target.Pointer || !TaskActive() || current == null) return true;

        var manager = ManagerAddress();
        if (manager == null) return true;
        EmailManager.instance.SendEmail(pc.emailAddress, manager, Subject(), current);
        Plugin.L.LogInfo($"[MAIL] insulte envoyée de '{pc.emailAddress}' à '{manager}'");
        current = null;
        TaskHook.TriggerTaskHookCommandStatic(Hook(InsultedAction, ManagerObj));
        pc.ShowPopup(CustomTaskPatches.Text("Message envoyé. Bonne chance à ton collègue.", "Message sent. Good luck to your coworker."), "OK");
        __instance.applicationWindow?.Close();
        return false;
    }
}
