using System;
using GameManagement;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Player.Lobby;
using Player.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DaleMod;

// Menu de debug (F7). Composant injecté dans IL2CPP, dessiné en IMGUI avec des Rect fixes
// (pas de GUILayout ni de GUI.Window, qui demandent des delegates/params mal supportés par l'interop).
public class DebugMenu : MonoBehaviour
{
    public DebugMenu(IntPtr ptr) : base(ptr) { }

    internal static TaskController LocalTC;   // renseigné par TaskController.Update (objet possédé localement)
    internal static bool BlockGameEnd;   // désactivé par défaut ; case du menu F7 (Debug) seulement

    bool open;
    int page;
    string status = "";
    CursorLockMode prevLock;
    bool prevVisible;

    const int PerPage = 14;
    // Toutes les valeurs des énumérations du jeu, sauf None : un rôle ajouté au jeu apparaît tout seul
    static readonly PlayerRole[] Roles = Array.FindAll((PlayerRole[])Enum.GetValues(typeof(PlayerRole)), r => r != PlayerRole.None);
    static readonly SubRole[] SubRoles = (SubRole[])Enum.GetValues(typeof(SubRole));

    void Update()
    {
        Perf.Frame();
        Perf.Run("ITEM", () => { ModItem.EnsureAllRegistered(); });
        Perf.Run("ASSETS", () => { ModAssets.TickLoads(); });
        Perf.Run("PC", () => { PcMod.Tick(); });
        Perf.Run("MAIL", () => { BossMail.Tick(); });
        Perf.Run("INK", () => { InkCoffee.Tick(); });
        Perf.Run("TUPP", () => { Tupperware.Tick(); });
        Perf.Run("INCIDENT", () => { Incident.Tick(); });
        Perf.Run("FIREX", () => { FireExPrank.Tick(); });
        Perf.Run("ZOOMER", () => { Zoomer.Tick(); });
        Perf.Run("BLOOD", () => { Blood.Tick(); });
        Perf.Run("SPLAT", () => { ScreenSplat.Tick(); });
        Perf.Run("CACA", () => { Caca.Tick(); });
        Perf.Run("PUFF", () => { Puffs.Tick(); });
        Perf.Run("AUTOGRAPH", () => { Autograph.Tick(); });
        Perf.Run("FRIGO", () => { FridgeCleanup.Tick(); });
        Perf.Run("SECU", () => { SafetyCheck.Tick(); });
        Perf.Run("CAFE", () => { CoffeeRound.Tick(); });
        Perf.Run("POPUP", () => { PopupSpam.Tick(); });
        Perf.Run("BACKUP", () => { ServerBackup.Tick(); });
        Perf.Run("TRACT", () => { UnionFlyer.Tick(); });
        Perf.Run("1TO1", () => { OneOnOne.Tick(); });
        Perf.Run("SUPPLY", () => { SupplyOrder.Tick(); });
        Perf.Run("PRES", () => { ClientPresentation.Tick(); });
        Perf.Run("STAPLER", () => { StaplerTrap.Tick(); });
        Perf.Run("PROUT", () => { WhoopeeCushion.Tick(); });
        Perf.Run("KARAOKE", () => { Karaoke.Tick(); });
        Perf.Run("RACUCH", () => { Racuch.Tick(); });
        Perf.Run("COSMETIC", () => { Cosmetics.Tick(); });
        Perf.Run("UI", () => { Hud.Tick(); UpdateBadge(); });
        Perf.Run("WATER", () => { WaterCup.Tick(); });
        Perf.Run("SHOWER", () => { ColdShower.Tick(); });
        Perf.Run("WATERPC", () => { WaterDamage.Tick(); });
        Perf.Run("NAP", () => { PowerNap.Tick(); });
        Perf.Run("CHAIN", () => { ChainMail.Tick(); });
        Perf.Run("ROLES", () => { RoleKit.Tick(); });
        Perf.Run("USURP", () => { Usurpateur.Tick(); });
        Perf.Run("CHANTEUR", () => { MaitreChanteur.Tick(); });
        Perf.Run("COSTKILLER", () => { CostKiller.Tick(); });
#if DEBUG
        Perf.Run("RECON", () => { CosmeticRecon.Tick(); });
#endif
#if DEBUG
        Perf.Run("REMOTE", () => { DevRemote.Tick(); });
#endif
#if DEBUG
        // Menu F7 : seulement dans la build Debug (la build Release est celle distribuée aux amis)
        var kb = Keyboard.current;
        if (kb == null || !kb.f7Key.wasPressedThisFrame) return;
        open = !open;
        if (open) { prevLock = Cursor.lockState; prevVisible = Cursor.visible; }
        else { Cursor.lockState = prevLock; Cursor.visible = prevVisible; }
#endif
    }

    // Badge du menu principal (seulement hors partie : ni client ni serveur réseau actifs) : le logo
    // Assets\zoomer_nobg.png collé en bas à droite, et "ZoomerMod vX.Y.Z" par-dessus, en police du jeu.
    // Les deux sur le canevas du mod (GameUi), le texte après le logo : il passe devant (un dessin IMGUI passerait
    // toujours par-dessus le canevas).
    static Texture2D logo;
    static bool logoTried;
    static UnityEngine.UI.RawImage badgeLogo;
    static TMPro.TextMeshProUGUI badgeText;

    static void UpdateBadge()
    {
        bool menu = !Mirror.NetworkClient.active && !Mirror.NetworkServer.active;
        if (!menu)
        {
            if (badgeLogo != null) badgeLogo.gameObject.SetActive(false);
            if (badgeText != null) badgeText.gameObject.SetActive(false);
            return;
        }
        if (!logoTried)
        {
            logoTried = true;
            try { logo = ModAssets.LoadTexture("zoomer_nobg.png"); if (logo != null) logo.hideFlags = HideFlags.DontUnloadUnusedAsset; }
            catch (Exception e) { Plugin.L.LogWarning("[BADGE] zoomer_nobg.png : " + e.Message); }
        }

        // Taille et place (pixels, origine en bas à droite de l'écran)
        float margin = Screen.height * 0.02f;
        float h = Screen.height * 0.18f, w = logo != null ? h * logo.width / logo.height : h;
        if (badgeLogo == null && logo != null)
        {
            var go = new GameObject("DaleMod_BadgeLogo");
            go.transform.SetParent(GameUi.Root(), false);
            badgeLogo = go.AddComponent<UnityEngine.UI.RawImage>();
            badgeLogo.texture = logo;
            badgeLogo.raycastTarget = false;
            var lrt = badgeLogo.rectTransform;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(1f, 0f);
        }
        if (badgeLogo != null)
        {
            badgeLogo.rectTransform.sizeDelta = new Vector2(w, h);
            badgeLogo.rectTransform.anchoredPosition = new Vector2(-margin, 0f);   // collé au bas de l'écran
            badgeLogo.gameObject.SetActive(true);
        }

        if (badgeText == null)
        {
            badgeText = GameUi.NewText("DaleMod_Badge");
            var rt = badgeText.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            badgeText.alignment = TMPro.TextAlignmentOptions.Bottom;
            badgeText.color = new Color(1f, 0.85f, 0.3f);
            badgeText.enableWordWrapping = false;
        }
        badgeText.transform.SetAsLastSibling();   // devant le logo
        GameUi.Style(badgeText);
        badgeText.text = $"{Plugin.Name} v{Plugin.Version}";
        badgeText.fontSize = Mathf.Max(16f, Screen.height / 40f);
        var rtt = badgeText.rectTransform;
        rtt.sizeDelta = new Vector2(w + 120f, badgeText.fontSize * 1.6f);
        rtt.anchoredPosition = new Vector2(-margin - w / 2f, badgeText.fontSize * 0.5f);
        badgeText.gameObject.SetActive(true);
    }

    void LateUpdate()
    {
        Perf.Run("SRV", () => { ServerBlink.LateTick(); });
        Perf.Run("ZOOMER", () => { Zoomer.LateTick(); });
        Perf.Run("WATERPC", () => { WaterDamage.LateTick(); });
        Perf.Run("BUTT", () => { ButtCopy.LateTick(); });
        Perf.Run("STAPLER", () => { StaplerTrap.LateTick(); });
    }

    void OnGUI()
    {
        long t0 = Perf.Start();
        Nausea.DrawOverlay();
        FireExPrank.DrawOverlay();
        ColdShower.DrawOverlay();
        PowerNap.DrawOverlay();
        Perf.Stop("GUI", t0);
        if (!open) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        float x = 20, y = 20, w = 560;
        GUI.Box(new Rect(x - 10, y - 10, w + 20, 880), "DaleMod — debug (F7)");
        y += 20;

        // Avant la partie aussi (dans le lobby) : réglage lu par l'hôte au tirage du Jester
        GUI.Label(new Rect(x, y, 300, 22), "Rôles bonus au prochain tirage (avant la partie) :");
        if (GUI.Button(new Rect(x + 300, y, 250, 22), RoleKit.ModeLabel())) RoleKit.NextMode();
        y += 26;
        var tc = LocalTC;
        if (tc == null) { GUI.Label(new Rect(x, y, w, 22), "Pas de partie en cours (TaskController local introuvable)"); return; }

        // --- Tâche active ---
        var active = CustomTaskPatches.ActiveTask();
        GUI.Label(new Rect(x, y, w, 22), "Tâche active : " + (active == null ? "aucune" : CustomTaskPatches.TitleOf(active) ?? active.GetIl2CppType().Name)); y += 24;
        if (GUI.Button(new Rect(x, y, 180, 24), "Valider l'étape en cours")) Run(() => ValidateStep(tc));
        if (GUI.Button(new Rect(x + 190, y, 180, 24), "Terminer la tâche")) Run(() => { tc.GetActiveTask()?.CompleteTask(); return "Tâche terminée"; });
        if (GUI.Button(new Rect(x + 380, y, 180, 24), "Retirer les tâches")) Run(() => { tc.ClearAllTasks(); return "Tâches retirées"; });
        y += 34;

        // --- Liste des tâches assignables ---
        var list = tc.GetEnabledTaskList();
        int pages = Math.Max(1, (list.Count + PerPage - 1) / PerPage);
        page = Math.Clamp(page, 0, pages - 1);
        GUI.Label(new Rect(x, y, 300, 22), $"Assigner une tâche ({list.Count}) — page {page + 1}/{pages}");
        if (GUI.Button(new Rect(x + 400, y, 70, 22), "<")) page--;
        if (GUI.Button(new Rect(x + 480, y, 70, 22), ">")) page++;
        y += 26;
        // Cible : moi, tout le monde, ou un joueur (les autres joueurs : hôte uniquement)
        var targets = Targets(tc);
        target = ((target % targets.Count) + targets.Count) % targets.Count;
        GUI.Label(new Rect(x, y, 60, 22), "Pour :");
        if (GUI.Button(new Rect(x + 60, y, 30, 22), "<")) target--;
        GUI.Box(new Rect(x + 95, y, 260, 22), targets[target].label);
        if (GUI.Button(new Rect(x + 360, y, 30, 22), ">")) target++;
        var chosen = targets[target];
        y += 26;
        // Tâches custom en tête (elles sont à la fin de la liste du jeu) ; l'index réel reste celui de la liste
        int customStart = list.Count - CustomTaskPatches.Count;
        for (int i = page * PerPage; i < Math.Min(list.Count, (page + 1) * PerPage); i++)
        {
            int index = i < CustomTaskPatches.Count ? customStart + i : i - CustomTaskPatches.Count, count = list.Count;
            var type = list[index].Item1;
            int col = (i - page * PerPage) % 2, row = (i - page * PerPage) / 2;
            string label = CustomTaskPatches.CustomTitle(index, count) is string t ? t + " (custom)" : type.Name.Replace("Task", "");
            if (GUI.Button(new Rect(x + col * 280, y + row * 26, 270, 24), label))
                Run(() => Assign(tc, chosen, index, count, type, label));
        }
        y += (PerPage / 2) * 26 + 10;

        // --- Rôle et sous-rôle, pour la même cible que les tâches ("Pour :") ---
        var shown = chosen.everyone ? null : chosen.tc.GetComponent<LobbyPlayer>();
        GUI.Label(new Rect(x, y, w, 22), $"Rôle de {chosen.label} : " +
            (shown == null ? "(plusieurs joueurs)" : $"{shown.playerRole} / {shown.subRole}") + "   (hôte uniquement)");
        y += 24;
        for (int i = 0; i < Roles.Length; i++)
        {
            var role = Roles[i];
            if (GUI.Button(new Rect(x + i * 112, y, 106, 24), role.ToString()))
                Run(() => SetRole(tc, chosen, lp => lp.ServerSetPlayerRole(role), "Rôle : " + role));
        }
        y += 28;
        // Sous-rôles de Specialist (Assistant / RH / Analyst)
        GUI.Label(new Rect(x, y, 110, 22), "Sous-rôle :");
        for (int i = 0; i < SubRoles.Length; i++)
        {
            var sub = SubRoles[i];
            if (GUI.Button(new Rect(x + 120 + i * 110, y, 104, 24), sub.ToString()))
                Run(() => SetRole(tc, chosen, lp => lp.ServerSetSubRole(sub, true), "Sous-rôle : " + sub));
        }
        // Jester (sous-rôle caché ZoomerMod) : forcé sans jamais être affiché dans ce menu
        if (GUI.Button(new Rect(x + 120 + SubRoles.Length * 110, y, 104, 24), "Jester"))
            Run(() => SetRole(tc, chosen, Jester.Force, "Jester (caché)"));
        y += 28;
        if (GUI.Button(new Rect(x + 120, y, 140, 24), "Syndicaliste"))
            Run(() =>
            {
                string err = null;
                var r = SetRole(tc, chosen, lp => err ??= Syndic.Force(lp), "Syndicaliste");
                return err ?? r;
            });
        if (GUI.Button(new Rect(x + 270, y, 104, 24), "Rival"))
            Run(() =>
            {
                string err = null;
                var r = SetRole(tc, chosen, lp => err ??= Rival.Force(lp), "Rival (cible au hasard)");
                return err ?? r;
            });
        // Test solo de la conversion : comme si la cible avait quitté la partie
        if (GUI.Button(new Rect(x + 384, y, 150, 24), "Rival → Jester")) Run(() => Rival.ForceConvert() ?? "Rival devenu Jester");
        y += 28;
        if (GUI.Button(new Rect(x + 120, y, 140, 24), "Pistonné"))
            Run(() =>
            {
                string err = null;
                var r = SetRole(tc, chosen, lp => err ??= Pistonne.Force(lp), "Pistonné");
                return err ?? r;
            });
        if (GUI.Button(new Rect(x + 270, y, 104, 24), "Usurpateur"))
            Run(() =>
            {
                string err = null;
                var r = SetRole(tc, chosen, lp => err ??= Usurpateur.Force(lp), "Usurpateur");
                return err ?? r;
            });
        // Test solo : une apparence au hasard à la place d'un autre joueur
        if (GUI.Button(new Rect(x + 384, y, 170, 24), "Mémoriser au hasard")) Run(Usurpateur.DebugMemorizeRandom);
        y += 28;
        if (GUI.Button(new Rect(x + 120, y, 140, 24), "Maître chanteur"))
            Run(() =>
            {
                string err = null;
                var r = SetRole(tc, chosen, lp => err ??= MaitreChanteur.Force(lp), "Maître chanteur");
                return err ?? r;
            });
        // Test solo : se bâillonner soi-même à la prochaine réunion (chat bloqué)
        if (GUI.Button(new Rect(x + 270, y, 170, 24), "Me bâillonner")) Run(MaitreChanteur.DebugGagSelf);
        if (GUI.Button(new Rect(x + 450, y, 170, 24), "Mannequin (E)")) Run(RoleKey.ToggleDummy);
        y += 28;
        if (GUI.Button(new Rect(x + 120, y, 140, 24), "Cost Killer"))
            Run(() =>
            {
                string err = null;
                var r = SetRole(tc, chosen, lp => err ??= CostKiller.Force(lp), "Cost Killer");
                return err ?? r;
            });
        y += 34;

        // --- Partie ---
        BlockGameEnd = GUI.Toggle(new Rect(x, y, 260, 22), BlockGameEnd, " Bloquer la fin de partie");
        if (GUI.Button(new Rect(x + 270, y, 140, 24), "Lancer un meeting")) Run(() => { GameManager.instance.ServerCallMeeting(); return "Meeting lancé"; });
        // Licenciement du joueur choisi ("Pour :"), comme depuis le tableau blanc (test du Jester : se virer soi-même)
        if (GUI.Button(new Rect(x + 420, y, 130, 24), "Virer")) Run(() => SetRole(tc, chosen, lp => GameManager.instance.ServerFirePlayer(lp.netIdentity, false, true), "Viré"));
        y += 30;
        if (GUI.Button(new Rect(x, y, 180, 24), "Panne générale")) Run(Incident.Start);
        if (GUI.Button(new Rect(x + 190, y, 180, 24), "Réparer la panne")) Run(Incident.Clear);
        GUI.Label(new Rect(x + 380, y, 180, 24), Incident.Active ? "Panne : EN COURS" : "Panne : non");
        y += 28;
        if (GUI.Button(new Rect(x, y, 180, 24), "Serveurs : couper/rétablir")) Run(ServerProbe.ToggleAll);
        if (GUI.Button(new Rect(x + 190, y, 180, 24), "Instantané serveur visé")) Run(ServerProbe.Snapshot);
        if (GUI.Button(new Rect(x + 380, y, 180, 24), "Limites des objets")) Run(ModItem.LogLimits);
        y += 28;
        if (GUI.Button(new Rect(x, y, 180, 24), "Objets de la cuisine")) Run(ModItem.LogKitchen);
        y += 28;
        if (GUI.Button(new Rect(x, y, 180, 24), "M'asperger (test solo)")) Run(FireExPrank.TestSelf);
        if (GUI.Button(new Rect(x + 190, y, 180, 24), "Saboter le broyeur")) Run(Zoomer.TestArm);
        if (GUI.Button(new Rect(x + 380, y, 180, 24), "Main coincée (test)")) Run(Zoomer.TestTrapSelf);
        y += 28;
        if (GUI.Button(new Rect(x, y, 180, 24), "Me doucher (test solo)")) Run(ColdShower.TestSelf);
        if (GUI.Button(new Rect(x + 190, y, 180, 24), "Court-circuit PC proche")) Run(WaterDamage.TestNearest);
        if (GUI.Button(new Rect(x + 380, y, 180, 24), "Pluie de popups")) Run(PopupSpam.Test);
        y += 30;

        // --- Sachet ---
        if (GUI.Button(new Rect(x, y, 180, 24), "Sachet devant moi")) Run(() =>
        {
            var cam = Camera.main;
            return cam == null ? "Caméra introuvable" : CokeBag.Spawn(cam.transform.position + cam.transform.forward * 1.2f);
        });
        if (GUI.Button(new Rect(x + 190, y, 180, 24), "Sachets aux toilettes")) Run(CokeBag.SpawnInToilets);
        if (GUI.Button(new Rect(x + 380, y, 180, 24), "Inspecter l'objet visé")) Run(CokeBag.InspectAimed);
        y += 28;
        if (GUI.Button(new Rect(x, y, 180, 24), "Diagnostic indicateur")) Run(TaskHookObjectsPatches.Diagnose);
        if (GUI.Button(new Rect(x + 190, y, 180, 24), "Me rendre nauséeux")) Run(Nausea.ApplyToSelf);
        if (GUI.Button(new Rect(x + 380, y, 180, 24), "Inspecter mon PC")) Run(PcMod.Inspect);
        y += 28;
        CokeBag.RequireToilets = GUI.Toggle(new Rect(x, y, 380, 22), CokeBag.RequireToilets, " Rail de bureau : sniffer uniquement aux toilettes");
        y += 24;
        BossMail.OwnPcCounts = GUI.Toggle(new Rect(x, y, 420, 22), BossMail.OwnPcCounts, " Lettre au boss : mon propre PC compte (test solo)");
        y += 30;

        // --- Mode triche des devs ---
        var cm = CheatManager.instance;
        if (cm != null)
        {
            GUI.Label(new Rect(x, y, 380, 22), $"Mode triche des devs : {(cm.isCheatMode ? "ON" : "off")}  (mot de passe : '{cm.password}')");
            if (GUI.Button(new Rect(x + 390, y, 170, 24), "Basculer")) Run(() => { cm.isCheatMode = !cm.isCheatMode; return "Mode triche : " + cm.isCheatMode; });
            y += 30;
        }

        GUI.Label(new Rect(x, y, w, 44), status);
    }

    // --- Assignation à d'autres joueurs ---
    static int target;
    class Target
    {
        public readonly string label; public readonly TaskController tc; public readonly bool everyone;
        public Target(string label, TaskController tc, bool everyone) { this.label = label; this.tc = tc; this.everyone = everyone; }
    }

    static System.Collections.Generic.List<Target> Targets(TaskController me)
    {
        var l = new System.Collections.Generic.List<Target> { new("Moi", me, false) };
        if (!Mirror.NetworkServer.active) return l;   // donner une tâche à un autre joueur : seul le serveur peut
        l.Add(new("Tout le monde", null, true));
        foreach (var t in UnityEngine.Object.FindObjectsOfType<TaskController>())
            if (t.Pointer != me.Pointer) l.Add(new(t.GetComponent<LobbyPlayer>()?.username ?? t.name, t, false));
        return l;
    }

    static string Assign(TaskController me, Target to, int index, int count, Il2CppSystem.Type type, string label)
    {
        int n = 0;
        if (to.everyone || to.tc.Pointer == me.Pointer)
        {
            me.ClearAllTasks();
            CustomTaskPatches.PrepareAssign(index, count);
            me.InstantiateAndAssignTask(type);
            n++;
        }
        if (to.everyone || to.tc.Pointer != me.Pointer)
            foreach (var t in UnityEngine.Object.FindObjectsOfType<TaskController>())
            {
                if (t.Pointer == me.Pointer || (!to.everyone && t.Pointer != to.tc.Pointer) || t.connectionToClient == null) continue;
                var indices = new Il2CppSystem.Collections.Generic.List<int>();
                indices.Add(index);
                TaskPools.Bypass = true;   // tâche imposée : pas de tirage par pools
                try { t.RpcGiveNewTasks(t.connectionToClient, indices); }
                finally { TaskPools.Bypass = false; }
                n++;
            }
        return $"Assignée à {to.label} ({n} joueur(s)) : {label}";
    }

    // Rôle / sous-rôle pour la cible choisie (serveur : ServerSetPlayerRole / ServerSetSubRole marchent pour n'importe
    // quel joueur). Le jeu redonne lui-même des tâches adaptées au nouveau rôle ; on vide les miennes comme avant.
    static string SetRole(TaskController me, Target to, Action<LobbyPlayer> apply, string what)
    {
        if (!Mirror.NetworkServer.active) return "Hôte uniquement";
        int n = 0;
        foreach (var t in UnityEngine.Object.FindObjectsOfType<TaskController>())
        {
            if (!to.everyone && t.Pointer != to.tc.Pointer) continue;
            var lp = t.GetComponent<LobbyPlayer>();
            if (lp == null) continue;
            apply(lp);
            if (t.Pointer == me.Pointer) me.ClearAllTasks();
            n++;
        }
        return $"{what} pour {to.label} ({n} joueur(s))";
    }

    static string ValidateStep(TaskController tc)
    {
        // currentTaskHook = première étape non faite de la tâche active : on la rejoue comme si le joueur l'avait faite
        tc.CheckForTaskCompletion();
        var hook = tc.currentTaskHook;
        if (hook == null) return "Aucune étape en attente";
        TaskHook.TriggerTaskHookCommandStatic(hook);
        return $"Étape validée : {hook.action} {hook.passiveObjectName}";
    }


    void Run(Func<string> action)
    {
        try { status = action(); }
        catch (Exception ex) { status = "Erreur : " + ex.Message; Plugin.L.LogError("[DEBUG] " + ex); }
        Plugin.L.LogInfo("[DEBUG] " + status);
    }
}

static class DebugMenuPatches
{
    // Mémorise le TaskController du joueur local (il est sur le LobbyPlayer, isOwned côté hôte comme client)
    [HarmonyPatch(typeof(TaskController), nameof(TaskController.Update))]
    [HarmonyPostfix]
    static void TrackLocal(TaskController __instance)
    {
        if (__instance.isOwned) DebugMenu.LocalTC = __instance;
    }

    // Empêche la partie de se terminer pendant les tests (changement de rôle, solo…)
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.IsGameFinished))]
    [HarmonyPrefix]
    static bool NoGameEnd(ref bool __result)
    {
#if !DEBUG
        return true;   // version distribuée : jamais de blocage
#endif
        if (!DebugMenu.BlockGameEnd) return true;
        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.EndGameIfFinished))]
    [HarmonyPrefix]
    static bool NoGameEndCheck()
    {
#if !DEBUG
        return true;
#endif
        return !DebugMenu.BlockGameEnd;
    }
}
