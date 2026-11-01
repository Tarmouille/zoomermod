using System;
using System.Collections.Generic;
using System.IO;
using Computer.Scripts.System;
using HarmonyLib;
using Il2CppSystem.FileSystem;
using Directory = Il2CppSystem.FileSystem.Directory;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// App "GlucksTube" : une icône "GlucksTube" sur le bureau du PC local, copiée de celle de MeowTube.
// Elle ouvre le lecteur vidéo du jeu, dans lequel on remplace la vidéo par Assets\bestof.mp4 (MeowTube n'est pas touché).
// Après 10 s de visionnage : hook "Watched GlucksTube" (tâche "Discours inspirants").
// La fenêtre du lecteur est rebaptisée : tout texte "MeowTube" devient "GlucksTube", les images MeowTube notre logo.
// Tout passe par un sondage régulier (Tick) et le clic sur l'icône : les petites méthodes du jeu sont souvent inlinées.
static class PcMod
{
    internal const string AppName = "GlucksTube", OpenedAction = "Opened", WatchedAction = "Watched", Key = "dalemod.gluckstube";
    const string IconLabel = AppName, VideoFile = "bestof.mp4", IconFile = "glucks.png", LogoFile = "gluckstube_logo.png";
    static Sprite appIcon;
    static Sprite AppIcon(Sprite fallback)
    {
        if (appIcon == null)
            try { appIcon = ModAssets.LoadSprite(IconFile); }
            catch (Exception e) { Plugin.L.LogError($"[PC] icône {IconFile} : {e}"); }
        return appIcon ?? fallback;
    }
    static Sprite wordmark;
    static Sprite Wordmark()
    {
        if (wordmark == null)
            try { wordmark = ModAssets.LoadSprite(LogoFile); }
            catch (Exception e) { Plugin.L.LogError($"[PC] logo {LogoFile} : {e}"); }
        return wordmark;
    }
    const float WatchSeconds = 10f, TickSeconds = 0.5f;

    internal static Player.Tasks.TaskHook WatchedHook() => new(null, AppName, null, WatchedAction, null, null);
    static Player.Tasks.TaskHook OpenedHook() => new(null, AppName, null, OpenedAction, null, null);

    internal static ComputerController LocalComputer()
    {
        var desk = LobbyManager.instance?.GetLocalPlayer()?.workStationController;
        return desk == null ? null : desk.GetComponentInChildren<ComputerController>(true);
    }

    static float nextTick;

    // Appelé à chaque frame par DebugMenu.Update
    internal static void Tick()
    {
        if (Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + TickSeconds;
        EnsureIcons();
        WatchPlayers();
    }

    // --- Icône sur le bureau ---
    // Ajoutée sur TOUS les PC, chez tous les joueurs : les clics sont rejoués chez chacun (RpcClick),
    // l'icône doit donc exister partout, au même endroit, pour que les autres voient et entendent la vidéo.
    static readonly HashSet<IntPtr> iconTried = new();   // FileSystemManager déjà traités

    static void EnsureIcons()
    {
        var local = LocalComputer();
        foreach (var pc in Object.FindObjectsOfType<ComputerController>())
        {
            var fs = pc.GetComponentInChildren<FileSystemManager>(true);
            if (fs?.root == null || !iconTried.Add(fs.Pointer)) continue;   // une seule tentative par PC
            bool isLocal = local != null && pc.Pointer == local.Pointer;

            var all = new List<FsObject>();
            Walk(fs.root, all);
            // Modèle : l'objet MeowTube, de préférence celui d'un dossier "Desktop" (l'exe peut aussi exister ailleurs)
            FsObject template = null;
            bool present = false;
            foreach (var o in all)
            {
                if (o.label == IconLabel) { present = true; break; }
                if (!Contains(o, "meow")) continue;
                if (template == null || Safe(() => o.directory?.name).IndexOf("desk", StringComparison.OrdinalIgnoreCase) >= 0) template = o;
            }
            if (present) continue;
            try { CreateIcon(fs, template, isLocal); }
            catch (Exception e) { Plugin.L.LogError($"[PC] création de l'icône : {e}"); }
            if (isLocal) Inspect(pc, fs);   // après coup : le diagnostic ne doit jamais empêcher l'ajout de l'icône
        }
    }

    static void CreateIcon(FileSystemManager fs, FsObject template, bool log)
    {
        if (template == null) { Plugin.L.LogWarning("[PC] icône MeowTube introuvable : pas d'icône GlucksTube"); return; }

        // Les icônes du bureau sont des File dont le NOM désigne l'app lancée ("MeowTube" -> MeowTube.exe) et dont le
        // LABEL est affiché : même nom que MeowTube (ouvre le lecteur vidéo), label à nous.
        // Ajout direct dans le dossier : Directory.GetPath() plante, donc pas de CreateFsObject(…, chemin).
        var dir = template.directory;
        if (dir?.contents == null) { Plugin.L.LogWarning("[PC] dossier de l'icône MeowTube introuvable"); return; }
        var copy = new Il2CppSystem.FileSystem.File(template.GetFileName(), IconLabel)
        {
            label = IconLabel,
            icon = AppIcon(template.icon),
            directory = dir,
        };
        dir.contents.Add(copy);
        fs.onFsObjectChanged?.Invoke();
        foreach (var d in Object.FindObjectsOfType<Computer.Desktop.Desktop>())   // rafraîchit le bureau de ce PC
            if (d.fileSystemManager != null && d.fileSystemManager.Pointer == fs.Pointer) d.UpdateDesktop();
        if (log) Plugin.L.LogInfo($"[PC] icône '{IconLabel}' ajoutée dans '{dir.name}' (lance '{copy.GetFileName()}')");
    }

    internal static bool Contains(FsObject o, string s) =>
        (o.label ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
        || Safe(o.GetFileName).IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;

    // Certains accesseurs du jeu plantent sur des objets incomplets (ex. dossier racine)
    internal static string Safe(Func<string> f) { try { return f() ?? ""; } catch { return "?"; } }

    internal static void Walk(Directory d, List<FsObject> acc)
    {
        if (d?.contents == null) return;
        foreach (var o in d.contents)
        {
            if (o == null) continue;
            acc.Add(o);
            var sub = o.TryCast<Directory>();
            if (sub != null) Walk(sub, acc);
        }
    }

    // --- Lecteur vidéo ---
    static readonly Dictionary<IntPtr, float> ourClicks = new();   // PC -> heure du dernier clic sur notre icône
    static readonly HashSet<IntPtr> seen = new(), ours = new();
    static IntPtr current;
    static float watched;
    static bool hookSent;

    static IntPtr PcOf(Component c)
    {
        var pc = c == null ? null : c.GetComponentInParent<ComputerController>(true);
        return pc == null ? IntPtr.Zero : pc.Pointer;
    }

    // Clic sur une icône du bureau (rejoué chez tous les joueurs) : on note sur quel PC on a cliqué la nôtre
    [HarmonyPatch(typeof(DesktopIcon), nameof(DesktopIcon.OnCursorClick))]
    [HarmonyPrefix]
    static void OnIconClick(DesktopIcon __instance)
    {
        if (__instance.fsObject?.label != IconLabel) return;
        ourClicks[PcOf(__instance)] = Time.unscaledTime;
        Plugin.L.LogInfo("[PC] clic sur l'icône GlucksTube");
    }

    // Notre icône lance MeowTube.exe : le jeu envoie alors "Opened MeowTube", qu'on ignore pour ne pas valider la tâche MeowTube
    [HarmonyPatch(typeof(Player.Tasks.TaskHook), nameof(Player.Tasks.TaskHook.TriggerTaskHookCommandStatic))]
    [HarmonyPrefix]
    static bool SkipMeowTubeOpen(Player.Tasks.TaskHook taskHook)
    {
        if (taskHook?.action != "Opened" || taskHook.passiveObjectName != "MeowTube") return true;
        var local = LocalComputer();
        bool ourClick = local != null && ourClicks.TryGetValue(local.Pointer, out var t) && Time.unscaledTime - t < 3f;
        if (ourClick) Plugin.L.LogInfo("[PC] 'Opened MeowTube' ignoré (c'est GlucksTube)");
        return !ourClick;
    }

    static void WatchPlayers()
    {
        var local = LocalComputer();
        IntPtr localActive = IntPtr.Zero;
        foreach (var vp in Object.FindObjectsOfType<VideoPlayer>())
        {
            var p = vp.Pointer;
            var pc = PcOf(vp);
            if (seen.Add(p))
            {
                bool mine = (ourClicks.TryGetValue(pc, out var t) && Time.unscaledTime - t < 3f) || vp.fsObject?.label == IconLabel;
                Plugin.L.LogInfo($"[PC] lecteur ouvert : fichier='{vp.fileName}' pc={(pc == IntPtr.Zero ? "?" : "ok")} -> {(mine ? AppName : "jeu")}");
                if (mine)
                {
                    if (local != null && pc == local.Pointer) Player.Tasks.TaskHook.TriggerTaskHookCommandStatic(OpenedHook());
                    ours.Add(p);
                    ourClicks.Remove(pc);
                    vp.StopAllCoroutines();   // minuteurs MeowTube (10 s / 10 min) : pas de "Watched MeowTube" en regardant GlucksTube
                }
            }
            if (!ours.Contains(p)) continue;
            Configure(vp);
            if (local != null && pc == local.Pointer) localActive = p;
        }

        // Validation de la tâche : uniquement pour le lecteur ouvert sur son propre PC
        if (localActive == IntPtr.Zero) { current = IntPtr.Zero; return; }
        if (localActive != current) { current = localActive; watched = 0f; }
        watched += TickSeconds;
        var task = CustomTaskPatches.ActiveTask();
        if (!CustomTaskPatches.IsActive(task, Key)) { hookSent = false; return; }
        if (!hookSent && watched >= WatchSeconds)
        {
            hookSent = true;
            Player.Tasks.TaskHook.TriggerTaskHookCommandStatic(WatchedHook());
            Plugin.L.LogInfo("[PC] GlucksTube regardé 10 s");
        }
    }

    static readonly HashSet<IntPtr> configured = new();

    // Remplace la vidéo du lecteur par la nôtre (réappliqué si le jeu la change) et renomme la fenêtre
    static void Configure(VideoPlayer vp)
    {
        var path = Path.Combine(ModAssets.Dir, VideoFile);
        var uvp = vp.videoPlayer;
        if (uvp != null && (uvp.source != UnityEngine.Video.VideoSource.Url || !configured.Contains(uvp.Pointer)))
        {
            uvp.source = UnityEngine.Video.VideoSource.Url;
            SetVideoUrl(uvp, path);
            uvp.isLooping = true;
            // Son : piste 0 de notre fichier, vers la même sortie que la vidéo de chat (enceinte du PC)
            uvp.controlledAudioTrackCount = 1;
            uvp.EnableAudioTrack(0, true);
            // Volume au maximum (le best of est plus faible que les vidéos de chat)
            uvp.SetDirectAudioVolume(0, 1f);
            var src = uvp.GetTargetAudioSource(0);
            if (src != null) src.volume = 1f;
            Plugin.L.LogInfo($"[PC] audio : mode={uvp.audioOutputMode} source={(src == null ? "-" : $"vol={src.volume} spatial={src.spatialBlend} portée={src.maxDistance}")}");
            uvp.Play();
            configured.Add(uvp.Pointer);
            Plugin.L.LogInfo($"[PC] vidéo remplacée par {path}");
        }
        Rebrand(vp);
    }

    // Toute la fenêtre, à chaque tick (le jeu peut remettre ses textes / images) : textes "MeowTube" -> "GlucksTube",
    // images MeowTube -> notre logo (large) ou notre icône (carrée), selon la forme de l'image d'origine.
    static readonly HashSet<IntPtr> dumped = new();
    static void Rebrand(VideoPlayer vp)
    {
        var win = vp.applicationWindow;
        if (win == null) return;
        var icon = AppIcon(null);
        win.windowTitle = AppName;
        if (win.title != null) win.title.text = AppName;
        if (icon != null) { win.icon = icon; if (win.iconImage != null) win.iconImage.sprite = icon; }

        bool dump = dumped.Add(win.Pointer);
        foreach (var t in win.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            if (dump) Plugin.L.LogInfo($"[PC] fenêtre : texte '{t.name}' = '{t.text}'");
            if ((t.text ?? "").IndexOf("meowtube", StringComparison.OrdinalIgnoreCase) >= 0)
                t.text = System.Text.RegularExpressions.Regex.Replace(t.text, "meow ?tube", AppName, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        foreach (var img in win.GetComponentsInChildren<UnityEngine.UI.Image>(true))
        {
            var sp = img.sprite;
            if (dump) Plugin.L.LogInfo($"[PC] fenêtre : image '{img.name}' sprite='{(sp == null ? "-" : sp.name)}'");
            if (sp == null || sp == icon || sp == wordmark) continue;
            if ((sp.name ?? "").IndexOf("meow", StringComparison.OrdinalIgnoreCase) < 0
                && img.name.IndexOf("meow", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var r = sp.rect;
            var mine = r.height > 0 && r.width / r.height > 1.6f ? Wordmark() : icon;
            if (mine == null) continue;
            img.sprite = mine;
            img.preserveAspect = true;
        }
    }

    // --- Diagnostic ---
    internal static string Inspect()
    {
        var pc = LocalComputer();
        if (pc == null) return "Aucun PC trouvé";
        Inspect(pc, pc.GetComponentInChildren<FileSystemManager>(true));
        return "PC inspecté (voir log)";
    }

    static void Inspect(ComputerController pc, FileSystemManager fs)
    {
        var log = Plugin.L;
        log.LogInfo($"[PC] {pc.gameObject.name} mail={pc.emailAddress}");
        if (fs?.root != null)
        {
            var all = new List<FsObject>();
            Walk(fs.root, all);
            foreach (var o in all)
            {
                log.LogInfo($"[PC]   {o.GetIl2CppType().Name} '{Safe(o.GetPath)}' nom='{Safe(o.GetFileName)}' label='{o.label}' open='{o.openCommand}'" +
                            (o.TryCast<ShortCut>() is ShortCut s ? $" -> '{s.target}'" : "") +
                            $" dossier='{Safe(() => o.directory?.name)}'");
            }
        }
        if (pc.executables != null)
            foreach (var e in pc.executables)
                if (e != null) log.LogInfo($"[PC]   app '{e.fileName}' ext='{e.openFileExtension}' type={e.GetIl2CppType().Name}");
    }

    // VideoPlayer.url : le setter C# a été retiré du jeu (code jamais utilisé, supprimé à la compilation) et celui de
    // l'interop passe par des Span absents. On appelle directement la fonction du moteur set_url_Injected(self, span UTF-16).
    static unsafe void SetVideoUrl(UnityEngine.Video.VideoPlayer vp, string url)
    {
        var icall = Il2CppInterop.Runtime.IL2CPP.il2cpp_resolve_icall("UnityEngine.Video.VideoPlayer::set_url_Injected");
        if (icall == IntPtr.Zero) throw new MissingMethodException("VideoPlayer::set_url_Injected");
        fixed (char* chars = url)
        {
            var span = new UnityEngine.Bindings.ManagedSpanWrapper(chars, url.Length);
            ((delegate* unmanaged<IntPtr, UnityEngine.Bindings.ManagedSpanWrapper*, void>)icall)(vp.m_CachedPtr, &span);
        }
    }
}
