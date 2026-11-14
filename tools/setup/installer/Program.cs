using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ZoomerModInstaller;

// Installeur ZoomerMod : trouve le jeu via Steam, installe / met à jour / désinstalle le mod embarqué, et
// l'active / le désactive (ligne "enabled" de doorstop_config.ini). Même logique que ZoomerMod-Setup.ps1.
static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

static class Setup
{
    const string AppId = "2920570";

    internal static readonly string[] RootFiles =
    {
        "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "ZoomerMod ON.bat", "ZoomerMod OFF.bat",
        "LISEZ-MOI.txt", "ZoomerMod-Setup.ps1", "Installer ZoomerMod.bat", "Desinstaller ZoomerMod.bat",
    };

    internal static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "?";

    // Bibliothèques Steam (libraryfolders.vdf) -> appmanifest_2920570.acf -> installdir
    internal static string FindGame()
    {
        var libs = new List<string>();
        string steam = null;
        foreach (var (hive, key, name) in new[]
                 {
                     (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                 })
        {
            try { using var k = hive.OpenSubKey(key); steam = k?.GetValue(name) as string; } catch { }
            if (!string.IsNullOrEmpty(steam)) break;
        }
        if (!string.IsNullOrEmpty(steam))
        {
            libs.Add(steam);
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        libs.Add(@"C:\Program Files (x86)\Steam");
        foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var acf = Path.Combine(lib, "steamapps", $"appmanifest_{AppId}.acf");
                if (!File.Exists(acf)) continue;
                var m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\"");
                if (!m.Success) continue;
                var dir = Path.GetFullPath(Path.Combine(lib, "steamapps", "common", m.Groups[1].Value));
                if (IsGame(dir)) return dir;
            }
            catch { }
        }
        return null;
    }

    internal static bool IsGame(string dir) => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "DDSS.exe"));
    internal static bool GameRunning() => Process.GetProcessesByName("DDSS").Length > 0;

    internal static string InstalledVersion(string game)
    {
        var plugin = Path.Combine(game, "BepInEx", "plugins", "DaleMod");
        if (!File.Exists(Path.Combine(plugin, "DaleMod.dll"))) return null;
        var v = Path.Combine(plugin, "version.txt");
        return File.Exists(v) ? File.ReadAllText(v).Trim() : "";   // mod d'avant les installeurs : version non écrite
    }

    internal static bool? Enabled(string game)
    {
        var ini = Path.Combine(game, "doorstop_config.ini");
        if (!File.Exists(ini) || !File.Exists(Path.Combine(game, "winhttp.dll"))) return null;
        foreach (var line in File.ReadAllLines(ini))
        {
            var m = Regex.Match(line, @"^enabled\s*=\s*(\w+)");
            if (m.Success) return m.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        return null;
    }

    internal static void SetEnabled(string game, bool on)
    {
        var ini = Path.Combine(game, "doorstop_config.ini");
        if (!File.Exists(ini)) return;
        var lines = File.ReadAllLines(ini).Select(l => Regex.IsMatch(l, @"^enabled\s*=") ? "enabled = " + (on ? "true" : "false") : l).ToArray();
        File.WriteAllLines(ini, lines);
    }

    internal static void Install(string game, Action<string> log)
    {
        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
                            ?? throw new InvalidOperationException("mod absent de l'installeur (build sans PayloadZip)");
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
        // Ancienne version du mod retirée (assets supprimés depuis) ; BepInEx\interop et cache sont gardés
        var plugin = Path.Combine(game, "BepInEx", "plugins", "DaleMod");
        if (Directory.Exists(plugin)) { log("Ancienne version retirée"); Directory.Delete(plugin, true); }
        int n = 0;
        var root = Path.GetFullPath(game).TrimEnd('\\') + "\\";
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith("/")) continue;
            var dest = Path.GetFullPath(Path.Combine(game, e.FullName.Replace('/', '\\')));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // jamais hors du dossier du jeu
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            e.ExtractToFile(dest, true);
            if (++n % 50 == 0) log($"{n} fichiers copiés…");
        }
        SetEnabled(game, true);
        log($"{n} fichiers copiés, mod activé.");
    }

    internal static string Uninstall(string game)
    {
        var plugins = Path.Combine(game, "BepInEx", "plugins");
        var others = Directory.Exists(plugins)
            ? Directory.GetFileSystemEntries(plugins).Select(Path.GetFileName).Where(n => !n.Equals("DaleMod", StringComparison.OrdinalIgnoreCase)).ToList()
            : new List<string>();
        var mine = Path.Combine(plugins, "DaleMod");
        if (Directory.Exists(mine)) Directory.Delete(mine, true);
        if (others.Count > 0)
        {
            // D'autres mods BepInEx sont installés : on ne retire que ZoomerMod et ses fichiers, le chargeur reste
            foreach (var f in new[] { "ZoomerMod ON.bat", "ZoomerMod OFF.bat", "LISEZ-MOI.txt", "ZoomerMod-Setup.ps1", "Installer ZoomerMod.bat", "Desinstaller ZoomerMod.bat" })
                TryDelete(Path.Combine(game, f));
            return $"ZoomerMod désinstallé. BepInEx est gardé : d'autres mods l'utilisent ({string.Join(", ", others)}).";
        }
        foreach (var d in new[] { "BepInEx", "dotnet" })
        {
            var p = Path.Combine(game, d);
            if (Directory.Exists(p)) Directory.Delete(p, true);
        }
        foreach (var f in RootFiles) TryDelete(Path.Combine(game, f));
        return "ZoomerMod désinstallé : le jeu est revenu à l'origine.";
    }

    static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
}

sealed class MainForm : Form
{
    readonly TextBox path = new() { ReadOnly = true, Dock = DockStyle.Fill };
    readonly Label state = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 6), Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
    readonly Button install = new() { AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    readonly Button toggle = new() { AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    readonly Button uninstall = new() { Text = "Désinstaller", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    string game;
    bool busy;

    public MainForm()
    {
        Text = $"ZoomerMod {Setup.Version} - Installation";
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(620, 380);
        MinimumSize = new Size(520, 340);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var browse = new Button { Text = "Parcourir…", AutoSize = true };
        browse.Click += (_, _) => Browse();
        install.Click += (_, _) => Run("Installation", () => Setup.Install(game, Log), $"ZoomerMod {Setup.Version} installé. Lance le jeu depuis Steam : le premier lancement est long (1 à 3 minutes).");
        toggle.Click += (_, _) =>
        {
            bool on = Setup.Enabled(game) != true;
            Run(on ? "Activation" : "Désactivation", () => Setup.SetEnabled(game, on), on ? "Mod activé." : "Mod désactivé : le jeu se lance sans mod (parties publiques).");
        };
        uninstall.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Retirer ZoomerMod du jeu ?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string msg = null;
            Run("Désinstallation", () => msg = Setup.Uninstall(game), null, () => msg);
        };

        var intro = new Label
        {
            UseMnemonic = false, AutoSize = true, MaximumSize = new Size(590, 0), Padding = new Padding(0, 0, 0, 6),
            Text = "Mod de fan pour Dale & Dawson Stationery Supplies, à jouer en partie privée entre joueurs qui ont tous la même version.\n" +
                   "Pas de mod en partie publique : désactive-le ici avant.",
        };
        var pathRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(path, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        buttons.Controls.AddRange(new Control[] { install, toggle, uninstall });

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(intro);
        layout.Controls.Add(new Label { Text = "Dossier du jeu :", AutoSize = true });
        layout.Controls.Add(pathRow);
        layout.Controls.Add(state);
        layout.Controls.Add(buttons);
        layout.Controls.Add(log);
        Controls.Add(layout);

        Shown += (_, _) => ActiveControl = install;
        SetGame(Setup.FindGame());
        if (game == null) Log("Jeu introuvable automatiquement : clique sur Parcourir… et choisis le dossier qui contient DDSS.exe.");
        else Log("Jeu trouvé via Steam.");
    }

    void Browse()
    {
        using var d = new FolderBrowserDialog { Description = "Dossier de Dale & Dawson (celui qui contient DDSS.exe)" };
        if (game != null) d.SelectedPath = game;
        if (d.ShowDialog(this) != DialogResult.OK) return;
        if (!Setup.IsGame(d.SelectedPath)) { MessageBox.Show(this, "Ce dossier ne contient pas DDSS.exe.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        SetGame(d.SelectedPath);
    }

    void SetGame(string dir)
    {
        game = Setup.IsGame(dir) ? dir : null;
        path.Text = game ?? "(introuvable)";
        Refresh2();
    }

    void Refresh2()
    {
        string v = game == null ? null : Setup.InstalledVersion(game);
        bool? on = game == null ? null : Setup.Enabled(game);
        state.Text = game == null ? "Choisis le dossier du jeu."
            : v == null ? "ZoomerMod n'est pas installé."
            : $"ZoomerMod {(v == "" ? "" : v + " ")}installé, {(on == true ? "activé" : "désactivé")}.";
        state.ForeColor = v == null ? SystemColors.ControlText : on == true ? Color.ForestGreen : Color.DarkOrange;
        install.Text = v == null ? $"Installer {Setup.Version}" : v == Setup.Version ? "Réinstaller" : $"Mettre à jour en {Setup.Version}";
        toggle.Text = on == true ? "Désactiver" : "Activer";
        install.Enabled = game != null && !busy;
        toggle.Enabled = v != null && on != null && !busy;
        uninstall.Enabled = v != null && !busy;
    }

    void Log(string s)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
        log.AppendText(s + Environment.NewLine);
    }

    async void Run(string what, Action work, string done, Func<string> doneLater = null)
    {
        if (Setup.GameRunning()) { MessageBox.Show(this, "Ferme le jeu d'abord.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        busy = true; Refresh2();
        Log(what + "…");
        try
        {
            await Task.Run(work);
            Log(done ?? doneLater?.Invoke());
        }
        catch (UnauthorizedAccessException e) { Log("ERREUR : accès refusé (" + e.Message + "). Relance l'installeur en administrateur."); }
        catch (Exception e) { Log("ERREUR : " + e.Message); }
        busy = false; Refresh2();
    }
}
