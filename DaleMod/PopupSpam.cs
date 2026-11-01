using System;
using System.Collections.Generic;
using System.IO;
using Computer.Scripts.System;
using Il2CppSystem.FileSystemInstances;
using Mirror;
using Objects.Scripts;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DaleMod;

// Pluie de popups façon pubs / arnaques, en VRAIES fenêtres du PC (comme MeowTube ou le bloc-notes) :
// chaque popup est une fenêtre du bloc-notes créée directement dans la zone des fenêtres du PC, renommée "Publicité",
// avec une bannière (Assets\popups\*.png, chacune au plus une fois) ou un fond coloré et un faux message pour compléter. On la ferme avec la croix
// de la fenêtre. Réutilisable : PopupSpam.Request(pc) (Audit Cybersécurité, et plus tard une tâche bonus).
// Visible par tous : le serveur demande à chaque client d'ouvrir les popups sur ce PC ; positions, tailles et bannières
// viennent d'un tirage à graine identique partout, et les clics du PC sont rejoués chez tous (fermetures comprises).
static class PopupSpam
{
    const int Count = 10;
    const float SpamUp = -91.1f, Spam = -91.2f;
    const string TemplateApp = "NotePad.exe";

    static readonly (string fr, string en)[] Messages =
    {
        ("FÉLICITATIONS !!!\nVous êtes le 1 000 000e visiteur", "CONGRATULATIONS!!!\nYou are visitor #1,000,000"),
        ("ALERTE\n37 virus détectés sur votre PC", "WARNING\n37 viruses detected on your PC"),
        ("Des célibataires près de chez vous", "Hot singles in your area"),
        ("TÉLÉCHARGEZ PLUS DE RAM", "DOWNLOAD MORE RAM"),
        ("Votre colis est bloqué en douane", "Your parcel is stuck at customs"),
        ("Le manager le déteste :\nson astuce pour dormir au travail", "Managers hate him:\none weird trick to nap at work"),
        ("Gagnez un iPhone en 1 clic", "Win an iPhone in 1 click"),
        ("Votre abonnement GlucksTube a expiré", "Your GlucksTube subscription has expired"),
    };

    internal static void Register()
    {
        NetSignal.OnServer(SpamUp, (it, _) => NetSignal.Send(it, Spam));
        NetSignal.On(Spam, it =>
        {
            var ws = it.TryCast<WorkStationController>();
            if (ws?.computerController != null) Spawn(ws.computerController);
        });
    }

    // Demande (depuis n'importe quel client) : popups sur ce PC, chez tout le monde
    internal static void Request(ComputerController pc)
    {
        var ws = pc?._workStationController;
        if (ws == null) { Plugin.L.LogWarning("[POPUP] poste du PC introuvable"); return; }
        if (NetworkServer.active) NetSignal.Send(ws, Spam);
        else NetSignal.SendToServer(ws, SpamUp);
    }

    // --- Ouverture des fenêtres (chez chaque client) ---
    static readonly Dictionary<uint, int> spawned = new();   // PC -> nombre de pluies déjà ouvertes (graine)
    class Pending { public ComputerController pc; public string name; public Rect rect; public Color color; public string message; public Sprite banner; public int tries; public GameObject window; }
    static readonly List<Pending> pending = new();
    static List<Sprite> banners;

    static void Spawn(ComputerController pc)
    {
        var wm = FindWindowManager(pc);
        var template = FindTemplate(pc);
        if (wm == null || template == null)
        {
            Plugin.L.LogWarning($"[POPUP] introuvable : {(wm == null ? "gestionnaire de fenêtres " : "")}{(template == null ? TemplateApp : "")}");
            return;
        }
        LoadBanners();
        int round = spawned.TryGetValue(pc.netId, out var n) ? n : 0;
        spawned[pc.netId] = round + 1;
        var rnd = new System.Random((int)pc.netId * 7919 + round);   // même tirage chez tous les joueurs

        // Contenus : chaque bannière au plus une fois (10 tirées au hasard s'il y en a plus), le reste en pubs texte
        // aux messages différents ; puis l'ordre est mélangé (positions au hasard)
        var bannerPicks = Shuffled(banners.Count, rnd);
        var messagePicks = Shuffled(Messages.Length, rnd);
        var slots = new List<int>();   // >= 0 : bannière ; < 0 : pub texte n° (-1 - k)
        for (int k = 0; k < Count; k++) slots.Add(k < bannerPicks.Count ? bannerPicks[k] : -1 - (k - bannerPicks.Count));
        slots = Shuffled(slots, rnd);

        for (int i = 0; i < Count; i++)
        {
            int slot = slots[i];
            var (fr, en) = Messages[slot >= 0 ? 0 : messagePicks[(-1 - slot) % messagePicks.Count]];
            var p = new Pending
            {
                pc = pc, name = $"Pub{pc.netId}_{round}_{i}",
                rect = new Rect((float)rnd.NextDouble(), (float)rnd.NextDouble(), 0.32f + (float)rnd.NextDouble() * 0.16f, 0.28f + (float)rnd.NextDouble() * 0.14f),
                color = Color.HSVToRGB((float)rnd.NextDouble(), 0.55f, 0.95f),
                message = CustomTaskPatches.Text(fr, en),
                banner = slot >= 0 ? banners[slot] : null,
            };
            // Fenêtre créée directement dans la zone des fenêtres du PC, comme le fait LaunchExecutable, mais sans sa
            // limite d'une fenêtre par type d'app (qui n'en ouvrait qu'une)
            try
            {
                var go = Object.Instantiate(template.gameObject, wm.windowParent);
                go.name = p.name;
                go.transform.SetAsLastSibling();   // au premier plan
                var exe = go.GetComponent<Executable>();
                if (exe != null) exe.fileName = p.name;
                p.window = go;
            }
            catch (Exception e) { Plugin.L.LogError($"[POPUP] création : {e.Message}"); }
            if (i == 0) Plugin.L.LogInfo($"[POPUP] diag : zone '{wm.windowParent?.name}' enfants={wm.windowParent?.childCount}, fenêtre={(p.window == null ? "aucune" : p.window.name)}");
            pending.Add(p);
        }
        Plugin.L.LogInfo($"[POPUP] {Count} popups ouvertes sur le PC de {pc.user?.username ?? "?"} (tirage {round})");
    }

    // Le gestionnaire de fenêtres de ce PC : dans le PC, sinon sous son écran (canvas), sinon parmi tous ceux de la scène
    static Windows.WindowManager FindWindowManager(ComputerController pc)
    {
        var wm = pc.GetComponentInChildren<Windows.WindowManager>(true);
        if (wm == null && pc.canvas != null) wm = pc.canvas.GetComponentInChildren<Windows.WindowManager>(true);
        if (wm != null) return wm;
        foreach (var w in Resources.FindObjectsOfTypeAll<Windows.WindowManager>())
        {
            var owner = w.GetComponentInParent<ComputerController>(true);
            if (owner != null && owner.Pointer == pc.Pointer) return w;
        }
        // Repli : le gestionnaire d'une fenêtre déjà ouverte sur ce PC
        foreach (var win in pc.GetComponentsInChildren<Window.ApplicationWindow>(true))
            if (win.windowManager != null) return win.windowManager;
        return null;
    }

    // L'app modèle (bloc-notes) parmi les apps du PC, quelle que soit l'écriture de son nom
    static Executable FindTemplate(ComputerController pc)
    {
        var exe = pc.FindExecutable(TemplateApp);
        if (exe != null) return exe;
        if (pc.executables != null)
            foreach (var e in pc.executables)
                if (e != null && (e.fileName ?? "").IndexOf("notepad", StringComparison.OrdinalIgnoreCase) >= 0) return e;
        return null;
    }

    // Les fenêtres apparaissent dans la frame : on les habille dès qu'on les trouve (et on réapplique un moment)
    internal static void Tick()
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var p = pending[i];
            if (p.pc == null || ++p.tries > 120) { pending.RemoveAt(i); continue; }
            if (p.window == null) { pending.RemoveAt(i); continue; }
            var exe = p.window.GetComponentInChildren<Executable>(true);
            var win = p.window.GetComponentInChildren<Window.ApplicationWindow>(true) ?? exe?.applicationWindow;
            if (win == null) { if (p.tries == 30) Plugin.L.LogWarning($"[POPUP] {p.name} : cadre de fenêtre introuvable"); continue; }
            if (p.tries == 1) Plugin.L.LogInfo($"[POPUP] {p.name} : fenêtre '{win.name}' trouvée, habillage");
            Dress(exe, win, p);
            if (p.tries > 10) pending.RemoveAt(i);   // quelques frames de réapplication (le jeu initialise la fenêtre)
        }
    }

    static void Dress(Executable exe, Window.ApplicationWindow win, Pending p)
    {
        var title = CustomTaskPatches.Text("Publicité", "Advertisement");
        win.windowTitle = title;
        if (win.title != null) win.title.text = title;

        // Taille et position : fractions de la zone des fenêtres (même résultat chez tous)
        // Taille et position : fractions de l'ÉCRAN du PC (pc.canvas). La zone des fenêtres n'est qu'un conteneur
        // de 100 x 100 : on convertit la position de l'écran vers ce conteneur.
        var rt = win.GetComponent<RectTransform>();
        var screen = p.pc.canvas;
        if (rt != null && screen != null)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);   // sizeDelta = vraie taille
            rt.pivot = new Vector2(0.5f, 0.5f);
            var size = screen.rect.size;
            var wsz = new Vector2(size.x * p.rect.width, size.y * p.rect.height);
            if (p.banner != null && win.content != null)
            {
                // Hauteur adaptée à la bannière (pas d'image écrasée) : contenu = largeur / proportions, + barre de titre
                float bar = Mathf.Max(0f, rt.rect.height - win.content.rect.height);
                var r = p.banner.rect;
                wsz.y = Mathf.Min(size.y * 0.85f, wsz.x * r.height / r.width + bar);
            }
            rt.sizeDelta = wsz;
            // Centre de la fenêtre dans l'écran (repère local de l'écran, origine au centre), puis dans le conteneur
            var local = new Vector2((p.rect.x - 0.5f) * (size.x - wsz.x), (p.rect.y - 0.5f) * (size.y - wsz.y)) + screen.rect.center;
            var world = screen.TransformPoint(local);
            var parent = rt.parent as RectTransform ?? rt.parent?.GetComponent<RectTransform>();
            rt.anchoredPosition = parent != null ? (Vector2)parent.InverseTransformPoint(world) - parent.rect.center : (Vector2)local;
        }

        // Contenu : bannière, ou fond coloré + message (le texte du bloc-notes, agrandi et centré)
        var content = win.content;
        if (content == null) return;
        var bg = content.Find("DaleMod_Banner");
        if (bg == null)
        {
            var go = new GameObject("DaleMod_Banner");
            go.transform.SetParent(content, false);
            var brt = go.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            go.AddComponent<Image>();
            bg = go.transform;
        }
        var img = bg.GetComponent<Image>();

        // Rien du bloc-notes ne doit rester : menu du haut (bouton Print…) et tout le contenu, sauf notre pub
        if (win.topMenu != null) win.topMenu.SetActive(false);
        for (int c = 0; c < content.childCount; c++)
        {
            var child = content.GetChild(c);
            if (child != bg) child.gameObject.SetActive(false);
        }
        bg.SetAsLastSibling();

        var label = bg.Find("DaleMod_Text");
        if (p.banner != null)
        {
            img.sprite = p.banner; img.color = Color.white; img.preserveAspect = false;
            if (label != null) label.gameObject.SetActive(false);
            return;
        }

        // Pub texte : fond coloré + notre propre texte (police du titre de la fenêtre), centré, taille auto
        img.sprite = null; img.color = p.color;
        if (label == null)
        {
            var go = new GameObject("DaleMod_Text");
            go.transform.SetParent(bg, false);
            var lrt = go.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16, 12); lrt.offsetMax = new Vector2(-16, -12);
            go.AddComponent<TMPro.TextMeshProUGUI>();
            label = go.transform;
        }
        var tmp = label.GetComponent<TMPro.TextMeshProUGUI>();
        if (win.title != null && win.title.font != null) tmp.font = win.title.font;
        tmp.text = p.message;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.fontStyle = TMPro.FontStyles.Bold;
        tmp.color = Color.white;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 10; tmp.fontSizeMax = 72;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        label.gameObject.SetActive(true);
    }

    // Indices 0..n-1 (ou liste) dans un ordre aléatoire, avec le tirage donné (identique chez tous)
    static List<int> Shuffled(int n, System.Random rnd)
    {
        var l = new List<int>();
        for (int i = 0; i < n; i++) l.Add(i);
        return Shuffled(l, rnd);
    }

    static List<int> Shuffled(List<int> l, System.Random rnd)
    {
        for (int i = l.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); }
        return l;
    }

    static void LoadBanners()
    {
        if (banners != null) return;
        banners = new List<Sprite>();
        var dir = Path.Combine(ModAssets.Dir, "popups");
        if (!Directory.Exists(dir)) return;
        var files = Directory.GetFiles(dir, "*.png");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);   // même ordre chez tous (même tirage)
        foreach (var f in files)
            try
            {
                var s = ModAssets.LoadSprite(Path.Combine("popups", Path.GetFileName(f)));
                if (s != null) banners.Add(s);
            }
            catch (Exception e) { Plugin.L.LogWarning($"[POPUP] bannière {f} : {e.Message}"); }
        Plugin.L.LogInfo($"[POPUP] {banners.Count} bannière(s) chargée(s)");
    }

    // --- Événement aléatoire : quand le jeu infecte un PC dont l'antivirus est désactivé (mêmes conditions que son
    // virus), 1 chance sur 2 que ce soit une pluie de popups à la place. Puis ce PC est tranquille un moment
    // (le jeu retente l'infection : sans ce délai, la pluie repartirait en boucle).
    internal static bool BypassVirus;   // Panne générale : virus imposé sur tous les PC, pas de tirage
    const float PopupInsteadOfVirus = 0.5f, CalmSeconds = 60f;
    static readonly Dictionary<uint, float> calmUntil = new();

    [HarmonyLib.HarmonyPatch(typeof(VirusController), nameof(VirusController.ServerSetVirus))]
    [HarmonyLib.HarmonyPrefix]
    static bool MaybePopupsInstead(VirusController __instance, bool state)
    {
        if (!state || BypassVirus || !NetworkServer.active || __instance.NetworkisFirewallActive) return true;
        var pc = __instance.computerController;
        if (pc == null) return true;
        if (calmUntil.TryGetValue(pc.netId, out var t) && Time.time < t) return false;   // pluie toute récente
        if (UnityEngine.Random.value >= PopupInsteadOfVirus) return true;                // le vrai virus du jeu
        calmUntil[pc.netId] = Time.time + CalmSeconds;
        Plugin.L.LogInfo($"[POPUP] antivirus désactivé sur le PC de {pc.user?.username ?? "?"} : pluie de popups au lieu du virus");
        Request(pc);
        return false;
    }

    // Debug : popups sur mon PC
    internal static string Test()
    {
        var pc = PcMod.LocalComputer();
        if (pc == null) return "PC local introuvable";
        Request(pc);
        return "Popups demandées sur mon PC";
    }
}
