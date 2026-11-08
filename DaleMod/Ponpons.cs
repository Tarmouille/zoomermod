using System;
using System.Collections.Generic;
using System.IO;
using Player.Scripts;
using UnityEngine;

namespace DaleMod;

// Coupe "Ponpons" (catégorie Cheveux) : deux chignons hauts, raie au milieu, mèches devant les oreilles
// (tools/make_ponpons.py). L'objet est rangé comme les coupes du jeu (sous "Hats", même position que leurs objets) : sa
// couleur vient des couleurs de cheveux, et sous un chapeau qui écrase les cheveux le jeu lui met son matériau
// "écrasé", comme aux siennes. Les chignons sont retirés sous tout chapeau (du jeu ou du mod). Maillage refait à
// chaque changement d'apparence, reporté sur la tête réelle : chaque sommet du modèle garde sa direction depuis le
// centre du crâne et sa hauteur au-dessus de la peau.
static class Ponpons
{
    internal const string Name = "DaleMod_Ponpons";
    const float R0 = 0.14f;   // rayon de la tête du modèle

    internal static CustomizationItem NewItem(CustomizationParameter hair, int id)
    {
        CustomizationItem model = null;
        foreach (var x in hair.customizationItems) if (x != null) { model = x; break; }
        if (model == null) return null;
        var mt = model.transform;
        var go = new GameObject(Name) { layer = model.gameObject.layer };
        go.transform.SetParent(mt.parent, false);
        go.transform.localPosition = mt.localPosition;
        go.transform.localRotation = mt.localRotation;
        go.transform.localScale = mt.localScale;
        go.SetActive(false);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = new Mesh { name = Name };
        var mr = go.AddComponent<MeshRenderer>();
        var modelR = model.modelRenderer;
        if (modelR != null) { mr.sharedMaterial = modelR.sharedMaterial; mr.shadowCastingMode = modelR.shadowCastingMode; }
        var it = go.AddComponent<CustomizationItem>();
        it.customizationType = model.customizationType;
        it.itemName = Name;
        it.id = id.ToString();
        it.price = 0;
        it.cutHair = false;
        it.clampHairMaterial = model.clampHairMaterial;
        it.modelRenderer = mr;
        it.meshFilter = mf;
        it.clonedMesh = mf.sharedMesh;
        it.colorIndexInRenderer = 0;
        return it;
    }

    // Catégorie des cheveux : celle dont les objets du jeu s'appellent "Hair..."
    internal static int FindHair(Il2CppSystem.Collections.Generic.List<CustomizationParameter> ps)
    {
        for (int i = 0; i < ps.Count; i++)
        {
            var items = ps[i]?.customizationItems;
            if (items == null) continue;
            foreach (var x in items) if (x != null) { if (x.gameObject.name.StartsWith("Hair")) return i; break; }
        }
        return -1;
    }

    internal static Transform Find(PlayerCustomizer c)
    {
        foreach (var t in c.GetComponentsInChildren<Transform>(true)) if (t.name == Name) return t;
        return null;
    }

    // ===================== Modèle =====================
    sealed class Src { public List<string> Groups = new(); public List<Vector3> V = new(); public List<List<int>> Tris = new(); }
    static Src src;

    static Src Load()
    {
        if (src != null) return src;
        src = new Src();
        var path = Path.Combine(ModAssets.Dir, "cosmetics", "ponpons.obj");
        List<int> cur = null;
        if (File.Exists(path))
            foreach (var raw in File.ReadAllLines(path))
            {
                var p = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2) continue;
                if (p[0] == "g") { src.Groups.Add(p[1]); cur = new List<int>(); src.Tris.Add(cur); }
                else if (p[0] == "v") src.V.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                else if (p[0] == "f" && cur != null)
                {
                    var idx = new int[p.Length - 1];
                    for (int i = 1; i < p.Length; i++) idx[i - 1] = int.Parse(p[i].Split('/')[0]) - 1;
                    for (int i = 1; i + 1 < idx.Length; i++) { cur.Add(idx[0]); cur.Add(idx[i]); cur.Add(idx[i + 1]); }
                }
            }
        else Plugin.L.LogWarning("[COSMETIC] " + path + " introuvable");
        return src;
    }

    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    // ===================== Pose =====================
    internal static void Place(PlayerCustomizer c, Func<Cosmetics.Face> measure)
    {
        var obj = Find(c);
        if (obj == null || !obj.gameObject.activeSelf) return;
        var f = measure();
        if (f == null) { Plugin.L.LogWarning("[COSMETIC] ponpons : tête introuvable"); return; }
        Build(obj, f, HatOn(c));
    }

    static bool HatOn(PlayerCustomizer c)
    {
        var ps = c.customizationParameters;
        if (ps == null) return false;
        foreach (var p in ps)
        {
            if (p?.parameterName != "Hat" || p.customizationItems == null) continue;
            foreach (var x in p.customizationItems) if (x != null && x.gameObject.activeSelf) return true;
        }
        return false;
    }

    static void Build(Transform obj, Cosmetics.Face f, bool hat)
    {
        var s = Load();
        // Centre du crâne (repère visage) : sous le sommet d'une demi-largeur de tête, au milieu en largeur et profondeur
        float top = float.MinValue, minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in f.pts) { top = Mathf.Max(top, p.y); minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
        float hw = (maxX - minX) * 0.5f, k = hw / R0;
        var center = new Vector3((minX + maxX) * 0.5f, top - hw, (minZ + maxZ) * 0.5f);
        var dirs = new Vector3[f.pts.Count];
        var dist = new float[f.pts.Count];
        for (int i = 0; i < dirs.Length; i++) { var d = f.pts[i] - center; dist[i] = d.magnitude; dirs[i] = dist[i] > 1e-6f ? d / dist[i] : Vector3.up; }
        var skin = new Dictionary<Vector3, float>();
        float Skin(Vector3 dir)
        {
            if (skin.TryGetValue(dir, out var r)) return r;
            float best = -1f, nearDot = -2f, nearR = hw;
            for (int i = 0; i < dirs.Length; i++)
            {
                float dot = Vector3.Dot(dirs[i], dir);
                if (dot > 0.97f) best = Mathf.Max(best, dist[i] * dot);   // ~14° autour de la direction
                if (dot > nearDot) { nearDot = dot; nearR = dist[i]; }
            }
            return skin[dir] = best > 0f ? best : nearR;
        }

        var verts = new List<Vector3>();
        var tris = new List<int>();
        var mapped = new Vector3?[s.V.Count];
        Vector3 Map(int i)
        {
            if (mapped[i] is Vector3 m) return m;
            var q = s.V[i] - new Vector3(0f, -R0, 0f);
            float len = q.magnitude;
            var d = len > 1e-6f ? q / len : Vector3.up;
            var fp = center + d * (Skin(d) + (len - R0) * k);
            var w = obj.InverseTransformPoint(f.World(fp));
            mapped[i] = w;
            return w;
        }
        for (int g = 0; g < s.Groups.Count; g++)
        {
            if (hat && s.Groups[g] == "chignons") continue;
            var t = s.Tris[g];
            // Deux faces, un sommet par triangle : rendu à facettes comme les coupes du jeu
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                Vector3 a = Map(t[i]), b = Map(t[i + 1]), c = Map(t[i + 2]);
                int n = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(a); verts.Add(c); verts.Add(b);
                for (int j = 0; j < 6; j++) tris.Add(n + j);
            }
        }
        var mesh = new Mesh { name = Name };
        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        obj.GetComponent<MeshFilter>().sharedMesh = mesh;
        var it = obj.GetComponent<CustomizationItem>();
        if (it != null) it.clonedMesh = mesh;
    }
}
