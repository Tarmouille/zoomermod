#if DEBUG
using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using Player.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Debug seulement : commandes lues dans BepInEx/dalemod_cmd.txt (une par ligne, fichier supprimé après lecture), pour
// régler les cosmétiques sans toucher au jeu :
//   set <catégorie> <modèle> <couleur>   change l'apparence du personnage d'aperçu (menu)
//   face <nom> [distance] [angle]         photo du visage -> BepInEx/shots/<nom>.png
//   body <nom> [angle]                    photo du corps entier
//   shot <nom>                            capture d'écran complète
static class DevRemote
{
    static string CmdFile => Path.Combine(Paths.BepInExRootPath, "dalemod_cmd.txt");
    static string ShotDir => Path.Combine(Paths.BepInExRootPath, "shots");
    static float next;

    // Test de toutes les tâches : une par passage (le jeu a le temps de souffler entre deux)
    static int taskTest = -1;
    static readonly List<string> taskFails = new();

    static void TaskTestStep()
    {
        var lpl = RoleKit.LocalLobby();
        var tc = lpl != null ? lpl.GetComponent<Player.Tasks.TaskController>() : null;
        if (tc == null) { Plugin.L.LogWarning("[TASKTEST] pas de TaskController"); taskTest = -1; return; }
        var list = tc.fullTaskList;
        if (taskTest >= list.Count)
        {
            try { tc.ClearAllTasks(); } catch { }
            Plugin.L.LogInfo($"[TASKTEST] fini : {taskFails.Count} tâche(s) en échec sur {list.Count} : {string.Join(", ", taskFails)}");
            taskTest = -1;
            return;
        }
        int i = taskTest++;
        string name = CustomTaskPatches.CustomTitle(i, list.Count) ?? list[i].Item1.Name;
        try
        {
            tc.ClearAllTasks();
            tc.InstantiateAndAssignTask(i);
            var t = tc.GetActiveTask();
            if (t == null) { Plugin.L.LogInfo($"[TASKTEST] #{i} {name} : aucune tâche créée"); return; }
            t.SetUpTask();
            Plugin.L.LogInfo($"[TASKTEST] #{i} {name} : ok ({t.subTasks?.Count ?? 0} étape(s))");
        }
        catch (Exception e)
        {
            taskFails.Add($"#{i} {name}");
            Plugin.L.LogWarning($"[TASKTEST] #{i} {name} : ÉCHEC {e.GetType().Name} {e.Message.Split('\n')[0]}");
        }
    }

    internal static void Tick()
    {
        if (taskTest >= 0 && Time.unscaledTime >= next - 0.25f) TaskTestStep();
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.5f;
        if (!File.Exists(CmdFile)) return;
        string[] lines;
        try { lines = File.ReadAllLines(CmdFile); File.Delete(CmdFile); } catch { return; }
        foreach (var line in lines)
        {
            var p = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            try { Plugin.L.LogInfo($"[REMOTE] {line.Trim()} -> {Run(p)}"); }
            catch (Exception e) { Plugin.L.LogError($"[REMOTE] {line.Trim()} : {e} | {e.InnerException}"); }
        }
    }

    static string current;   // apparence courante de l'aperçu (JSON), partie du perso enregistré

    static PlayerCustomizer Preview()
    {
        // En partie : son propre personnage ; au menu : l'aperçu de l'écran de personnalisation
        var me = Mirror.NetworkClient.localPlayer;
        if (me != null) { var c = me.GetComponentInChildren<PlayerCustomizer>(true); if (c != null) return c; }
        var tab = Object.FindObjectOfType<NewCustomizeTab>(true);
        if (tab != null && tab.playerCustomizer != null) return tab.playerCustomizer;
        return Object.FindObjectOfType<PlayerCustomizer>();
    }

    static string Run(string[] p)
    {
        var c = Preview();
        switch (p[0])
        {
            case "set":
            {
                if (c == null) return "pas de personnage";
                int cat = int.Parse(p[1]), model = int.Parse(p[2]), color = int.Parse(p[3]);
                current ??= PlayerPrefs.GetString("playerCustomizationJson_" + PlayerPrefs.GetInt("currentCustomizationIndex", 0), "");
                if (string.IsNullOrEmpty(current) && c.playerCustomization != null) current = c.playerCustomization.ToJson();
                var pc = string.IsNullOrEmpty(current) ? null : PlayerCustomization.FromJson(current);
                Cosmetics.Ensure(c);
                if (pc?.customizationParameterSelection == null) { current = null; return "apparence illisible : " + current; }
                CustomizationParameterSelection found = null;
                foreach (var s in pc.customizationParameterSelection) if (s != null && s.parameterIndex == cat) found = s;
                if (found == null) { found = new CustomizationParameterSelection(); found.parameterIndex = cat; pc.customizationParameterSelection.Add(found); }
                found.modelIndex = model; found.colorIndex = color;
                Plugin.L.LogInfo("[REMOTE] apparence : " + pc.ToJson());
                c.Customize(pc, p.Length > 4 && p[4] == "manager" ? Player.Lobby.PlayerRole.Manager : Player.Lobby.PlayerRole.Specialist, false, false);
                current = pc.ToJson();
                return "ok";
            }
            case "preset":
            {
                // Comme un clic sur un personnage enregistré dans l'écran de personnalisation
                var tab = Object.FindObjectOfType<NewCustomizeTab>(true);
                if (tab == null) return "pas d'écran";
                tab.LoadCustomizationPreset(int.Parse(p[1]));
                current = null;
                return "personnage " + p[1];
            }
            case "size":
            {
                if (c == null) return "pas de personnage";
                current ??= PlayerPrefs.GetString("playerCustomizationJson_" + PlayerPrefs.GetInt("currentCustomizationIndex", 0), "");
                if (string.IsNullOrEmpty(current) && c.playerCustomization != null) current = c.playerCustomization.ToJson();
                var pc = PlayerCustomization.FromJson(current);
                pc.bodySize = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                c.Customize(pc, Player.Lobby.PlayerRole.Specialist, false, false);
                current = pc.ToJson();
                return "corpulence " + pc.bodySize;
            }
            case "face":
            case "body":
            {
                if (c == null) return "pas de personnage";
                Transform head = null;
                foreach (var t in c.GetComponentsInChildren<Transform>(true)) if (t.name == "head") { head = t; break; }
                if (head == null) return "pas de tête";
                bool face = p[0] == "face";
                float scale = c.transform.lossyScale.y;
                float dist = p.Length > 2 ? float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) : face ? 0.9f : 2.2f;
                float angle = p.Length > 3 ? float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                if (!face && p.Length > 2) { angle = dist; dist = 2.2f; }
                var target = face ? head.position + Vector3.up * 0.13f * scale : c.transform.position + Vector3.up * 0.5f * scale;
                var fwd = Quaternion.AngleAxis(angle, Vector3.up) * Flat(head.forward);
                return Photo(p.Length > 1 ? p[1] : "photo", target + fwd * dist * scale, target);
            }
            case "dump":
            {
                if (c == null) return "pas de personnage";
                var sb = new System.Text.StringBuilder();
                foreach (var r in c.GetComponentsInChildren<Renderer>(false))
                    sb.Append(r.name).Append(r.enabled ? "" : "(off)").Append(' ');
                var ps = c.customizationParameters;
                var p3 = ps[3];
                for (int i = 0; i < p3.customizationItems.Count; i++)
                {
                    var it = p3.customizationItems[i];
                    sb.Append($" | barbe {i}: {(it == null ? "null" : it.gameObject.name + " id=" + it.id + " actif=" + it.gameObject.activeSelf)}");
                }
                return sb.ToString();
            }
            case "mesh":
            {
                if (c == null) return "pas de personnage";
                var sb = new System.Text.StringBuilder();
                foreach (var r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!r.name.StartsWith(p.Length > 1 ? p[1] : "Jacket")) continue;
                    var m = r.sharedMesh;
                    sb.Append($" || {r.name}: {m?.name} lisible={m?.isReadable} sommets={m?.vertexCount} sous-maillages={m?.subMeshCount} os={r.bones?.Length} racine={r.rootBone?.name}");
                    try { sb.Append($" triangles={m.triangles.Length}"); } catch (Exception e) { sb.Append(" triangles:ERREUR " + e.Message); }
                    try { sb.Append($" poids={m.boneWeights.Length}"); } catch (Exception e) { sb.Append(" poids:ERREUR " + e.Message); }
                    try { sb.Append($" bindposes={m.bindposes.Length}"); } catch (Exception e) { sb.Append(" bindposes:ERREUR " + e.Message); }
                    try { sb.Append($" uv={m.uv.Length}"); } catch (Exception e) { sb.Append(" uv:ERREUR " + e.Message); }
                    try { var mats = r.sharedMaterials; foreach (var x in mats) sb.Append(" mat=" + x?.name); } catch { }
                }
                return sb.ToString();
            }
            case "render":
            {
                // Réglages d'affichage de chaque morceau du personnage (vue à la première personne)
                if (c == null) return "pas de personnage";
                var sb = new System.Text.StringBuilder();
                foreach (var r in c.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.gameObject.activeInHierarchy) continue;
                    sb.Append($" || {r.name}: calque {LayerMask.LayerToName(r.gameObject.layer)}({r.gameObject.layer}) actif={r.enabled} ombres={r.shadowCastingMode} masque={r.renderingLayerMask} forceOff={r.forceRenderingOff}");
                }
                var cam = Camera.main;
                if (cam != null) sb.Append($" || caméra {cam.name} masque {cam.cullingMask}");
                return sb.ToString();
            }
            case "tasktest":
            {
                var lp = RoleKit.LocalLobby();
                Plugin.L.LogInfo($"[TASKTEST] joueur {lp?.username}, rôle {lp?.playerRole}, sous-rôle {lp?.subRole}");
                taskFails.Clear();
                taskTest = 0;
                return "test des tâches lancé";
            }
            case "ui":
            {
                var tab = Object.FindObjectOfType<NewCustomizeTab>(true);
                if (tab == null) return "pas d'écran";
                CosmeticRecon.DumpUi(tab);
                return "écran relevé";
            }
            case "shot":
            {
                Directory.CreateDirectory(ShotDir);
                var path = Path.Combine(ShotDir, (p.Length > 1 ? p[1] : "ecran") + ".png");
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
                Object.Destroy(tex);
                return path;
            }
        }
        return "commande inconnue";
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

    // Rendu d'une caméra temporaire dans une texture (sans l'interface), enregistré en PNG
    static string Photo(string name, Vector3 from, Vector3 at)
    {
        Directory.CreateDirectory(ShotDir);
        var go = new GameObject("DaleMod_PhotoCam");
        var cam = go.AddComponent<Camera>();
        go.transform.position = from;
        go.transform.LookAt(at);
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 200f;
        var rt = new RenderTexture(768, 768, 24);
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(768, 768, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.Destroy(go);
        var bytes = ImageConversion.EncodeToPNG(tex);
        var path = Path.Combine(ShotDir, name + ".png");
        File.WriteAllBytes(path, bytes);
        Object.Destroy(tex);
        rt.Release();
        return path;
    }
}
#endif
