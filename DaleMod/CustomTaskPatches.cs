using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using LevelManagement;
using Mirror;
using Player.Lobby;
using Player.Tasks;
using Player.TaskManagement.Tasks;
using UnityEngine;
using TaskList = Il2CppSystem.Collections.Generic.List<Il2CppSystem.ValueTuple<Il2CppSystem.Type, Il2CppSystem.Collections.Generic.List<Player.Lobby.PlayerRole>, bool, Il2CppSystem.Collections.Generic.List<LevelManagement.Levels>>>;

namespace DaleMod;

// Une tâche custom. Le jeu instancie les tâches par type : chaque tâche custom réutilise comme "support"
// une tâche présente dans le jeu mais jamais listée (les tâches Halloween), dont on remplace le contenu.
class CustomTask
{
    public Type Carrier;
    public string Key;
    public PlayerRole Role;                      // pool : Specialist = bleue, Slacker = rouge, Saboteur = noire (TaskPools)
    public string TitleFr, TitleEn, DescFr, DescEn;
    public (string action, string obj, string fr, string en)[] Steps;
    public Action<Task> OnSetUp;                 // optionnel : mise en place (ex. faire apparaître un objet)
    public Func<TaskHook, bool> AcceptHook;      // optionnel : false = le hook ne compte pas pour cette tâche
    public bool NotForManager;                   // tâche bleue que le Manager ne tire pas (ex. "1 to 1" avec lui)
    public bool RequiresManager;                 // pas tirée s'il n'y a aucun chef (Manager ou Assistant) dans la partie (TaskPools)
    public bool ForAssistant;                    // tâche Manager que l'Assistant (sous-rôle de Specialist) peut aussi tirer
}

static class CustomTaskPatches
{
    static readonly CustomTask[] All =
    {
        new CustomTask
        {
            Carrier = typeof(ExtinguishPumpkinTask), Key = DeskBagPatches.RailKey, Role = PlayerRole.Slacker,
            TitleFr = "Rail de bureau", TitleEn = "Office line",
            DescFr = "Un petit remontant discret", DescEn = "A discreet pick-me-up",
            Steps = new[]
            {
                ("Picked up", CokeBag.Name, "Prendre le sachet à ton bureau",     "Take the bag from your desk"),
                ("Consumed",  CokeBag.Name, "Le sniffer aux toilettes",          "Sniff it in the toilets"),
            },
            AcceptHook = hook => hook.action != "Consumed" || hook.passiveObjectName != CokeBag.Name
                                 || !CokeBag.RequireToilets || CokeBag.LocalPlayerNearToilet(),
        },
        new CustomTask
        {
            // Rôle Saboteur, comme les autres sabotages (DisableServersTask…) : tâche "noire"
            Carrier = typeof(LightPumpkinTask), Key = IncidentKey, Role = PlayerRole.Saboteur,
            TitleFr = "Panne générale", TitleEn = "Total outage",
            DescFr = "Paralyser tout le bureau", DescEn = "Bring the whole office to a halt",
            Steps = new[]
            {
                (Incident.HookAction, "Servers", "Déclencher la panne depuis la salle serveur", "Trigger the outage from the server room"),
            },
        },
        new CustomTask
        {
            Carrier = typeof(ExtinguishPumpkinTask), Key = Caca.Key, Role = PlayerRole.Slacker,
            TitleFr = "Recyclage", TitleEn = "Recycling",
            DescFr = "Rien ne se perd", DescEn = "Waste not, want not",
            Steps = new[]
            {
                (Caca.AteAction,  Caca.ToiletObj, "Manger son caca",      "Eat your poop"),
            },
        },
        new CustomTask
        {
            // Calquée sur "Regarder MeowTube" : regarder la vidéo 10 s dans l'app GlucksTube de son PC
            Carrier = typeof(LightPumpkinTask), Key = PcMod.Key, Role = PlayerRole.Specialist,
            TitleFr = "Discours inspirants", TitleEn = "Inspiring speeches",
            DescFr = "Se motiver avant de bosser", DescEn = "Getting motivated before work",
            Steps = new[]
            {
                // Mêmes étapes que "Regarder MeowTube"
                ("Enter", "WorkStation" + MineSuffix, "Utiliser ton ordinateur", "Use your computer"),
                (PcMod.OpenedAction, PcMod.AppName, "Ouvrir GlucksTube", "Open GlucksTube"),
                (PcMod.WatchedAction, PcMod.AppName, "Regardez des discours inspirant sur GlucksTube pendant 10 secondes", "Watch inspiring speeches on GlucksTube for 10 seconds"),
            },
        },
        new CustomTask
        {
            // Depuis le PC d'un collègue, insulter le manager par e-mail (l'e-mail part de l'adresse du collègue)
            Carrier = typeof(LightPumpkinTask), Key = BossMail.Key, Role = PlayerRole.Slacker,
            TitleFr = "Lettre au boss", TitleEn = "Letter to the boss",
            DescFr = "Dire enfin ce qu'on pense (avec l'adresse d'un autre)", DescEn = "Finally speak your mind (with someone else's address)",
            Steps = new[]
            {
                (BossMail.HijackedAction, BossMail.DeskObj, "S'installer au PC d'un collègue", "Sit at a coworker's computer"),
                (BossMail.InsultedAction, BossMail.ManagerObj, "Envoyer un e-mail injurieux au manager depuis son PC", "Send an insulting e-mail to the manager from their computer"),
            },
        },
        new CustomTask
        {
            // Sabotage : l'encre dans la machine à café rend les cafés "nauséeux" jusqu'à réparation
            Carrier = typeof(ExtinguishPumpkinTask), Key = InkCoffee.Key, Role = PlayerRole.Saboteur,
            TitleFr = "Inkish Coffee", TitleEn = "Inkish Coffee",
            DescFr = "Un café qui a du caractère", DescEn = "Coffee with character",
            Steps = new[]
            {
                (InkCoffee.PickedAction, InkCoffee.InkObj, "Prendre une cartouche d'encre", "Take an ink cartridge"),
                (InkCoffee.PoisonedAction, InkCoffee.MachineObj, "Verser l'encre dans la machine à café", "Pour the ink into the coffee machine"),
            },
        },
        new CustomTask
        {
            Carrier = typeof(LightPumpkinTask), Key = Tupperware.SpecialistKey, Role = PlayerRole.Specialist,
            TitleFr = "Tupperware 2.0", TitleEn = "Tupperware 2.0",
            DescFr = "Midi : bolo maison", DescEn = "Lunchtime: homemade bolognese",
            Steps = new[]
            {
                (Tupperware.TookAction, Tupperware.Obj, "Prendre ton repas dans le frigo", "Take your lunch from the fridge"),
                (Tupperware.AteAction, Tupperware.Obj, "Manger tes spaghetti bolo", "Eat your spaghetti bolognese"),
                (Tupperware.WashedAction, Tupperware.Obj, "Laver ton tupperware aux lavabos des toilettes", "Wash your tupperware at the restroom sink"),
            },
        },
        new CustomTask
        {
            Carrier = typeof(ExtinguishPumpkinTask), Key = Tupperware.SlackerKey, Role = PlayerRole.Slacker,
            TitleFr = "Tupperware 2.0 : ni vu ni connu", TitleEn = "Tupperware 2.0: nobody saw a thing",
            DescFr = "La vaisselle, c'est pour les autres", DescEn = "Dishes are someone else's problem",
            Steps = new[]
            {
                (Tupperware.TookAction, Tupperware.Obj, "Prendre un repas dans le frigo", "Take a lunch from the fridge"),
                (Tupperware.AteAction, Tupperware.Obj, "Manger les spaghetti bolo", "Eat the spaghetti bolognese"),
                (Tupperware.ReturnedAction, Tupperware.Obj, "Remettre le tupperware vide dans le frigo, ni vu ni connu", "Put the empty tupperware back in the fridge, nobody saw a thing"),
            },
        },
        new CustomTask
        {
            Carrier = typeof(LightPumpkinTask), Key = FireExPrank.Key, Role = PlayerRole.Slacker,
            TitleFr = "Rafraîchissement d'équipe", TitleEn = "Team refreshment",
            DescFr = "Quoi de mieux qu'une douche de poudre ?", DescEn = "Nothing like a powder shower",
            Steps = new[]
            {
                (FireExPrank.PickedAction, FireExPrank.ExObj, "Prendre un extincteur", "Grab a fire extinguisher"),
                (FireExPrank.SprayedAction, FireExPrank.CoworkerObj, "Asperger un collègue", "Spray a coworker"),
            },
        },
        new CustomTask
        {
            // Tâche rouge (Slacker). Dessin dans Paint, impression (Paint.Print), puis panneau d'affichage (hook du jeu)
            Carrier = typeof(ExtinguishPumpkinTask), Key = Windmill.Key, Role = PlayerRole.Slacker,
            TitleFr = "Moulin à vent", TitleEn = "Windmill",
            DescFr = "Brasser de l'air, mais avec talent", DescEn = "Producing hot air, artistically",
            Steps = new[]
            {
                ("Enter", "WorkStation" + MineSuffix, "Aller à ton poste de travail", "Go to your workstation"),
                (Windmill.DrewAction, Windmill.DrawingObj, "Ouvrir Paint et dessiner", "Open Paint and draw something"),
                (Windmill.PrintedAction, Windmill.DrawingObj, "Imprimer le dessin", "Print the drawing"),
                ("Placed", "Printed Image" + LocationSeparator + "Bulletin Board",   // hook du jeu quand on punaise une image imprimée
                  "Punaiser le dessin sur le panneau d'affichage", "Pin the drawing on the bulletin board"),
            },
        },
        new CustomTask
        {
            // Sabotage invisible : le prochain qui broie un document a la main coincée (Zoomer.cs)
            Carrier = typeof(LightPumpkinTask), Key = Zoomer.Key, Role = PlayerRole.Saboteur,
            TitleFr = "Attention Mr. Zoomer", TitleEn = "Careful, Mr. Zoomer",
            DescFr = "Les doigts, c'est surfait", DescEn = "Fingers are overrated",
            Steps = new[]
            {
                (Zoomer.SabotagedAction, Zoomer.ShredderObj, "Saboter le broyeur", "Sabotage the paper shredder"),
            },
        },
        new CustomTask
        {
            Carrier = typeof(ExtinguishPumpkinTask), Key = ColdShower.Key, Role = PlayerRole.Slacker,
            TitleFr = "Douche froide", TitleEn = "Cold shower",
            DescFr = "Rien de tel pour réveiller un collègue", DescEn = "Nothing wakes a coworker up like it",
            Steps = new[]
            {
                (WaterCup.FilledAction, WaterCup.CupObj, "Remplir un gobelet à la fontaine à eau", "Fill a cup at the water cooler"),
                (ColdShower.SoakedAction, ColdShower.CoworkerObj, "Verser le gobelet sur la tête d'un collègue", "Pour the cup over a coworker's head"),
            },
        },
        new CustomTask
        {
            Carrier = typeof(LightPumpkinTask), Key = WaterDamage.Key, Role = PlayerRole.Slacker,
            TitleFr = "Dégât des eaux", TitleEn = "Water damage",
            DescFr = "Oups, ça a glissé", DescEn = "Oops, it slipped",
            Steps = new[]
            {
                (WaterCup.FilledAction, WaterCup.CupObj, "Remplir un gobelet à la fontaine à eau", "Fill a cup at the water cooler"),
                (WaterDamage.SpilledAction, WaterDamage.ComputerObj, "Renverser le gobelet sur un PC", "Spill the cup on a computer"),
            },
        },
        new CustomTask
        {
            Carrier = typeof(ExtinguishPumpkinTask), Key = PowerNap.Key, Role = PlayerRole.Slacker,
            TitleFr = "Power Nap", TitleEn = "Power Nap",
            DescFr = "Recharger les batteries, discrètement", DescEn = "Recharge your batteries, quietly",
            Steps = new[]
            {
                ("Enter", "WorkStation" + MineSuffix, "Aller à ton poste de travail", "Go to your workstation"),
                (PowerNap.NappedAction, PowerNap.DeskObj, "Dormir 20 s sans que le manager te voie", "Sleep for 20 s without the manager seeing you"),
            },
        },
        new CustomTask
        {
            Carrier = typeof(LightPumpkinTask), Key = ChainMail.SpamKey, Role = PlayerRole.Slacker,
            TitleFr = "Spam RH", TitleEn = "HR spam",
            DescFr = "Ne brise pas la chaîne", DescEn = "Don't break the chain",
            Steps = ChainSteps(),
        },
        new CustomTask
        {
            Carrier = typeof(ExtinguishPumpkinTask), Key = ChainMail.ForwardKey, Role = PlayerRole.Specialist,
            TitleFr = "Transfert de mails", TitleEn = "Mail forwarding",
            DescFr = "Faire circuler l'information", DescEn = "Keep the information flowing",
            Steps = new[]
            {
                (ChainMail.ForwardedAction, ChainMail.WorkMailObj, "Transférer le mail des chiffres du trimestre à un collègue", "Forward the quarterly figures e-mail to a coworker"),
                (ChainMail.ForwardedAction, ChainMail.WorkMailObj, "Transférer le mail de la procédure d'évacuation à un autre collègue", "Forward the evacuation procedure e-mail to another coworker"),
                (ChainMail.ForwardedAction, ChainMail.WorkMailObj, "Transférer le mail du rendez-vous Henderson à un autre collègue", "Forward the Henderson meeting e-mail to another coworker"),
            },
        },
        new CustomTask
        {
            // Comme le Moulin à vent, mais le dessin imprimé doit être signé par un autre joueur (Autograph.cs)
            Carrier = typeof(LightPumpkinTask), Key = Autograph.Key, Role = PlayerRole.Slacker,
            TitleFr = "L'Autographe-gate", TitleEn = "The Autograph-gate",
            DescFr = "Un original, dédicacé s'il vous plaît", DescEn = "An original, signed please",
            Steps = new[]
            {
                ("Enter", "WorkStation" + MineSuffix, "Aller à ton poste de travail", "Go to your workstation"),
                (Windmill.DrewAction, Windmill.DrawingObj, "Ouvrir Paint et dessiner", "Open Paint and draw something"),
                (Windmill.PrintedAction, Windmill.DrawingObj, "Imprimer le dessin", "Print the drawing"),
                (Autograph.SignedAction, Windmill.DrawingObj, "Faire signer le dessin par un collègue", "Get a coworker to sign the drawing"),
            },
        },
        new CustomTask
        {
            // Sortir 3 aliments périmés du frigo et les jeter (FridgeCleanup.cs) ; 3 étapes identiques
            Carrier = typeof(ExtinguishPumpkinTask), Key = FridgeCleanup.Key, Role = PlayerRole.Specialist,
            TitleFr = "Inventaire du frigo", TitleEn = "Fridge inventory",
            DescFr = "Quelque chose sent bizarre dans la cuisine", DescEn = "Something smells funny in the kitchen",
            Steps = new[]
            {
                (FridgeCleanup.ThrownAction, FridgeCleanup.FoodObj, "Jeter un aliment périmé du frigo (1/3)", "Throw away expired food from the fridge (1/3)"),
                (FridgeCleanup.ThrownAction, FridgeCleanup.FoodObj, "Jeter un aliment périmé du frigo (2/3)", "Throw away expired food from the fridge (2/3)"),
                (FridgeCleanup.ThrownAction, FridgeCleanup.FoodObj, "Jeter un aliment périmé du frigo (3/3)", "Throw away expired food from the fridge (3/3)"),
            },
        },
        new CustomTask
        {
            // Vérifier 3 extincteurs différents en main (1 chance sur 4 qu'il parte tout seul), puis mail au manager (SafetyCheck.cs)
            Carrier = typeof(LightPumpkinTask), Key = SafetyCheck.Key, Role = PlayerRole.Specialist,
            TitleFr = "Formation sécurité", TitleEn = "Safety training",
            DescFr = "Ça ne devrait pas exploser. Normalement.", DescEn = "It shouldn't go off. Normally.",
            Steps = new[]
            {
                (SafetyCheck.CheckedAction, SafetyCheck.ExObj, "Vérifier un extincteur en main (1/3)", "Inspect a fire extinguisher in hand (1/3)"),
                (SafetyCheck.CheckedAction, SafetyCheck.ExObj, "Vérifier un autre extincteur (2/3)", "Inspect another fire extinguisher (2/3)"),
                (SafetyCheck.CheckedAction, SafetyCheck.ExObj, "Vérifier un autre extincteur (3/3)", "Inspect another fire extinguisher (3/3)"),
                (SafetyCheck.ReportedAction, SafetyCheck.ManagerObj, "Envoyer le compte rendu au manager par mail", "E-mail the report to the manager"),
            },
        },
        new CustomTask
        {
            // 2 cafés de la machine déposés dans le porte-tasse de collègues (CoffeeRound.cs)
            Carrier = typeof(ExtinguishPumpkinTask), Key = CoffeeRound.Key, Role = PlayerRole.Specialist,
            TitleFr = "Café pour l'équipe", TitleEn = "Coffee for the team",
            DescFr = "Un petit café, ça soude une équipe", DescEn = "Nothing bonds a team like coffee",
            Steps = new[]
            {
                (CoffeeRound.DeliveredAction, CoffeeRound.CoffeeObj, "Déposer un café sur le bureau d'un collègue (1/2)", "Put a coffee on a coworker's desk (1/2)"),
                (CoffeeRound.DeliveredAction, CoffeeRound.CoffeeObj, "Déposer un café sur le bureau d'un collègue (2/2)", "Put a coffee on a coworker's desk (2/2)"),
            },
        },
        new CustomTask
        {
            // Désactiver puis réactiver l'antivirus (app du PC) de 3 PC de joueurs différents (CyberAudit.cs)
            Carrier = typeof(LightPumpkinTask), Key = CyberAudit.Key, Role = PlayerRole.Specialist,
            TitleFr = "Audit Cybersécurité", TitleEn = "Cybersecurity audit",
            DescFr = "On éteint, on rallume, et on prie", DescEn = "Turn it off, turn it on, and pray",
            Steps = new[]
            {
                (CyberAudit.AuditedAction, CyberAudit.AntivirusObj, "Désactiver puis réactiver l'antivirus d'un PC (1/3)", "Turn a computer's antivirus off and back on (1/3)"),
                (CyberAudit.AuditedAction, CyberAudit.AntivirusObj, "Désactiver puis réactiver l'antivirus d'un autre PC (2/3)", "Turn another computer's antivirus off and back on (2/3)"),
                (CyberAudit.AuditedAction, CyberAudit.AntivirusObj, "Désactiver puis réactiver l'antivirus d'un autre PC (3/3)", "Turn another computer's antivirus off and back on (3/3)"),
            },
        },
        new CustomTask
        {
            // Comme l'Autographe-gate, mais la courbe doit être signée par le manager (Autograph.cs)
            // Pas pour le Manager (il signerait sa propre courbe), pas tirée sans manager
            Carrier = typeof(ExtinguishPumpkinTask), Key = Autograph.StonksKey, Role = PlayerRole.Specialist, NotForManager = true, RequiresManager = true,
            TitleFr = "STONKS", TitleEn = "STONKS",
            DescFr = "Les chiffres ne mentent jamais (enfin, presque)", DescEn = "Numbers never lie (well, almost)",
            Steps = new[]
            {
                ("Enter", "WorkStation" + MineSuffix, "Aller à ton poste de travail", "Go to your workstation"),
                (Windmill.DrewAction, Windmill.DrawingObj, "Dessiner dans Paint une belle courbe qui monte", "Draw a nice rising curve in Paint"),
                (Windmill.PrintedAction, Windmill.DrawingObj, "Imprimer la belle courbe", "Print the nice curve"),
                (Autograph.ManagerSignedAction, Windmill.DrawingObj, "Faire signer la courbe par le manager", "Get the manager to sign the curve"),
            },
        },
        new CustomTask
        {
            // "Lancer la sauvegarde" 5 s sur une baie, LED bleues ; 1 chance sur 4 de panne après (ServerBackup.cs)
            Carrier = typeof(LightPumpkinTask), Key = ServerBackup.Key, Role = PlayerRole.Specialist,
            TitleFr = "Sauvegarde des serveurs", TitleEn = "Server backup",
            DescFr = "Ctrl+S, version salle serveur", DescEn = "Ctrl+S, server room edition",
            Steps = new[]
            {
                (ServerBackup.BackedUpAction, ServerBackup.ServersObj, "Sauvegarder les serveurs (rester 5 s à côté d'une baie)", "Back up the servers (stay 5 s next to a rack)"),
            },
        },
        new CustomTask
        {
            // Tract FO : document du PC (bloc-notes), imprimé 3 fois puis déposé dans 3 bacs d'autres postes (UnionFlyer.cs)
            Carrier = typeof(ExtinguishPumpkinTask), Key = UnionFlyer.Key, Role = PlayerRole.Specialist,
            TitleFr = "Macron Explosion", TitleEn = "Macron Explosion",
            DescFr = "On lâche rien !", DescEn = "We won't back down!",
            Steps = new[]
            {
                (UnionFlyer.PrintedAction, UnionFlyer.FlyerObj, "Imprimer le tract FO (Tract_FO.txt sur ton PC) (1/3)", "Print the union flyer (Tract_FO.txt on your PC) (1/3)"),
                (UnionFlyer.PrintedAction, UnionFlyer.FlyerObj, "Imprimer le tract FO (2/3)", "Print the union flyer (2/3)"),
                (UnionFlyer.PrintedAction, UnionFlyer.FlyerObj, "Imprimer le tract FO (3/3)", "Print the union flyer (3/3)"),
                (UnionFlyer.DeliveredAction, UnionFlyer.FlyerObj, "Déposer un tract dans le bac d'un autre poste (1/3)", "Put a flyer in another desk's paper tray (1/3)"),
                (UnionFlyer.DeliveredAction, UnionFlyer.FlyerObj, "Déposer un tract dans le bac d'un autre poste (2/3)", "Put a flyer in another desk's paper tray (2/3)"),
                (UnionFlyer.DeliveredAction, UnionFlyer.FlyerObj, "Déposer un tract dans le bac d'un autre poste (3/3)", "Put a flyer in another desk's paper tray (3/3)"),
            },
        },
        new CustomTask
        {
            // 20 s seul avec le manager (ou l'assistant) dans son bureau (OneOnOne.cs) ; pas pour le manager, pas sans chef
            Carrier = typeof(LightPumpkinTask), Key = OneOnOne.Key, Role = PlayerRole.Specialist, NotForManager = true, RequiresManager = true,
            TitleFr = "1 to 1", TitleEn = "1 to 1",
            DescFr = "Un petit point, juste toi et moi", DescEn = "A quick catch-up, just you and me",
            Steps = new[]
            {
                (OneOnOne.MetAction, OneOnOne.ManagerObj, "Rester 20 s seul avec le manager (ou l'assistant) dans son bureau", "Spend 20 s alone with the manager (or the assistant) in their office"),
            },
        },
        new CustomTask
        {
            // L'équivalent côté chef : 20 s seul avec n'importe quel joueur dans son propre bureau (OneOnOne.cs)
            Carrier = typeof(ExtinguishPumpkinTask), Key = OneOnOne.ManagerKey, Role = PlayerRole.Manager, ForAssistant = true,
            TitleFr = "1 to 1 Manager", TitleEn = "1 to 1 Manager",
            DescFr = "Ma porte est toujours ouverte (sauf pendant le 1 to 1)", DescEn = "My door is always open (except during the 1 to 1)",
            Steps = new[]
            {
                (OneOnOne.MetAction, OneOnOne.EmployeeObj, "Faire un 1 to 1 de 20 s avec un collègue, seuls dans ton bureau", "Hold a 20 s 1 to 1 with a coworker, alone in your office"),
            },
        },
        new CustomTask
        {
            // Mail au fournisseur -> carton nommé à l'accueil 10 s après -> le ranger à l'entrepôt (SupplyOrder.cs)
            Carrier = typeof(LightPumpkinTask), Key = SupplyOrder.Key, Role = PlayerRole.Specialist,
            TitleFr = "Commande de fournitures", TitleEn = "Supply order",
            DescFr = "Faites le plein de fournitures", DescEn = "Stock up on supplies",
            Steps = new[]
            {
                (SupplyOrder.OrderedAction, SupplyOrder.BoxObj, "Commander les fournitures par mail", "Order the supplies by e-mail"),
                (SupplyOrder.ArrivedAction, SupplyOrder.BoxObj, "Attendre l'arrivée du colis", "Wait for the parcel to arrive"),
                (SupplyOrder.CollectedAction, SupplyOrder.BoxObj, "Ramasser le colis à l'accueil (ou en salle de réunion)", "Pick up the parcel at reception (or in the meeting room)"),
                (SupplyOrder.DeliveredAction, SupplyOrder.BoxObj, "Ranger le colis dans l'armoire de l'entrepôt", "Store the parcel in the warehouse cabinet"),
            },
        },
        new CustomTask
        {
            // Préparer au tableau blanc des réunions, puis présenter 20 s dans la salle de réunion (ClientPresentation.cs)
            Carrier = typeof(ExtinguishPumpkinTask), Key = ClientPresentation.Key, Role = PlayerRole.Specialist,
            TitleFr = "Présentation client", TitleEn = "Client presentation",
            DescFr = "Synergie, disruption et slides en Comic Sans", DescEn = "Synergy, disruption and Comic Sans slides",
            Steps = new[]
            {
                (ClientPresentation.PreparedAction, ClientPresentation.BoardObj, "Préparer la présentation au tableau blanc des réunions", "Prepare the presentation at the meeting whiteboard"),
                (ClientPresentation.PresentedAction, ClientPresentation.BoardObj, "Présenter 20 s dans la salle de réunion (sans en sortir)", "Present for 20 s in the meeting room (without leaving it)"),
            },
        },
        new CustomTask
        {
            // S'asseoir sur la photocopieuse (5 s), récupérer la photocopie, la punaiser (ButtCopy.cs)
            Carrier = typeof(LightPumpkinTask), Key = ButtCopy.Key, Role = PlayerRole.Slacker,
            TitleFr = "Photocopie de fesses", TitleEn = "Butt photocopy",
            DescFr = "Un classique des pots de départ", DescEn = "An office party classic",
            Steps = new[]
            {
                (ButtCopy.SatAction, ButtCopy.CopierObj, "S'asseoir sur la photocopieuse", "Sit on the photocopier"),
                ("Picked up", "Printed Image", "Récupérer la photocopie", "Grab the photocopy"),
                ("Placed", "Printed Image" + LocationSeparator + "Bulletin Board", "Punaiser la photocopie au panneau d'affichage", "Pin the photocopy on the bulletin board"),
            },
            // Les étapes "image imprimée" ne comptent qu'après s'être assis sur la machine
            AcceptHook = h => h.action == ButtCopy.SatAction || ButtCopy.Sat,
        },
        new CustomTask
        {
            // Piège sur l'agrafeuse d'un poste : s'asseoir ou toucher la gelée = doigt agrafé (StaplerTrap.cs)
            Carrier = typeof(ExtinguishPumpkinTask), Key = StaplerTrap.Key, Role = PlayerRole.Saboteur,
            TitleFr = "Agrafeuse piégée", TitleEn = "Booby-trapped stapler",
            DescFr = "Clac. Aïe.", DescEn = "Clack. Ouch.",
            Steps = new[]
            {
                (StaplerTrap.SabotagedAction, StaplerTrap.StaplerObj, "Saboter l'agrafeuse d'un poste de travail", "Sabotage a workstation's stapler"),
            },
        },
        new CustomTask
        {
            // Salle de pause : CD de Karl Casey dans la chaîne, puis 15 s de chant sans que le manager entre (Karaoke.cs)
            Carrier = typeof(LightPumpkinTask), Key = Karaoke.Key, Role = PlayerRole.Slacker,
            TitleFr = "Karaoké : Rise of the Machines", TitleEn = "Karaoke: Rise of the Machines",
            DescFr = "Les machines se lèvent, la salle de pause aussi", DescEn = "The machines rise, and so does the break room",
            Steps = new[]
            {
                (Karaoke.StartedAction, Karaoke.KaraokeObj, "Mettre le CD Rise of the Machines dans la chaîne de la salle de pause", "Put the Rise of the Machines CD in the break room stereo"),
                (Karaoke.SangAction, Karaoke.KaraokeObj, "Chanter 15 s dans la salle de pause sans que le manager débarque", "Sing for 15 s in the break room without the manager walking in"),
            },
        },
    };

    // Spam RH : 5 étapes identiques (chaque envoi à un nouveau collègue en valide une)
    static (string, string, string, string)[] ChainSteps()
    {
        var steps = new (string, string, string, string)[ChainMail.ChainCount];
        for (int i = 0; i < steps.Length; i++)
            steps[i] = (ChainMail.ChainedAction, ChainMail.ChainObj,
                $"Envoyer la chaîne à un collègue ({i + 1}/{steps.Length})", $"Send the chain mail to a coworker ({i + 1}/{steps.Length})");
        return steps;
    }

    internal const string IncidentKey = "dalemod.panneGenerale";
    internal const string MineSuffix = "@me";
    internal const char LocationSeparator = '|';

    // Plusieurs tâches custom peuvent partager un même support : chaque instance est rattachée
    // à sa définition au moment de l'assignation (position de l'entrée dans la liste des tâches).
    static readonly Dictionary<IntPtr, CustomTask> bound = new();
    static readonly Queue<CustomTask> pending = new();
    static readonly HashSet<string> warned = new();   // un seul avertissement de repli par type de tâche (instances temporaires)

    internal static bool IsActive(Task t, string key) => Find(t)?.Key == key;
    internal static string TitleOf(Task t) => Find(t)?.TitleFr;

    // Tâche active du joueur local. GetActiveTask du jeu lève une exception quand la liste est momentanément vide.
    internal static Task ActiveTask()
    {
        try { return DebugMenu.LocalTC?.GetActiveTask(); }
        catch { return null; }
    }
    internal static int Count => All.Length;

    static CustomTask Find(Task t)
    {
        if (t == null) return null;
        if (bound.TryGetValue(t.Pointer, out var d)) return d;
        var name = t.GetIl2CppType().Name;
        if (Array.Find(All, x => x.Carrier.Name == name) == null) return null;
        // 1. tâche en cours de création par InstantiateAndAssignTask(index) : définition exacte
        // 2. assignation du menu debug (PrepareAssign)
        // 3. repli (ne devrait plus arriver) : première définition de ce support, signalée dans le log
        if (assigning != null && assigning.Carrier.Name == name)
        {
            d = assigning;
            if (pending.Count > 0 && pending.Peek() == d) pending.Dequeue();   // file de secours alignée
        }
        else if (pending.Count > 0 && pending.Peek().Carrier.Name == name) d = pending.Dequeue();
        else
        {
            d = Array.Find(All, x => x.Carrier.Name == name);
            if (warned.Add(name)) Plugin.L.LogWarning($"[CUSTOM] tâche {name} sans définition connue : repli sur '{d.TitleFr}' (avertissement unique)");
        }
        bound[t.Pointer] = d;
        return d;
    }

    // Le jeu crée chaque tâche à partir de sa position dans la liste des tâches activées : on note la définition
    // custom correspondante le temps de la création (SetUpTask / Find sont appelés pendant cet appel).
    // Avant : une file remplie à la réception des listes (GiveNewTasks), y compris celles des AUTRES joueurs chez
    // l'hôte, et consommée dans le désordre -> mauvaises tâches affichées (ex. une bleue chez un Slacker).
    static CustomTask assigning;

    [HarmonyPatch(typeof(TaskController), nameof(TaskController.InstantiateAndAssignTask), new[] { typeof(int) })]
    [HarmonyPrefix]
    static void BeforeInstantiate(TaskController __instance, int taskIndex)
    {
        int count = __instance.GetEnabledTaskList().Count;
        int custom = taskIndex - (count - All.Length);
        assigning = custom >= 0 && custom < All.Length ? All[custom] : null;
    }

    [HarmonyPatch(typeof(TaskController), nameof(TaskController.InstantiateAndAssignTask), new[] { typeof(int) })]
    [HarmonyPostfix]
    static void AfterInstantiate() => assigning = null;

    // File de secours (si le jeu créait une tâche sans passer par InstantiateAndAssignTask(index)) :
    // uniquement les listes du joueur LOCAL, et remise à zéro à chaque nouvelle liste
    [HarmonyPatch(typeof(TaskController), nameof(TaskController.GiveNewTasks))]
    [HarmonyPrefix]
    static void BeforeGiveNewTasks(TaskController __instance, Il2CppSystem.Collections.Generic.List<int> taskIndices)
    {
        if (taskIndices == null || !__instance.isOwned) return;
        pending.Clear();
        int count = __instance.GetEnabledTaskList().Count;
        foreach (var i in taskIndices) PrepareAssign(i, count);
    }

    // Définition custom à cette position de la liste des tâches activées (null : tâche du jeu)
    internal static CustomTask CustomAt(int enabledIndex, int enabledCount)
    {
        int custom = enabledIndex - (enabledCount - All.Length);
        return custom >= 0 && custom < All.Length ? All[custom] : null;
    }

    internal static string CustomTitle(int enabledIndex, int enabledCount)
    {
        int custom = enabledIndex - (enabledCount - All.Length);
        return custom >= 0 && custom < All.Length ? All[custom].TitleFr : null;
    }

    // Aussi utilisé par le menu de debug, qui assigne une tâche par sa position dans la liste
    internal static void PrepareAssign(int enabledIndex, int enabledCount)
    {
        int custom = enabledIndex - (enabledCount - All.Length);
        if (custom >= 0 && custom < All.Length) pending.Enqueue(All[custom]);
    }

    internal static string Text(string fr, string en)
    {
        var lang = LocalizationManager.instance?.language?.ToString() ?? "";
        return lang.StartsWith("Fr", StringComparison.OrdinalIgnoreCase) ? fr : en;
    }

    // Patches propres à chaque support (SetUpTask / GetTaskName / GetTaskDescription)
    internal static void PatchCarriers(Harmony h)
    {
        foreach (var carrier in new HashSet<Type>(Array.ConvertAll(All, d => d.Carrier)))   // un seul patch par support partagé
        {
            var d = new { Carrier = carrier };
            h.Patch(AccessTools.Method(d.Carrier, "SetUpTask"), new HarmonyMethod(typeof(CustomTaskPatches), nameof(SetUpTask)));
            h.Patch(AccessTools.Method(d.Carrier, "GetTaskName"), new HarmonyMethod(typeof(CustomTaskPatches), nameof(GetTaskName)));
            h.Patch(AccessTools.Method(d.Carrier, "GetTaskDescription"), new HarmonyMethod(typeof(CustomTaskPatches), nameof(GetTaskDescription)));
        }
    }

    // --- Ajout à la liste des tâches tirables (même ajout chez tous les joueurs moddés) ---
    [HarmonyPatch(typeof(TaskController), nameof(TaskController.GetEnabledTaskList))]
    [HarmonyPostfix]
    static void AddToEnabled(TaskList __result)
    {
        if (__result == null || IndexOf(__result, All[0]) >= 0) return;   // déjà ajoutées
        foreach (var d in All) AddEntry(__result, d);
    }

    // CompleteTask cherche le rôle de la tâche dans fullTaskList : on y ajoute notre entrée
    // le temps de la validation seulement (fullTaskList passe aussi par le filtre des réglages du lobby).
    [HarmonyPatch(typeof(Task), nameof(Task.CompleteTask))]
    [HarmonyPrefix]
    static void BeforeComplete(Task __instance, out bool __state)
    {
        __state = false;
        var d = Find(__instance);
        var full = __instance.taskController?.fullTaskList;
        if (d == null || full == null || IndexOf(full, d) >= 0) return;
        AddEntry(full, d);
        __state = true;
    }

    [HarmonyPatch(typeof(Task), nameof(Task.CompleteTask))]
    [HarmonyFinalizer]
    static void AfterComplete(Task __instance, bool __state)
    {
        if (!__state) return;
        var d = Find(__instance);
        var full = __instance.taskController.fullTaskList;
        int i = IndexOf(full, d);
        if (i >= 0) full.RemoveAt(i);
        Plugin.L.LogInfo($"[CUSTOM] {d.TitleFr} validée");
    }

    [HarmonyPatch(typeof(Task), nameof(Task.OnTaskHookCommand))]
    [HarmonyPrefix]
    static bool FilterHook(Task __instance, TaskHook taskHook)
    {
        var d = Find(__instance);
        if (d?.AcceptHook == null || taskHook == null || d.AcceptHook(taskHook)) return true;
        Plugin.L.LogInfo($"[CUSTOM] {taskHook.action} {taskHook.passiveObjectName} ignoré (conditions non remplies)");
        return false;
    }

    [HarmonyPatch(typeof(Task), nameof(Task.GetPlayerRole))]
    [HarmonyPrefix]
    static bool GetPlayerRole(Task __instance, ref PlayerRole __result)
    {
        var d = Find(__instance);
        if (d == null) return true;
        // Rôle = pool de la tâche (couleur affichée : rouge pour une tâche Slacker chez un Specialist, etc.)
        __result = d.Role;
        return false;
    }

    static int IndexOf(TaskList list, CustomTask d)
    {
        var type = Il2CppType.From(d.Carrier);
        for (int i = 0; i < list.Count; i++)
            if (list[i].Item1.Equals(type)) return i;
        return -1;
    }

    static void AddEntry(TaskList list, CustomTask d)
    {
        var roles = new Il2CppSystem.Collections.Generic.List<PlayerRole>();
        roles.Add(d.Role);   // premier rôle = couleur (TaskPools, GetPlayerRole)
        // Tâches bleues : comme celles du jeu ("Specialist+Manager"), le Manager peut aussi les tirer (son tirage est
        // celui du jeu). Les sous-rôles Assistant / RH / Analyst sont des Specialists : déjà couverts.
        if (d.Role == PlayerRole.Specialist && !d.NotForManager) roles.Add(PlayerRole.Manager);
        var maps = new Il2CppSystem.Collections.Generic.List<Levels>();
        foreach (Levels l in Enum.GetValues(typeof(Levels))) maps.Add(l);
        var entry = new Il2CppSystem.ValueTuple<Il2CppSystem.Type, Il2CppSystem.Collections.Generic.List<PlayerRole>, bool, Il2CppSystem.Collections.Generic.List<Levels>>(Il2CppType.From(d.Carrier), roles, true, maps);
        AddStructNative(list.Pointer, entry.Pointer);
    }

    // List<struct>.Add appelé côté IL2CPP avec un pointeur vers le struct brut (l'interop le marshale mal)
    static unsafe void AddStructNative(IntPtr list, IntPtr boxedStruct)
    {
        var klass = IL2CPP.il2cpp_object_get_class(list);
        var add = IL2CPP.il2cpp_class_get_method_from_name(klass, "Add", 1);
        void** args = stackalloc void*[1];
        args[0] = (void*)IL2CPP.il2cpp_object_unbox(boxedStruct);
        IntPtr exc = IntPtr.Zero;
        IL2CPP.il2cpp_runtime_invoke(add, list, args, ref exc);
        Il2CppInterop.Runtime.Il2CppException.RaiseExceptionIfNecessary(exc);
    }

    // --- Contenu des tâches ---
    static readonly IntPtr baseSetUpTask = (IntPtr)Array.Find(
        typeof(Task).GetFields(BindingFlags.NonPublic | BindingFlags.Static),
        f => f.Name.StartsWith("NativeMethodInfoPtr_SetUpTask")).GetValue(null);

    static unsafe bool SetUpTask(Task __instance)
    {
        var d = Find(__instance);
        // Appel non virtuel de Task.SetUpTask (réinitialise l'état de la tâche)
        IntPtr exc = IntPtr.Zero;
        IL2CPP.il2cpp_runtime_invoke(baseSetUpTask, __instance.Pointer, (void**)IntPtr.Zero, ref exc);
        Il2CppInterop.Runtime.Il2CppException.RaiseExceptionIfNecessary(exc);
        if (__instance.subTasks == null)
            __instance.subTasks = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.ValueTuple<TaskHook, string, bool>>();

        foreach (var s in d.Steps)
        {
            // "objet@me" : objet appartenant au joueur local (ex. son bureau), comme les étapes "Use your computer" du jeu
            // "objet|lieu" : étape avec un lieu (ex. "|Bulletin Board" = n'importe quel document posé sur le panneau)
            string obj = s.obj, owner = null, location = null;
            int bar = obj.IndexOf(LocationSeparator);
            if (bar >= 0) { location = obj[(bar + 1)..]; obj = obj[..bar]; }
            if (obj.EndsWith(MineSuffix))
            {
                obj = obj[..^MineSuffix.Length];
                owner = LobbyManager.instance?.GetLocalPlayer()?.username;
            }
            var hook = new TaskHook(null, obj, owner, s.action, location, null);
            var step = new Il2CppSystem.ValueTuple<TaskHook, string, bool>(hook, Text(s.fr, s.en), false);
            AddStructNative(__instance.subTasks.Pointer, step.Pointer);
        }
        try { d.OnSetUp?.Invoke(__instance); }
        catch (Exception ex) { Plugin.L.LogError($"[CUSTOM] {d.TitleFr} : mise en place échouée : {ex}"); }
        Plugin.L.LogInfo($"[CUSTOM] {d.TitleFr} prête ({__instance.subTasks.Count} étapes)");
        return false;
    }

    static bool GetTaskName(Task __instance, ref string __result) { __result = Find(__instance).Key + ".title"; return false; }
    static bool GetTaskDescription(Task __instance, ref string __result) { __result = Find(__instance).Key + ".description"; return false; }

    // --- Textes : nos clés de localisation ---
    // Les deux surcharges GetLocalizedValue(key) et GetLocalizedValue(key, replacements)
    [HarmonyPatch]
    internal static class LocalizePatch
    {
        static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods() =>
            Array.FindAll(typeof(LocalizationManager).GetMethods(), m => m.Name == nameof(LocalizationManager.GetLocalizedValue));

        static bool Prefix(string key, ref string __result)
        {
            if (key == null || !key.StartsWith("dalemod.")) return true;
            foreach (var d in All)
            {
                if (key == d.Key + ".title") { __result = Text(d.TitleFr, d.TitleEn); return false; }
                if (key == d.Key + ".description") { __result = Text(d.DescFr, d.DescEn); return false; }
            }
            return true;
        }
    }
}
