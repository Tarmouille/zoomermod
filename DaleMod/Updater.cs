using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DaleMod;

// Mise à jour automatique (build Release seulement) : au lancement, regarde la dernière release GitHub ; si elle est
// plus récente, télécharge son zip en fond et remplace le dossier plugins\DaleMod. Le jeu en cours garde l'ancienne
// version en mémoire, la nouvelle est chargée au prochain lancement. Le badge du menu affiche BadgeInfo().
// Remplacer la DLL chargée : Windows refuse l'écrasement mais accepte le renommage (DaleMod.dll -> DaleMod.dll.old,
// supprimé au lancement suivant).
internal static class Updater
{
    const string Repo = "Tarmouille/zoomermod";
    enum State { None, Downloading, Installed, Failed }
    static volatile State state;
    static volatile string newVersion;

    // Texte du badge du menu (fil principal : RoleKit.T lit la langue du jeu) : info, null si rien à afficher, et
    // action à faire par le joueur (affichée en évidence), null si aucune
    internal static string BadgeInfo(out string action)
    {
        action = state switch
        {
            State.Installed => RoleKit.T("Relance le jeu", "Restart the game"),
            State.Failed => RoleKit.T("Relance l'installeur", "Run the installer again"),
            _ => null,
        };
        return state switch
        {
            State.Downloading => RoleKit.T($"Mise à jour {newVersion} en téléchargement…", $"Downloading update {newVersion}…"),
            State.Installed => RoleKit.T($"Mise à jour {newVersion} installée", $"Update {newVersion} installed"),
            State.Failed => RoleKit.T("Mise à jour impossible", "Update failed"),
            _ => null,
        };
    }

    internal static void Start(string pluginDir)
    {
        try { File.Delete(Path.Combine(pluginDir, "DaleMod.dll.old")); } catch { }
#if DEBUG
        Plugin.L.LogInfo("[UPDATE] désactivé en Debug");
#else
        System.Threading.ThreadPool.QueueUserWorkItem(Worker, pluginDir);   // pas de lambda : NullableAttribute absent des références
#endif
    }

    static void Worker(object pluginDir)
    {
        try { Run((string)pluginDir, Plugin.Version, Log).GetAwaiter().GetResult(); }
        catch (Exception e) { Plugin.L.LogWarning("[UPDATE] échec : " + e.Message); }
    }

    static void Log(string s) => Plugin.L.LogInfo("[UPDATE] " + s);

    // Sans Unity : testable hors du jeu. Retourne la version installée, ou null si rien à faire.
    internal static async Task<string> Run(string pluginDir, string current, Action<string> log)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ZoomerMod-Updater");

        using var json = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
        string tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
        string latest = tag.TrimStart('v', 'V');
        if (!Version.TryParse(latest, out var vNew) || !Version.TryParse(current, out var vCur) || vNew <= vCur)
        {
            log($"à jour ({current}, dernière release {tag})");
            return null;
        }
        var asset = json.RootElement.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("name").GetString() == $"ZoomerMod-{latest}.zip");
        if (asset.ValueKind != JsonValueKind.Object) { log($"{tag} : pas de ZoomerMod-{latest}.zip dans la release"); return null; }

        log($"{current} -> {latest} : téléchargement");
        newVersion = latest;
        state = State.Downloading;
        // Fichiers de travail dans BepInEx\cache : dans le dossier du jeu, et hors de plugins\ (BepInEx y chargerait une
        // 2e copie de DaleMod.dll si le jeu s'arrêtait en pleine mise à jour)
        string work = Path.GetFullPath(Path.Combine(pluginDir, "..", "..", "cache", "ZoomerMod-update"));
        Directory.CreateDirectory(work);
        string zipPath = Path.Combine(work, $"ZoomerMod-{latest}.zip");
        string stage = Path.Combine(work, "DaleMod");
        try
        {
            using (var src = await http.GetStreamAsync(asset.GetProperty("browser_download_url").GetString()))
            using (var dst = File.Create(zipPath))
                await src.CopyToAsync(dst);

            // 1. Tout extraire à part (pas de mod à moitié remplacé si le zip est abîmé)
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            const string prefix = "BepInEx/plugins/DaleMod/";
            int n = 0;
            using (var zip = ZipFile.OpenRead(zipPath))
                foreach (var e in zip.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || name.EndsWith("/")) continue;
                    string dest = Path.Combine(stage, name.Substring(prefix.Length));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                    n++;
                }
            if (!File.Exists(Path.Combine(stage, "DaleMod.dll"))) throw new Exception("DaleMod.dll absent du zip");

            // 2. Mise en place : la DLL chargée est renommée, le reste écrasé
            string dll = Path.Combine(pluginDir, "DaleMod.dll"), old = dll + ".old";
            if (File.Exists(old)) File.Delete(old);
            if (File.Exists(dll)) File.Move(dll, old);
            try
            {
                foreach (var f in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
                {
                    string dest = Path.Combine(pluginDir, Path.GetRelativePath(stage, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(f, dest, true);
                }
            }
            catch
            {
                if (!File.Exists(dll) && File.Exists(old)) File.Move(old, dll);   // l'ancienne DLL reste utilisable
                throw;
            }
            log($"{latest} installée ({n} fichiers), active au prochain lancement");
            state = State.Installed;
            return latest;
        }
        catch
        {
            state = State.Failed;
            throw;
        }
        finally
        {
            try { File.Delete(zipPath); } catch { }
            try { Directory.Delete(work, true); } catch { }
        }
    }
}
