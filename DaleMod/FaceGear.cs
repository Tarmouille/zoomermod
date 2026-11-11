using System;
using System.Collections.Generic;
using System.IO;
using Player.Scripts;
using UnityEngine;

namespace DaleMod;

// 3e lot, objets du visage (tools/make_cosmetics3.py), rigides sous l'os de la tête comme la moustache :
// - Lunettes (catégorie du jeu) : lunettes 3D en carton, monocle.
// - Yeux (catégorie du jeu) : yeux "UwU" (U à la place des yeux, bouche en w, joues roses).
// - Accessoire (catégorie du mod) : masque en papier à la tête de Glucks, avec son élastique autour de la tête.
// Maillages écrits dans le repère "visage" de Cosmetics.Face pour une tête de 0,28 m dont les yeux sont à x = ±0,06 ;
// ils sont recalés en largeur sur les yeux réels. La couleur choisie dans le jeu tombe sur un sous-maillage vide.
// Vue à la première personne : objets "DaleMod_*" sous la tête, donc sur la couche cachée (Cosmetics.MatchLayers).
static class FaceGear
{
    static string T(string fr, string en) => RoleKit.T(fr, en);
    const float Eye = 0.06f;

    enum Fit
    {
        Front,   // pièce rigide posée devant le visage, à la profondeur de la peau au milieu, à hauteur des yeux
        Skin,    // chaque sommet plaqué sur la peau (z = hauteur au-dessus de la peau)
        Paper,   // surface lisse bombée comme la tête, juste au-dessus de la peau (masque en papier)
    }

    sealed class Gear
    {
        public string Name, File, Icon, Fr, En, Category;   // catégorie : "Glasses", "Eyes" (catégories du jeu) ou "Accessory"
        public Fit Fit;
        public bool Strap;                                  // élastique autour de la tête (masque)
        public Func<string, Material> Mat;
    }

    internal const string Mask = "DaleMod_MasqueGlucks";

    static readonly List<Gear> gear = new()
    {
        new Gear { Name = "DaleMod_Lunettes3D", File = "lunettes3d.obj", Icon = "icon_lunettes3d.png", Fr = "Lunettes 3D", En = "3D glasses", Category = "Glasses", Fit = Fit.Front,
                   Mat = g => g == "rouge" ? CosmeticExtras.Lit(new Color(0.85f, 0.08f, 0.1f), 0.85f, 0f) : g == "cyan" ? CosmeticExtras.Lit(new Color(0.1f, 0.72f, 0.88f), 0.85f, 0f) : CosmeticExtras.Lit(new Color(0.95f, 0.95f, 0.93f), 0.1f, 0f) },
        new Gear { Name = "DaleMod_Monocle", File = "monocle.obj", Icon = "icon_monocle.png", Fr = "Monocle", En = "Monocle", Category = "Glasses", Fit = Fit.Skin,
                   Mat = g => g == "verre" ? CosmeticExtras.Lit(new Color(0.78f, 0.88f, 0.95f), 0.95f, 0f) : CosmeticExtras.Lit(new Color(1f, 0.76f, 0.2f), 0.7f, 0.6f) },
        new Gear { Name = "DaleMod_UwU", File = "uwu.obj", Icon = "icon_uwu.png", Fr = "UwU", En = "UwU", Category = "Eyes", Fit = Fit.Skin,
                   Mat = g => g == "joues" ? CosmeticExtras.Lit(new Color(0.96f, 0.55f, 0.62f), 0.1f, 0f) : CosmeticExtras.Lit(new Color(0.04f, 0.04f, 0.05f), 0.2f, 0f) },
        new Gear { Name = Mask, File = "masque.obj", Icon = "icon_masque.png", Fr = "Masque de Glucks", En = "Glucks mask", Category = "Accessory", Fit = Fit.Paper, Strap = true,
                   Mat = g => CosmeticExtras.Textured("cosmetics/masque_glucks.png", 0.1f, 0f) },
    };

    internal static bool IsGear(string n) => gear.Exists(g => g.Name == n);

    // ===================== Ajout aux listes =====================
    internal static void AddItems(Il2CppSystem.Collections.Generic.List<CustomizationParameter> ps, CustomizationParameter accessory, Transform head,
                                  Dictionary<IntPtr, string> itemIcons, Dictionary<IntPtr, string> itemNames)
    {
        foreach (var g in gear)
        {
            var p = g.Category == "Accessory" ? accessory : FindByItem(ps, g.Category);
            if (p is null) { Plugin.L.LogWarning($"[COSMETIC] catégorie {g.Category} introuvable pour {g.Name}"); continue; }
            var go = new GameObject(g.Name);
            go.transform.SetParent(head, false);
            go.SetActive(false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh { name = g.Name };
            var mr = go.AddComponent<MeshRenderer>();
            var src = Load(g.File);
            int subs = src.Groups.Count + (g.Strap ? 1 : 0);
            var list = new Material[subs + 1];
            for (int i = 0; i < src.Groups.Count; i++) list[i] = g.Mat(src.Groups[i]);
            if (g.Strap) list[src.Groups.Count] = CosmeticExtras.Lit(new Color(0.08f, 0.08f, 0.09f), 0.3f, 0f);
            list[subs] = CosmeticExtras.Lit(Color.white, 0f, 0f);   // reçoit la couleur choisie dans le jeu (sous-maillage vide)
            mr.sharedMaterials = list;
            var it = go.AddComponent<CustomizationItem>();
            CustomizationItem model = null;
            foreach (var x in p.customizationItems) if (x != null) { model = x; break; }
            it.customizationType = model != null ? model.customizationType : p.customizationType;
            it.itemName = g.Name;
            it.id = p.customizationItems.Count.ToString();
            it.price = 0;
            it.cutHair = false;
            it.modelRenderer = mr;
            it.meshFilter = mf;
            it.clonedMesh = mf.sharedMesh;
            it.colorIndexInRenderer = subs;
            p.customizationItems.Add(it);
            itemIcons[it.Pointer] = g.Icon;
            itemNames[it.Pointer] = T(g.Fr, g.En);
        }
    }

    // Catégorie du jeu repérée par le nom de ses objets ("Glasses.001", "Eyes.002"…)
    static CustomizationParameter FindByItem(Il2CppSystem.Collections.Generic.List<CustomizationParameter> ps, string prefix)
    {
        foreach (var p in ps)
        {
            if (p?.customizationItems == null) continue;
            foreach (var x in p.customizationItems) if (x != null) { if (x.gameObject.name.StartsWith(prefix)) return p; break; }
        }
        return null;
    }

    // ===================== Maillages =====================
    sealed class Src
    {
        public List<string> Groups = new();
        public List<Vector3> V = new();
        public List<Vector2> UV = new();
        public List<List<(int v, int t)>> Tris = new();
    }
    static readonly Dictionary<string, Src> srcs = new();

    static Src Load(string file)
    {
        if (srcs.TryGetValue(file, out var s)) return s;
        s = new Src();
        var path = Path.Combine(ModAssets.Dir, "cosmetics", file);
        List<(int, int)> cur = null;
        if (File.Exists(path))
            foreach (var raw in File.ReadAllLines(path))
            {
                var p = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2) continue;
                if (p[0] == "g") { s.Groups.Add(p[1]); cur = new List<(int, int)>(); s.Tris.Add(cur); }
                else if (p[0] == "v") s.V.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                else if (p[0] == "vt") s.UV.Add(new Vector2(F(p[1]), F(p[2])));
                else if (p[0] == "f" && cur != null)
                {
                    var idx = new (int, int)[p.Length - 1];
                    for (int i = 1; i < p.Length; i++)
                    {
                        var parts = p[i].Split('/');
                        idx[i - 1] = (int.Parse(parts[0]) - 1, parts.Length > 1 && parts[1].Length > 0 ? int.Parse(parts[1]) - 1 : -1);
                    }
                    for (int i = 1; i + 1 < idx.Length; i++) { cur.Add(idx[0]); cur.Add(idx[i]); cur.Add(idx[i + 1]); }
                }
            }
        else Plugin.L.LogWarning("[COSMETIC] " + path + " introuvable");
        return srcs[file] = s;
    }

    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    // ===================== Pose sur le visage réel =====================
    internal static void Place(PlayerCustomizer c, Transform head, Func<Cosmetics.Face> measure)
    {
        Cosmetics.Face f = null;
        foreach (var g in gear)
        {
            var t = head.Find(g.Name);
            if (t == null || !t.gameObject.activeSelf) continue;
            f ??= measure();
            if (f == null) return;
            Build(t, f, g);
        }
    }

    static void Build(Transform obj, Cosmetics.Face f, Gear g)
    {
        var src = Load(g.File);
        float k = f.k;
        // Largeur recalée sur l'écart réel des yeux (bornée : une mesure ratée ne doit pas tordre l'objet)
        float sx = f.eyeDX > 0f ? Mathf.Clamp(f.eyeDX / (Eye * k), 0.75f, 1.4f) : 1f;
        float frontZ = SkinAt(f, 0f, 0.05f * k);
        var paper = g.Fit == Fit.Paper ? new PaperFit(f, 0.135f * k * sx) : null;
        Vector3 Pos(Vector3 q)
        {
            float x = q.x * k * sx, y = q.y * k;
            float z = g.Fit == Fit.Front ? frontZ + q.z * k
                    : g.Fit == Fit.Paper ? paper.Z(x, y) + q.z * k
                    : SkinZ(f, x, y) + q.z * k + 0.006f * k;
            return obj.InverseTransformPoint(f.World(new Vector3(x, y, z)));
        }
        var verts = new List<Vector3>(); var uvs = new List<Vector2>();
        var subs = new List<int[]>();
        foreach (var tris in src.Tris)
        {
            var list = new List<int>();
            for (int i = 0; i + 2 < tris.Count; i += 3)
                foreach (var id in new[] { tris[i], tris[i + 1], tris[i + 2], tris[i], tris[i + 2], tris[i + 1] })
                {
                    list.Add(verts.Count);
                    verts.Add(Pos(src.V[id.v]));
                    uvs.Add(id.t >= 0 && id.t < src.UV.Count ? src.UV[id.t] : Vector2.zero);
                }
            subs.Add(list.ToArray());
        }
        if (g.Strap) subs.Add(Strap(obj, f, sx, verts, uvs));
        var mesh = new Mesh { name = obj.name };
        mesh.vertices = verts.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.subMeshCount = subs.Count + 1;
        for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
        mesh.SetTriangles(new int[0], subs.Count);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        obj.GetComponent<MeshFilter>().sharedMesh = mesh;
        var it = obj.GetComponent<CustomizationItem>();
        if (it != null) it.clonedMesh = mesh;
        Plugin.L.LogInfo($"[COSMETIC] {g.Name} posé (largeur x{sx:0.00} d'après les yeux)");
    }

    // Peau sous (x, y), lissée : moyenne de la hauteur de peau autour du point (les facettes de la tête ne doivent pas
    // froisser le papier)
    static float SkinZ(Cosmetics.Face f, float x, float y)
    {
        float r = 0.012f * f.k, sum = 0f;
        foreach (var (dx, dy) in new[] { (0f, 0f), (r, 0f), (-r, 0f), (0f, r), (0f, -r) }) sum += SkinAt(f, x + dx, y + dy);
        return sum / 5f;
    }

    // Masque en papier : par tranche de hauteur, un arc de cercle (arrondi de la tête) passant devant la peau, à la plus
    // petite avance qui ne coupe jamais la peau sur toute la largeur du masque ; tranches interpolées et lissées
    sealed class PaperFit
    {
        const int Bins = 32;
        readonly float y0, y1, R;
        readonly float[] z = new float[Bins];
        internal PaperFit(Cosmetics.Face f, float halfW)
        {
            y0 = -0.14f * f.k; y1 = 0.17f * f.k; R = 0.15f * f.k;
            var raw = new float[Bins];
            for (int i = 0; i < Bins; i++)
            {
                float y = y0 + (y1 - y0) * i / (Bins - 1), need = float.MinValue;
                for (int j = -6; j <= 6; j++)
                {
                    float x = halfW * j / 6f;
                    need = Mathf.Max(need, SkinAt(f, x, y) + x * x / (2f * R));
                }
                raw[i] = need + 0.008f * f.k;
            }
            for (int i = 0; i < Bins; i++)   // lissage en hauteur (en gardant le maximum : jamais sous la peau)
                z[i] = Mathf.Max(raw[i], (raw[Mathf.Max(0, i - 1)] + raw[i] + raw[Mathf.Min(Bins - 1, i + 1)]) / 3f);
        }
        internal float Z(float x, float y)
        {
            float t = Mathf.Clamp01((y - y0) / (y1 - y0)) * (Bins - 1);
            int i = Mathf.Min((int)t, Bins - 2);
            return Mathf.Lerp(z[i], z[i + 1], t - i) - x * x / (2f * R);
        }
    }

    static List<Vector3> lastPts;
    static float mid;

    // Hauteur de la peau : sommet le plus en avant autour de (x, y). La tête est peu détaillée : sans sommet tout près,
    // on élargit la recherche (le sommet le plus proche pouvait être à l'arrière du crâne). Sur une tête bombée, le plus
    // haut des sommets voisins est au-dessus des facettes entre eux : rien ne passe sous la peau.
    // Seule la moitié avant de la tête compte : la peau est trouée aux yeux, et derrière le trou il y a l'arrière du crâne.
    static float SkinAt(Cosmetics.Face f, float x, float y)
    {
        if (f.pts != lastPts)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in f.pts) { lo = Mathf.Min(lo, p.z); hi = Mathf.Max(hi, p.z); }
            lastPts = f.pts; mid = (lo + hi) * 0.5f;
        }
        for (float r = 0.03f * f.k; r < 0.2f * f.k; r *= 1.5f)
        {
            float best = float.NegativeInfinity, r2 = r * r;
            foreach (var p in f.pts)
                if (p.z > mid && (p.x - x) * (p.x - x) + (p.y - y) * (p.y - y) < r2 && p.z > best) best = p.z;
            if (!float.IsNegativeInfinity(best)) return best;
        }
        return f.Surface(x, y);
    }

    // Élastique du masque : bande à hauteur des yeux, d'un bord du masque à l'autre en passant derrière la tête
    static int[] Strap(Transform obj, Cosmetics.Face f, float sx, List<Vector3> verts, List<Vector2> uvs)
    {
        float k = f.k, ys = 0.05f * k, half = 0.006f * k;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        var slice = new List<Vector3>();
        foreach (var p in f.pts) if (Mathf.Abs(p.y - ys) < 0.02f * k) { slice.Add(p); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
        var tris = new List<int>();
        if (slice.Count < 8) return tris.ToArray();
        float cz = (minZ + maxZ) * 0.5f;
        float edgeX = 0.128f * k * sx;
        float a0 = Mathf.Atan2(edgeX, SkinZ(f, edgeX, ys) + 0.012f * k - cz);   // angle du bord du masque (0 = devant)
        const int N = 28;
        var ring = new List<Vector3>();
        for (int i = 0; i <= N; i++)
        {
            float a = a0 + (2f * Mathf.PI - 2f * a0) * i / N;
            float best = 0f;
            foreach (var p in slice)
            {
                float pa = Mathf.Atan2(p.x, p.z - cz);
                float d = Mathf.DeltaAngle(pa * Mathf.Rad2Deg, a * Mathf.Rad2Deg);
                if (Mathf.Abs(d) < 12f) best = Mathf.Max(best, new Vector2(p.x, p.z - cz).magnitude);
            }
            if (best <= 0f) best = (maxZ - minZ) * 0.5f;
            best += 0.005f * k;
            float x = Mathf.Sin(a) * best, z = cz + Mathf.Cos(a) * best;
            ring.Add(obj.InverseTransformPoint(f.World(new Vector3(x, ys - half, z))));
            ring.Add(obj.InverseTransformPoint(f.World(new Vector3(x, ys + half, z))));
        }
        // Deux faces avec des sommets à part (des sommets partagés donneraient des normales nulles : rendu noir)
        for (int side = 0; side < 2; side++)
        {
            int b = verts.Count;
            foreach (var x in ring) { verts.Add(x); uvs.Add(Vector2.zero); }
            for (int i = 0; i < N; i++)
            {
                int a = b + 2 * i;
                if (side == 0) tris.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });
                else tris.AddRange(new[] { a, a + 3, a + 1, a, a + 2, a + 3 });
            }
        }
        return tris.ToArray();
    }

    // Masque porté : barbe, sourcils, yeux, lunettes et objets du visage masqués dessous (les cheveux restent)
    internal static void HideUnderMask(PlayerCustomizer c, Transform head)
    {
        var m = head.Find(Mask);
        if (m == null || !m.gameObject.activeSelf) return;
        foreach (var r in c.GetComponentsInChildren<Renderer>(false))
        {
            string n = r.gameObject.name;
            if (n.StartsWith("Beard") || n.StartsWith("Brows") || n.StartsWith("Eyes") || n.StartsWith("Glasses")
                || n == "DaleMod_MoustacheRonde" || (IsGear(n) && n != Mask))
                r.gameObject.SetActive(false);
        }
    }
}
