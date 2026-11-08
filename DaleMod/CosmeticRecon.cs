using System;
using System.Text;
using HarmonyLib;
using Player.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// TEMPORAIRE : relevé des cosmétiques du jeu dans le log (écran de personnalisation, sauvegarde, Verify, placement
// sur le visage). À retirer après l'étude.
static class CosmeticRecon
{
    static readonly System.Collections.Generic.HashSet<IntPtr> done = new();
    static float next;
    static bool tested, uiDumped;

    internal static void Tick()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 3f;
        if (!tested) { tested = true; try { TestVerify(); TestOwned(); } catch (Exception e) { Plugin.L.LogError("[RECON] tests : " + e); } }
        if (!uiDumped)
        {
            var tab = Object.FindObjectOfType<NewCustomizeTab>(true);
            if (tab != null) { uiDumped = true; try { DumpUi(tab); } catch (Exception e) { Plugin.L.LogError("[RECON] UI : " + e); } }
        }
        foreach (var c in Object.FindObjectsOfType<PlayerCustomizer>(true))
        {
            if (c == null || !done.Add(c.Pointer)) continue;
            if (done.Count > 2) return;
            try { DumpItems(c); DumpFace(c); } catch (Exception e) { Plugin.L.LogError("[RECON] " + e); }
        }
    }

    // Ce que fait le jeu d'une sauvegarde avec un objet inconnu (cas "mod désactivé")
    static void TestVerify()
    {
        var c = Object.FindObjectOfType<PlayerCustomizer>(true);
        int n = c?.customizationParameters?.Count ?? 12;
        var baseJson = PlayerPrefs.GetString("playerCustomizationJson_0", "");
        if (string.IsNullOrEmpty(baseJson)) { Plugin.L.LogInfo("[RECON] pas de sauvegarde 0"); return; }
        foreach (var (label, edit) in new (string, Func<PlayerCustomization, PlayerCustomization>)[]
        {
            ("barbe modèle 50", p => { p.customizationParameterSelection[3].modelIndex = 50; return p; }),
            ("chemise couleur 40", p => { p.customizationParameterSelection[7].colorIndex = 40; return p; }),
            ("13e catégorie", p => { var s = new CustomizationParameterSelection(); s.parameterIndex = 12; s.modelIndex = 1; s.colorIndex = 0; p.customizationParameterSelection.Add(s); return p; }),
        })
        {
            foreach (bool own in new[] { false, true })
            {
                try
                {
                    var p = edit(PlayerCustomization.FromJson(baseJson));
                    p.Verify(n, own);
                    Plugin.L.LogInfo($"[RECON] Verify({n}, possession={own}) sur {label} -> {Short(p.ToJson())}");
                }
                catch (Exception e) { Plugin.L.LogInfo($"[RECON] Verify({n}, possession={own}) sur {label} -> EXCEPTION {e.GetType().Name}: {e.Message}"); }
            }
        }
        try
        {
            var p = PlayerCustomization.FromJson(baseJson);
            p.customizationParameterSelection[3].modelIndex = 50;
            c?.Customize(p, Player.Lobby.PlayerRole.Specialist, false, false);
            Plugin.L.LogInfo("[RECON] Customize sans Verify avec barbe 50 : pas d'exception");
        }
        catch (Exception e) { Plugin.L.LogInfo("[RECON] Customize sans Verify avec barbe 50 -> EXCEPTION " + e.Message); }
        try { c?.Customize(PlayerCustomization.FromJson(baseJson), Player.Lobby.PlayerRole.Specialist, false, false); } catch { }
    }

    static string Short(string json)
    {
        int i = json.IndexOf("\"firstName\"", StringComparison.Ordinal);
        return json.Replace("\"parameterIndex\":", "p").Replace("\"modelIndex\":", "m").Replace("\"colorIndex\":", "c").Replace("customizationParameterSelection", "sel");
    }

    static void TestOwned()
    {
        var im = Currency.ItemManager.instance;
        if (im == null) { Plugin.L.LogInfo("[RECON] pas d'ItemManager"); return; }
        Plugin.L.LogInfo($"[RECON] ItemManager : argent {im.money}, objets possédés {im.ownedItems?.Count}, couleurs possédées {im.ownedColorItems?.Count}");
        foreach (var (cat, id) in new[] { (0, 1), (0, 8), (0, 9), (3, 1), (3, 2), (5, 2), (0, 40), (13, 0) })
        {
            string r;
            try { r = $"objet possédé {im.IsItemOwned(cat, id)}, prix {im.GetItemPrice(cat, id)}"; } catch (Exception e) { r = "EXCEPTION " + e.Message; }
            Plugin.L.LogInfo($"[RECON] IsItemOwned({cat},{id}) : {r}");
        }
        foreach (var (cat, id) in new[] { (7, 0), (7, 7), (7, 40) })
        {
            string r;
            try { r = $"{im.IsColorItemOwned(cat, id)}"; } catch (Exception e) { r = "EXCEPTION " + e.Message; }
            Plugin.L.LogInfo($"[RECON] IsColorItemOwned({cat},{id}) : {r}");
        }
    }

    // Objets : nom du GameObject, type de rendu, taille du mesh
    static void DumpItems(PlayerCustomizer c)
    {
        var sb = new StringBuilder($"[RECON] objets de {Path(c.transform)}\n");
        var ps = c.customizationParameters;
        for (int i = 0; ps != null && i < ps.Count; i++)
        {
            var p = ps[i];
            for (int j = 0; p?.customizationItems != null && j < p.customizationItems.Count; j++)
            {
                var it = p.customizationItems[j];
                if (it == null) { sb.AppendLine($"  [{i}.{j}] null"); continue; }
                var go = it.gameObject;
                var r = go.GetComponent<Renderer>();
                var smr = go.GetComponent<SkinnedMeshRenderer>();
                var mf = go.GetComponent<MeshFilter>();
                var mesh = smr != null ? smr.sharedMesh : mf?.sharedMesh;
                sb.AppendLine($"  [{i}.{j}] go={go.name} parent={go.transform.parent?.name} rendu={(smr != null ? "skinned" : mf != null ? "rigide" : r?.GetIl2CppType().Name ?? "aucun")} " +
                              $"mesh={(mesh == null ? "-" : $"{mesh.name} {mesh.vertexCount}v b={mesh.bounds.center} {mesh.bounds.size}")} lpos={go.transform.localPosition} lrot={go.transform.localEulerAngles} lscale={go.transform.localScale} " +
                              $"mats={(r == null ? "-" : r.sharedMaterials.Length.ToString())}");
            }
            for (int k = 0; p?.colorItems != null && k < p.colorItems.Count; k++)
            {
                var ci = p.colorItems[k];
                if (ci?.strictItems == null || ci.strictItems.Count == 0) continue;
                string s = "";
                foreach (var st in ci.strictItems) s += (st == null ? "null" : st.gameObject.name) + " ";
                sb.AppendLine($"  couleur [{i}.{k}] {ci.itemName} réservée à : {s}");
            }
        }
        Plugin.L.LogInfo(sb.ToString());
    }

    // Placement sur le visage : boîtes des objets actifs dans le repère de l'os de la tête
    static void DumpFace(PlayerCustomizer c)
    {
        Transform head = null;
        foreach (var t in c.GetComponentsInChildren<Transform>(true)) if (t.name == "head") { head = t; break; }
        if (head == null) { Plugin.L.LogInfo("[RECON] pas d'os head"); return; }
        var sb = new StringBuilder($"[RECON] os head : pos {head.position} avant {head.forward} haut {head.up} droite {head.right} échelle {head.lossyScale}\n");
        foreach (var r in c.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.gameObject.activeInHierarchy) continue;
            var b = r.bounds;
            sb.AppendLine($"  {r.name} ({r.GetIl2CppType().Name}) monde min {b.min} max {b.max} | repère tête : min {head.InverseTransformPoint(b.min)} max {head.InverseTransformPoint(b.max)} centre {head.InverseTransformPoint(b.center)}");
        }
        // Jupe (inactive) et chemises : taille du mesh
        foreach (var smr in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.name.StartsWith("Skirt") || smr.name.StartsWith("Shirt") || smr.name.StartsWith("Pants") || smr.name.StartsWith("Jacket"))
                sb.AppendLine($"  maillage {smr.name} : {smr.sharedMesh?.name} {smr.sharedMesh?.vertexCount}v bounds local {smr.localBounds.center} {smr.localBounds.size} racine {smr.rootBone?.name} mats {smr.sharedMaterials.Length}");
        Plugin.L.LogInfo(sb.ToString());
    }

    internal static void DumpUi(NewCustomizeTab tab)
    {
        var sb = new StringBuilder($"[RECON] écran de personnalisation {Path(tab.transform)} (actif {tab.gameObject.activeInHierarchy})\n");
        Tree(tab.transform, 0, sb);
        Plugin.L.LogInfo(sb.ToString());
    }

    static void Tree(Transform t, int depth, StringBuilder sb)
    {
        if (depth > 9) return;
        sb.Append(' ', depth * 2).Append(t.name);
        var comps = t.GetComponents<Component>();
        foreach (var co in comps)
        {
            if (co == null) continue;
            string n = co.GetIl2CppType().Name;
            if (n == "Transform" || n == "RectTransform" || n == "CanvasRenderer") continue;
            sb.Append(" <").Append(n);
            var img = co.TryCast<UnityEngine.UI.Image>();
            if (img != null && img.sprite != null) sb.Append(":").Append(img.sprite.name);
            var tmp = co.TryCast<TMPro.TextMeshProUGUI>();
            if (tmp != null) sb.Append(":'").Append(tmp.text?.Replace("\n", " ")).Append('\'');
            sb.Append('>');
        }
        if (!t.gameObject.activeSelf) sb.Append(" (inactif)");
        sb.Append('\n');
        int max = Math.Min(t.childCount, 40);
        for (int i = 0; i < max; i++) Tree(t.GetChild(i), depth + 1, sb);
        if (t.childCount > max) sb.Append(' ', depth * 2 + 2).Append($"... {t.childCount - max} de plus\n");
    }

    // Sauvegarde : qui lit / écrit les apparences dans les PlayerPrefs (le patch tient-il sur une méthode Unity ?)
    [HarmonyPatch(typeof(PlayerPrefs), nameof(PlayerPrefs.SetString))]
    [HarmonyPrefix]
    static void TraceSet(string key)
    {
        if (key != null && key.StartsWith("playerCustomization")) Plugin.L.LogInfo($"[RECON] PlayerPrefs.SetString({key})\n{Environment.StackTrace}");
    }

    [HarmonyPatch(typeof(PlayerPrefs), nameof(PlayerPrefs.GetString), typeof(string))]
    [HarmonyPrefix]
    static void TraceGet1(string key)
    {
        if (key != null && key.StartsWith("playerCustomization")) Plugin.L.LogInfo($"[RECON] PlayerPrefs.GetString({key})");
    }

    [HarmonyPatch(typeof(PlayerPrefs), nameof(PlayerPrefs.GetString), typeof(string), typeof(string))]
    [HarmonyPrefix]
    static void TraceGet2(string key)
    {
        if (key != null && key.StartsWith("playerCustomization")) Plugin.L.LogInfo($"[RECON] PlayerPrefs.GetString({key}, défaut)");
    }

    [HarmonyPatch(typeof(NewCustomizeTab), nameof(NewCustomizeTab.SaveCustomization))]
    [HarmonyPrefix]
    static void TraceSave() => Plugin.L.LogInfo("[RECON] NewCustomizeTab.SaveCustomization");

    [HarmonyPatch(typeof(NewCustomizeTab), nameof(NewCustomizeTab.LoadCustomizationPreset))]
    [HarmonyPrefix]
    static void TraceLoad(int index) => Plugin.L.LogInfo($"[RECON] NewCustomizeTab.LoadCustomizationPreset({index})");

    [HarmonyPatch(typeof(NewCustomizeTab), nameof(NewCustomizeTab.SelectCategory))]
    [HarmonyPrefix]
    static void TraceCat(int categoryIndex) => Plugin.L.LogInfo($"[RECON] SelectCategory({categoryIndex})");

    [HarmonyPatch(typeof(Player.Lobby.LobbyPlayer), nameof(Player.Lobby.LobbyPlayer.NetworkplayerCustomizationJson), MethodType.Setter)]
    [HarmonyPrefix]
    static void TraceNet() => Plugin.L.LogInfo($"[RECON] LobbyPlayer.NetworkplayerCustomizationJson modifié\n{Environment.StackTrace}");

    static string Name(Object o) => o == null ? "-" : o.name;
    static string Path(Transform t) { string s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }
}
