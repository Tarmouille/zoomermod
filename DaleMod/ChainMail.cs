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

// Deux tâches e-mail, sur le modèle de BossMail (fenêtre "écrire" préremplie, envoi remplacé par le nôtre) :
// - "Spam RH" (rouge, Slacker ; voir TaskPools) : envoyer la chaîne 5 fois (5 étapes identiques).
// - "Transfert de mails" (Specialist) : 3 mails de travail arrivent dans la boîte du joueur ; il doit les transférer
//   (3 étapes). La fenêtre "écrire" reprend le mail affiché, sinon le prochain à transférer.
// Le joueur choisit le destinataire avec l'outil du jeu : n'importe quel joueur, lui-même compris, et autant de fois
// qu'il veut (les joueurs virés gardent leur PC : exiger des destinataires différents bloquait la tâche).
// Chaque envoi valide une étape.
static class ChainMail
{
    internal const string SpamKey = "dalemod.spamRH", ForwardKey = "dalemod.transfertMails";
    internal const string ChainedAction = "Chained", ChainObj = "Chain Mail", ForwardedAction = "Forwarded", WorkMailObj = "Work Mail";
    internal const int ChainCount = 5;

    static TaskHook Hook(string action, string obj) => new(null, obj, null, action, null, null);
    static bool Active(string key) => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), key);
    static string MyName() => LobbyManager.instance?.GetLocalPlayer()?.username;

    // --- Contenu ---
    static string ChainSubject() => CustomTaskPatches.Text("URGENT !!! À TRANSFÉRER À 5 COLLÈGUES", "URGENT!!! FORWARD TO 5 COWORKERS");
    static string ChainBody() => CustomTaskPatches.Text(
        "Bonjour à tous,\n\nCe message circule dans l'entreprise depuis 1998. Si tu ne l'envoies pas à 5 autres employés " +
        "dans l'heure, le chiffre d'affaires va en pâtir.\n\nKevin de la compta ne l'a pas transféré : le lendemain, l'imprimante " +
        "a mangé son rapport trimestriel. Brenda l'a envoyé à 12 personnes : elle a eu une place de parking.\n\n" +
        "Ne brise pas la chaîne. Le CA compte sur toi.\n\nPS : ceci n'est pas un spam, c'est de la culture d'entreprise.",
        "Hi all,\n\nThis message has been going around the company since 1998. If you don't send it to 5 other employees " +
        "within the hour, revenue will suffer.\n\nKevin from accounting didn't forward it: the next day, the printer ate " +
        "his quarterly report. Brenda sent it to 12 people: she got a parking spot.\n\n" +
        "Don't break the chain. Revenue is counting on you.\n\nPS: this is not spam, it's company culture.");

    static readonly (string from, string subjFr, string subjEn, string bodyFr, string bodyEn)[] WorkMails =
    {
        ("direction.financiere@DaleDawson.com", "Chiffres du trimestre à valider", "Quarterly figures to approve",
         "Bonjour,\n\nVous trouverez ci-joint les chiffres consolidés du trimestre. Merci de les faire valider par un collègue " +
         "avant vendredi midi, la clôture comptable ne pourra pas être décalée.\n\nCordialement,\nLa direction financière",
         "Hello,\n\nPlease find attached the consolidated quarterly figures. Please have a coworker approve them before " +
         "Friday noon; the accounting close cannot be postponed.\n\nBest regards,\nFinance department"),
        ("rh@DaleDawson.com", "Mise à jour de la procédure d'évacuation", "Updated evacuation procedure",
         "Bonjour,\n\nSuite au dernier exercice incendie, la procédure d'évacuation a été mise à jour : le point de " +
         "rassemblement est désormais le parking. Merci de diffuser l'information à votre équipe.\n\nLe service RH",
         "Hello,\n\nFollowing the last fire drill, the evacuation procedure has been updated: the assembly point is now " +
         "the parking lot. Please share this with your team.\n\nHR department"),
        ("commercial@DaleDawson.com", "Rendez-vous client Henderson : ordre du jour", "Henderson client meeting: agenda",
         "Bonjour,\n\nLe rendez-vous avec la société Henderson est confirmé pour jeudi 10 h. Ordre du jour : bilan de la " +
         "commande en cours, nouveaux tarifs, délais de livraison. Merci de transmettre à la personne qui suit le dossier.\n\n" +
         "Le service commercial",
         "Hello,\n\nThe meeting with Henderson Inc. is confirmed for Thursday 10am. Agenda: status of the current order, " +
         "new pricing, delivery times. Please pass this on to whoever handles the account.\n\nSales department"),
    };
    static string WorkSubject(int i) => CustomTaskPatches.Text(WorkMails[i].subjFr, WorkMails[i].subjEn);
    static string WorkBody(int i) => CustomTaskPatches.Text(WorkMails[i].bodyFr, WorkMails[i].bodyEn);
    static string ForwardSubject(int i) => CustomTaskPatches.Text("TR : ", "FW: ") + WorkSubject(i);
    static string ForwardBody(int i) =>
        CustomTaskPatches.Text("Pour info, voir ci-dessous.\n\n---------- Message transféré ----------\nDe : ",
                               "FYI, see below.\n\n---------- Forwarded message ----------\nFrom: ")
        + WorkMails[i].from + "\n" + CustomTaskPatches.Text("Objet : ", "Subject: ") + WorkSubject(i) + "\n\n" + WorkBody(i);

    // --- État de la tâche en cours (remis à zéro à chaque nouvelle tâche) ---
    static IntPtr taskPtr;
    static readonly HashSet<int> forwarded = new();          // mails de travail déjà transférés
    static int validated;                                    // étapes validées
    static bool seeded;                                      // mails de travail déjà déposés dans la boîte

    // Fenêtres "écrire" préremplies par nous : -1 = chaîne, i >= 0 = transfert du mail i
    static readonly Dictionary<IntPtr, int> windows = new();
    static readonly HashSet<IntPtr> seen = new();

    static void SyncTask()
    {
        var t = CustomTaskPatches.ActiveTask();
        var ptr = t?.Pointer ?? IntPtr.Zero;
        if (ptr == taskPtr) return;
        taskPtr = ptr;
        forwarded.Clear(); validated = 0; seeded = false;
    }

    // Adresses des collègues (joueurs) : adresse en minuscules -> adresse
    static Dictionary<string, string> Coworkers(out string mine)
    {
        mine = null;
        var me = MyName();
        var all = new Dictionary<string, string>();
        foreach (var pc in Object.FindObjectsOfType<ComputerController>())
        {
            if (pc.user == null || string.IsNullOrEmpty(pc.emailAddress)) continue;
            if (pc.user.username == me) mine = pc.emailAddress;
            all[pc.emailAddress.ToLowerInvariant()] = pc.emailAddress;
            all[pc.user.username.ToLowerInvariant()] = pc.emailAddress;   // accepte aussi le nom affiché
        }
        mine ??= me + "@DaleDawson.com";
        return all;
    }

    internal static void Tick()
    {
        SyncTask();
        bool spam = Active(SpamKey), fwd = Active(ForwardKey);

        // Mails de travail déposés dans la boîte du joueur au début de la tâche
        if (fwd && !seeded && EmailManager.instance != null)
        {
            Coworkers(out var mine);
            for (int i = 0; i < WorkMails.Length; i++)
                EmailManager.instance.SendEmail(WorkMails[i].from, mine, WorkSubject(i), WorkBody(i));
            seeded = true;
            Plugin.L.LogInfo($"[CHAIN] {WorkMails.Length} mails de travail déposés pour {mine}");
        }

        foreach (var w in SceneCache.All<WriteEmail>())
        {
            if (!seen.Add(w.Pointer)) continue;
            var pc = w.GetComponentInParent<ComputerController>(true);
            if (pc == null || !pc.isLocalPlayerUsingComputer) continue;
            string subject, body;
            if (spam) { windows[w.Pointer] = -1; subject = ChainSubject(); body = ChainBody(); }
            else if (fwd)
            {
                int i = DisplayedWorkMail(pc);
                if (i < 0) continue;
                windows[w.Pointer] = i; subject = ForwardSubject(i); body = ForwardBody(i);
            }
            else continue;
            if (w.sender != null) w.sender.text = pc.emailAddress;
            if (w.receiver != null) w.receiver.text = "";   // destinataire vide à chaque nouveau mail
            if (w.subject != null) w.subject.text = subject;
            if (w.text != null) w.text.text = body;
            Plugin.L.LogInfo($"[CHAIN] fenêtre préremplie ({(spam ? "chaîne" : "transfert " + windows[w.Pointer])})");
        }
    }

    // Mail de travail affiché dans l'app Email de ce PC ; sinon le prochain pas encore transféré
    static int DisplayedWorkMail(ComputerController pc)
    {
        foreach (var e in pc.GetComponentsInChildren<Email>(true))
        {
            var info = e.mailInfo?.text;
            if (string.IsNullOrEmpty(info)) continue;
            for (int i = 0; i < WorkMails.Length; i++)
                if (!forwarded.Contains(i) && info.Contains(WorkSubject(i))) return i;
        }
        for (int i = 0; i < WorkMails.Length; i++) if (!forwarded.Contains(i)) return i;
        return -1;
    }

    // --- Envoi : remplacé par le nôtre pour nos fenêtres ---
    [HarmonyPatch(typeof(WriteEmail), nameof(WriteEmail.SendMail))]
    [HarmonyPrefix]
    static bool OnSendMail(WriteEmail __instance)
    {
        if (!windows.TryGetValue(__instance.Pointer, out int kind)) return true;
        var pc = __instance.GetComponentInParent<ComputerController>(true);
        if (pc == null) return true;
        bool chain = kind < 0;
        if (!Active(chain ? SpamKey : ForwardKey)) { windows.Remove(__instance.Pointer); return true; }

        var coworkers = Coworkers(out var mine);
        coworkers[mine.ToLowerInvariant()] = mine;   // s'envoyer le mail à soi-même compte aussi
        var typed = (__instance.receiver?.text ?? "").Replace("​", "").Trim().ToLowerInvariant();
        Plugin.L.LogInfo($"[CHAIN] envoi vers '{typed}'");
        if (!coworkers.TryGetValue(typed, out var to))
        {
            pc.ShowPopup(CustomTaskPatches.Text("Choisis un collègue (ou toi-même) comme destinataire.", "Pick a coworker (or yourself) as the recipient."), "OK");
            return false;
        }

        string subject = chain ? ChainSubject() : ForwardSubject(kind), body = chain ? ChainBody() : ForwardBody(kind);
        EmailManager.instance.SendEmail(pc.emailAddress, to, subject, body);
        if (!chain) forwarded.Add(kind);
        windows.Remove(__instance.Pointer);
        // Destinataire effacé, et fenêtre de nouveau "à préremplir" si le jeu la réutilise pour le mail suivant
        if (__instance.receiver != null) __instance.receiver.text = "";
        seen.Remove(__instance.Pointer);

        int needed = chain ? ChainCount : WorkMails.Length;
        Validate(chain);

        pc.ShowPopup(validated >= needed
                ? CustomTaskPatches.Text("Envoyé. Mission accomplie !", "Sent. Mission accomplished!")
                : CustomTaskPatches.Text($"Envoyé ({validated}/{needed}).", $"Sent ({validated}/{needed})."), "OK");
        __instance.applicationWindow?.Close();
        return false;
    }

    static void Validate(bool chain)
    {
        validated++;
        TaskHook.TriggerTaskHookCommandStatic(chain ? Hook(ChainedAction, ChainObj) : Hook(ForwardedAction, WorkMailObj));
    }
}
