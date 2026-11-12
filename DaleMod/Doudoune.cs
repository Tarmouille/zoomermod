using System;
using System.Collections.Generic;
using Player.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Doudoune sans manches (catégorie Veste : manager seulement, elle remplace la veste ; la cravate est celle de la
// chemise). Maillage propre, fin (grille autour du torse) : chaque point est posé par un rayon lancé depuis l'axe du
// corps sur le t-shirt du jeu (espace de référence des maillages), puis écarté de la surface ; il prend les os du
// point touché. En bas les rayons sont horizontaux, au-dessus de la poitrine ils montent vers les épaules jusqu'à
// l'encolure. Boudins horizontaux en relief, emmanchures en ellipse (bord ramené sur l'ellipse : pas d'escalier),
// col montant, épaisseur (doublure sombre + biais aux bords). Version ouverte : la grille s'arrête de part et d'autre du
// milieu du devant. Le jeu la déforme selon la corpulence. Deux objets (fermée, ouverte), deux couleurs
// réservées (noir, bleu marine) qui habillent l'extérieur.
static class Doudoune
{
    internal const string Prefix = "DaleMod_Doudoune";
    internal const string Source = "Shirt.002";
    const int NC = 96, NL = 40, NU = 30, Bands = 8, NCol = 4;

    sealed class Sample { public Vector3 pos, dir; public BoneWeight w; public bool hit, arm; }

    sealed class Shape
    {
        public Vector3[] v; public Vector2[] uv; public BoneWeight[] w;
        public int[] outer, lining, tape;
        public int[] weld;   // paires de sommets confondus (fermeture) : même normale
    }

    static readonly Dictionary<string, Shape> shapes = new();
    static readonly Dictionary<string, Texture2D> texs = new();

    internal static CustomizationItem NewItem(PlayerCustomizer c, CustomizationParameter p, int id, int submesh, string name, bool open)
    {
        SkinnedMeshRenderer tee = null;
        foreach (var r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name == Source) { tee = r; break; }
        if (tee == null || tee.sharedMesh == null) { Plugin.L.LogWarning("[DOUDOUNE] t-shirt du jeu introuvable"); return null; }
        Mesh mesh;
        try
        {
            string key = tee.sharedMesh.GetInstanceID() + (open ? "o" : "f");
            if (!shapes.TryGetValue(key, out var s)) shapes[key] = s = Build(tee, submesh, open);
            mesh = new Mesh { name = name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            mesh.vertices = s.v;
            mesh.uv = s.uv;
            mesh.boneWeights = s.w;
            mesh.bindposes = tee.sharedMesh.bindposes;
            mesh.subMeshCount = 3;
            mesh.SetTriangles(s.outer, 0);
            mesh.SetTriangles(s.lining, 1);
            mesh.SetTriangles(s.tape, 2);
            mesh.RecalculateNormals();
            var nrm = mesh.normals;
            for (int k = 0; k + 1 < s.weld.Length; k += 2)
            {
                var n = (nrm[s.weld[k]] + nrm[s.weld[k + 1]]).normalized;
                nrm[s.weld[k]] = nrm[s.weld[k + 1]] = n;
            }
            mesh.normals = nrm;
            mesh.RecalculateBounds();
        }
        catch (Exception e) { Plugin.L.LogError("[DOUDOUNE] " + e); return null; }

        var lining = CosmeticExtras.Lit(new Color(0.07f, 0.07f, 0.08f), 0.2f, 0f);
        var tape = CosmeticExtras.Lit(new Color(0.1f, 0.1f, 0.11f), 0.35f, 0f);

        var go = Object.Instantiate(tee.gameObject, tee.transform.parent, false);
        go.name = name;
        go.SetActive(false);
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.sharedMaterials = new[] { Material(Colors[0].col), lining, tape };
        smr.updateWhenOffscreen = true;
        var it = go.GetComponent<CustomizationItem>() ?? go.AddComponent<CustomizationItem>();
        it.customizationType = p.customizationType;
        it.itemName = name;
        it.id = id.ToString();
        it.price = 0;
        it.cutHair = false;
        it.modelRenderer = smr;
        it.skinnedMeshRenderer = smr;
        it.clonedMesh = mesh;
        it.colorIndexInRenderer = 0;   // la couleur choisie (noir, bleu marine) habille l'extérieur
        try { it.OnAwake(); } catch (Exception e) { Plugin.L.LogWarning("[DOUDOUNE] données de déformation : " + e.Message); }
        return it;
    }

    // Coloris (couleurs de la catégorie Veste réservées aux deux doudounes)
    internal static readonly (string fr, string en, Color32 col)[] Colors =
    {
        ("Noir", "Black", new Color32(38, 38, 42, 255)),
        ("Bleu marine", "Navy blue", new Color32(26, 36, 82, 255)),
    };

    static readonly Dictionary<string, Material> matsByColor = new();

    internal static Material Material(Color32 col)
    {
        string key = $"{col.r}_{col.g}_{col.b}";
        if (matsByColor.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(CosmeticExtras.Lit(Color.white, 0.62f, 0f)) { name = "DaleMod_doudoune_" + key, hideFlags = HideFlags.DontUnloadUnusedAsset };
        m.SetTexture("_BaseMap", Texture(col));
        return matsByColor[key] = m;
    }

    // Le jeu pose une copie du matériau de la couleur choisie ("... (Instance)") : on reconnaît le nom
    internal static bool IsVestMaterial(Material m) => m != null && m.name.StartsWith("DaleMod_doudoune_");

    // Texture d'un boudin (v : un boudin, répété) : couture sombre, léger reflet ; fermeture éclair au milieu du devant
    // (u = 0 et u = 1)
    static Texture2D Texture(Color32 col)
    {
        string key = $"{col.r}_{col.g}_{col.b}";
        if (texs.TryGetValue(key, out var done) && done != null) return done;
        const int W = 256, H = 64;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float f = (y + 0.5f) / H, seam = Mathf.Min(f, 1f - f);
                float k = 0.55f + 0.45f * Mathf.SmoothStep(0f, 1f, seam / 0.09f) + 0.12f * Mathf.Pow(Mathf.Sin(Mathf.PI * f), 8f);
                k *= 0.97f + 0.06f * Mathf.PerlinNoise(x * 0.4f, y * 0.4f);
                var c = new Color32((byte)Mathf.Clamp(col.r * k, 0, 255), (byte)Mathf.Clamp(col.g * k, 0, 255), (byte)Mathf.Clamp(col.b * k, 0, 255), 255);
                int dz = Mathf.Min(x, W - 1 - x);
                if (dz < 1) c = (y / 2) % 2 == 0 ? new Color32(150, 150, 156, 255) : new Color32(60, 60, 64, 255);   // dents
                else if (dz < 2) c = new Color32(18, 18, 20, 255);                                                      // ruban
                px[y * W + x] = c;
            }
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "DaleMod_doudoune_" + key, hideFlags = HideFlags.DontUnloadUnusedAsset, wrapMode = TextureWrapMode.Repeat };
        tex.SetPixels32(px);
        tex.Apply(true);
        return texs[key] = tex;
    }

    static Shape Build(SkinnedMeshRenderer tee, int submesh, bool open)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var src = tee.sharedMesh;
        var b = CosmeticExtras.Frame(tee, out var Arm);
        if (b == null) throw new Exception("os manquants");
        Vector3[] tv = src.vertices;
        BoneWeight[] tw = src.boneWeights;
        var tri = RobeCut.TrianglesOf(src, submesh);
        if (tri == null) throw new Exception("triangles illisibles");
        Vector3 Lateral(Vector3 x) { var r = x - b.axis; return r - b.up * Vector3.Dot(r, b.up); }

        // Rayon typique du torse : unité des épaisseurs
        var radii = new List<float>();
        for (int i = 0; i < tv.Length; i++) if (Arm(i) < 0.3f) radii.Add(Lateral(tv[i]).magnitude);
        if (radii.Count == 0) throw new Exception("pas de torse");
        radii.Sort();
        float R = radii[radii.Count / 2];

        float hem = b.low + (b.chest - b.low) * 0.02f;
        Vector3 Center(float h) => b.axis + b.up * (h - b.H(b.axis));
        Vector3 Dir(float th, float phi) => (Mathf.Cos(th) * b.fwd + Mathf.Sin(th) * b.right) * Mathf.Cos(phi) + b.up * Mathf.Sin(phi);

        // Premier point du t-shirt touché depuis l'intérieur ; os = mélange de ceux du triangle touché
        // Triangles en flottants (les opérations de Vector3 passent par le jeu : beaucoup trop lentes ici)
        int nt = tri.Length / 3;
        var A = new float[nt * 9];
        for (int t = 0; t < nt; t++)
        {
            Vector3 a = tv[tri[3 * t]], p1 = tv[tri[3 * t + 1]], p2 = tv[tri[3 * t + 2]];
            int k = 9 * t;
            A[k] = a.x; A[k + 1] = a.y; A[k + 2] = a.z;
            A[k + 3] = p1.x - a.x; A[k + 4] = p1.y - a.y; A[k + 5] = p1.z - a.z;
            A[k + 6] = p2.x - a.x; A[k + 7] = p2.y - a.y; A[k + 8] = p2.z - a.z;
        }
        Sample Cast(Vector3 o, Vector3 d)
        {
            var s = new Sample { dir = d };
            float best = float.MaxValue; int bt = -1; float bu = 0, bv = 0;
            float ox = o.x, oy = o.y, oz = o.z, dx = d.x, dy = d.y, dz = d.z;
            for (int t = 0, k = 0; t < nt; t++, k += 9)
            {
                float e1x = A[k + 3], e1y = A[k + 4], e1z = A[k + 5], e2x = A[k + 6], e2y = A[k + 7], e2z = A[k + 8];
                float px = dy * e2z - dz * e2y, py = dz * e2x - dx * e2z, pz = dx * e2y - dy * e2x;
                float det = e1x * px + e1y * py + e1z * pz;
                if (det > -1e-14f && det < 1e-14f) continue;
                float inv = 1f / det;
                float tx = ox - A[k], ty = oy - A[k + 1], tz = oz - A[k + 2];
                float u = (tx * px + ty * py + tz * pz) * inv;
                if (u < 0f || u > 1f) continue;
                float qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
                float v = (dx * qx + dy * qy + dz * qz) * inv;
                if (v < 0f || u + v > 1f) continue;
                float dist = (e2x * qx + e2y * qy + e2z * qz) * inv;
                if (dist > 1e-7f && dist < best) { best = dist; bt = 3 * t; bu = u; bv = v; }
            }
            if (bt < 0) return s;
            s.hit = true;
            s.pos = o + d * best;
            int i0 = tri[bt], i1 = tri[bt + 1], i2 = tri[bt + 2];
            s.arm = (Arm(i0) + Arm(i1) + Arm(i2)) / 3f >= 0.35f;
            var acc = new Dictionary<int, float>();
            void Add(BoneWeight x, float k)
            {
                void One(int bi, float wt) { if (wt > 0f) acc[bi] = (acc.TryGetValue(bi, out var o2) ? o2 : 0f) + wt * k; }
                One(x.boneIndex0, x.weight0); One(x.boneIndex1, x.weight1); One(x.boneIndex2, x.weight2); One(x.boneIndex3, x.weight3);
            }
            Add(tw[i0], 1f - bu - bv); Add(tw[i1], bu); Add(tw[i2], bv);
            var top = new List<KeyValuePair<int, float>>(acc);
            top.Sort((x, y) => y.Value.CompareTo(x.Value));
            float sum = 0f; for (int k = 0; k < Mathf.Min(4, top.Count); k++) sum += top[k].Value;
            var bw = new BoneWeight();
            if (top.Count > 0) { bw.boneIndex0 = top[0].Key; bw.weight0 = top[0].Value / sum; }
            if (top.Count > 1) { bw.boneIndex1 = top[1].Key; bw.weight1 = top[1].Value / sum; }
            if (top.Count > 2) { bw.boneIndex2 = top[2].Key; bw.weight2 = top[2].Value / sum; }
            if (top.Count > 3) { bw.boneIndex3 = top[3].Key; bw.weight3 = top[3].Value / sum; }
            s.w = bw;
            return s;
        }

        // Encolure : par angle, la plus haute élévation qui touche encore le t-shirt (lissée)
        const int NA = 72;
        var phiMax = new float[NA];
        var o0 = Center(b.chest);
        for (int a = 0; a < NA; a++)
        {
            float th = a * Mathf.PI * 2f / NA, last = 0f;
            for (float phi = 0f; phi < 1.5f; phi += 0.02f) if (Cast(o0, Dir(th, phi)).hit) last = phi;
            phiMax[a] = last - 0.06f;
        }
        var sm = new float[NA];
        for (int a = 0; a < NA; a++) { float s2 = 0f; for (int k = -3; k <= 3; k++) s2 += phiMax[(a + k + NA) % NA]; sm[a] = s2 / 7f; }
        float PhiMax(float th)
        {
            float x = Mathf.Repeat(th, Mathf.PI * 2f) / (Mathf.PI * 2f) * NA;
            int i = (int)x; float f = x - i;
            return Mathf.Lerp(sm[i % NA], sm[(i + 1) % NA], f);
        }

        // Ligne r (0..NL+NU) : en bas, hauteur ; au-dessus, élévation depuis la poitrine
        Sample Surf(float th, float r)
        {
            if (r <= NL) return Cast(Center(Mathf.Lerp(hem, b.chest, r / NL)), Dir(th, 0f));
            return Cast(o0, Dir(th, PhiMax(th) * (r - NL) / NU));
        }

        // Ouverture du devant (version ouverte) : demi-angle, plus large vers le col
        float Gap(float r) => open ? 0.2f + 0.22f * Mathf.Clamp01((r - (NL + NU * 0.4f)) / (NU * 0.6f)) : 0f;
        float Theta(int i, float r) { float g = Gap(r); return g + (Mathf.PI * 2f - 2f * g) * i / NC; }

        int NR = NL + NU;
        var grid = new Sample[NC + 1, NR + 1];
        for (int r = 0; r <= NR; r++) for (int i = 0; i <= NC; i++) grid[i, r] = Surf(Theta(i, r), r);

        // Emmanchures : ellipse (angle, ligne) autour des points tombés sur les manches, de chaque côté
        var holes = new List<(float th, float r, float a, float bb)>();
        foreach (int sideSign in new[] { 1, -1 })
        {
            float tmin = float.MaxValue, tmax = float.MinValue, rmin = float.MaxValue, rmax = float.MinValue;
            for (int r = 0; r <= NR; r++)
                for (int i = 0; i <= NC; i++)
                {
                    var s = grid[i, r];
                    if (!s.hit || !s.arm) continue;
                    float th = Theta(i, r);
                    if (Mathf.Sign(Mathf.Sin(th)) != sideSign) continue;
                    tmin = Mathf.Min(tmin, th); tmax = Mathf.Max(tmax, th); rmin = Mathf.Min(rmin, r); rmax = Mathf.Max(rmax, r);
                }
            if (tmin > tmax) continue;
            float hr = (rmax - rmin) * 0.5f * 1.12f + 1.5f;
            holes.Add(((tmin + tmax) * 0.5f, (rmin + rmax) * 0.5f + 0.5f, (tmax - tmin) * 0.5f * 1.12f + 0.06f, hr));
        }
        float InHole(float th, float r)
        {
            float m = float.MaxValue;
            foreach (var h in holes) { float dx = (th - h.th) / h.a, dy = (r - h.r) / h.bb; m = Mathf.Min(m, dx * dx + dy * dy); }
            return m;   // < 1 : dans une emmanchure
        }

        bool[,] keep = new bool[NC, NR];
        for (int r = 0; r < NR; r++)
            for (int i = 0; i < NC; i++)
            {
                bool ok = grid[i, r].hit && grid[i + 1, r].hit && grid[i, r + 1].hit && grid[i + 1, r + 1].hit;
                // gardée si un de ses coins est hors des emmanchures (les autres sont ramenés sur l'ellipse)
                keep[i, r] = ok && (InHole(Theta(i, r), r) >= 1f || InHole(Theta(i + 1, r), r) >= 1f
                                    || InHole(Theta(i, r + 1), r + 1) >= 1f || InHole(Theta(i + 1, r + 1), r + 1) >= 1f);
            }
        // Sommets d'une case gardée tombés dans une emmanchure : ramenés sur l'ellipse
        var param = new Vector2[NC + 1, NR + 1];
        for (int r = 0; r <= NR; r++)
            for (int i = 0; i <= NC; i++)
            {
                float th = Theta(i, r), rr = r;
                param[i, r] = new Vector2(th, rr);
                bool used = false;
                for (int di = -1; di <= 0; di++) for (int dr = -1; dr <= 0; dr++)
                {
                    int ci = i + di, cr = r + dr;
                    if (ci >= 0 && ci < NC && cr >= 0 && cr < NR && keep[ci, cr]) used = true;
                }
                if (!used || InHole(th, rr) >= 1f) continue;
                foreach (var h in holes)
                {
                    float dx = (th - h.th) / h.a, dy = (rr - h.r) / h.bb, d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d >= 1f || d < 1e-4f) continue;
                    float nth = h.th + dx / d * h.a * 1.001f, nr = h.r + dy / d * h.bb * 1.001f;
                    var s = Surf(nth, nr);
                    if (s.hit) { grid[i, r] = s; param[i, r] = new Vector2(nth, nr); }
                }
            }

        // Phase des boudins : distance moyenne parcourue le long de la grille
        var acc = new float[NR + 1];
        for (int r = 1; r <= NR; r++)
        {
            float sum = 0f; int n = 0;
            for (int i = 0; i <= NC; i += 4) if (grid[i, r].hit && grid[i, r - 1].hit) { sum += (grid[i, r].pos - grid[i, r - 1].pos).magnitude; n++; }
            acc[r] = acc[r - 1] + (n > 0 ? sum / n : 0f);
        }
        float total = Mathf.Max(1e-9f, acc[NR]);

        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var w = new List<BoneWeight>();
        var outer = new List<int>();
        var idx = new int[NC + 1, NR + 1 + NCol];
        for (int r = 0; r <= NR; r++)
            for (int i = 0; i <= NC; i++)
            {
                var s = grid[i, r];
                if (!s.hit) { idx[i, r] = -1; continue; }
                float band = acc[r] / total * Bands, f = band - Mathf.Floor(band);
                float puff = Mathf.Pow(Mathf.Sin(Mathf.PI * f), 0.55f);
                if (r == 0 || r == NR) puff *= 0.4f;
                var outward = s.dir;
                idx[i, r] = v.Count;
                // fermée : plus d'ampleur au milieu du devant, la cravate de la chemise passe dessous
                float th = Mathf.DeltaAngle(0f, param[i, r].x * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float tie = open ? 0f : 0.16f * Mathf.Exp(-th * th / 0.12f) * Mathf.Clamp01(r / (float)NL);
                v.Add(s.pos + outward * R * (0.075f + tie + 0.085f * puff));
                uv.Add(new Vector2(param[i, r].x / (Mathf.PI * 2f), band));
                w.Add(s.w);
            }
        void Quad(List<int> l, int a, int c1, int c2, int d) { if (a < 0 || c1 < 0 || c2 < 0 || d < 0) return; l.Add(a); l.Add(c1); l.Add(c2); l.Add(c1); l.Add(d); l.Add(c2); }
        for (int r = 0; r < NR; r++)
            for (int i = 0; i < NC; i++)
                if (keep[i, r]) Quad(outer, idx[i, r], idx[i + 1, r], idx[i, r + 1], idx[i + 1, r + 1]);

        // Col montant : quelques rangs au-dessus de l'encolure, un peu rentrés
        float hc = (b.high - b.low) * 0.1f;
        for (int k = 1; k <= NCol; k++)
            for (int i = 0; i <= NC; i++)
            {
                int bi = idx[i, NR];
                if (bi < 0) { idx[i, NR + k] = -1; continue; }
                var p0 = v[bi];
                var inward = -Lateral(p0).normalized;
                float t = (float)k / NCol;
                idx[i, NR + k] = v.Count;
                v.Add(p0 + b.up * hc * t + inward * hc * 0.08f * t * t);
                uv.Add(new Vector2(uv[bi].x, Mathf.Floor(uv[bi].y) + 0.5f));
                w.Add(w[bi]);
            }
        for (int k = 0; k < NCol; k++)
            for (int i = 0; i < NC; i++)
                Quad(outer, idx[i, NR + k], idx[i + 1, NR + k], idx[i, NR + k + 1], idx[i + 1, NR + k + 1]);

        // Fermée : la dernière colonne rejoint la première (même position, coordonnée de texture 1 au lieu de 0)
        var seam = new HashSet<int>();
        var weld = new List<int>();
        if (!open)
            for (int r = 0; r <= NR + NCol; r++)
            {
                int a = idx[0, r], z = idx[NC, r];
                if (a >= 0 && z >= 0) { v[z] = v[a]; seam.Add(a); seam.Add(z); weld.Add(a); weld.Add(z); }
            }

        // Épaisseur : normales de la face extérieure, doublure en retrait, faces retournées
        var tmp = new Mesh();
        tmp.vertices = v.ToArray();
        tmp.triangles = outer.ToArray();
        tmp.RecalculateNormals();
        var on = tmp.normals;
        Object.Destroy(tmp);
        int nv = v.Count;
        float thick = R * 0.05f;
        for (int i = 0; i < nv; i++) { v.Add(v[i] - on[i] * thick); uv.Add(uv[i]); w.Add(w[i]); }
        for (int k = 0, n = weld.Count; k < n; k++) weld.Add(weld[k] + nv);
        var lining = new List<int>();
        for (int t = 0; t < outer.Count; t += 3) { lining.Add(outer[t] + nv); lining.Add(outer[t + 2] + nv); lining.Add(outer[t + 1] + nv); }

        // Biais : bande entre l'extérieur et la doublure sur chaque bord (sommets à part : arête nette)
        var count = new Dictionary<long, int>();
        long Key(int x, int y) => x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
        for (int t = 0; t < outer.Count; t += 3)
            for (int e = 0; e < 3; e++) { long k = Key(outer[t + e], outer[t + (e + 1) % 3]); count[k] = count.TryGetValue(k, out var n) ? n + 1 : 1; }
        var tape = new List<int>();
        int n0 = outer.Count;
        for (int t = 0; t < n0; t += 3)
            for (int e = 0; e < 3; e++)
            {
                int a = outer[t + e], c1 = outer[t + (e + 1) % 3];
                if (count[Key(a, c1)] != 1) continue;
                if (!open && seam.Contains(a) && seam.Contains(c1)) continue;   // fermeture : pas un bord
                int b0 = v.Count;
                v.Add(v[a]); v.Add(v[c1]); v.Add(v[a + nv]); v.Add(v[c1 + nv]);
                uv.Add(uv[a]); uv.Add(uv[c1]); uv.Add(uv[a]); uv.Add(uv[c1]);
                w.Add(w[a]); w.Add(w[c1]); w.Add(w[a]); w.Add(w[c1]);
                // l'arête a->c1 est parcourue dans ce sens par la face extérieure : la bande la parcourt à l'envers
                tape.Add(b0 + 1); tape.Add(b0); tape.Add(b0 + 2);
                tape.Add(b0 + 1); tape.Add(b0 + 2); tape.Add(b0 + 3);
            }

        Plugin.L.LogInfo($"[DOUDOUNE] {(open ? "ouverte" : "fermée")} : {v.Count} sommets, {outer.Count / 3} triangles dehors, {tape.Count / 6} morceaux de biais, {holes.Count} emmanchures, rayon {R:0.0000}, {sw.ElapsedMilliseconds} ms");
        return new Shape { v = v.ToArray(), uv = uv.ToArray(), w = w.ToArray(), outer = outer.ToArray(), lining = lining.ToArray(), tape = tape.ToArray(), weld = weld.ToArray() };
    }
}
