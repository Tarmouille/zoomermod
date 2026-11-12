using System;
using System.Collections.Generic;
using System.IO;
using Player.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// 2e lot de cosmétiques (Cosmetics.cs fait l'ajout aux listes, la sauvegarde et le réseau) :
// - Chapeaux rigides (catégorie Chapeau) : sombrero, casque viking, casque de Solaire. Maillages "chapeau" (origine au
//   sommet de la tête) posés d'après la tête réelle ; chaque matière est un sous-maillage à matériau fixe, et la couleur
//   choisie dans le jeu tombe sur un sous-maillage vide (les chapeaux gardent leurs couleurs). Le casque de Solaire
//   couvre toute la tête : cheveux et visage masqués.
// - Vêtements peints : texture calculée sur le modèle du jeu lui-même (chaque point de la texture est reporté sur le
//   vêtement 3D, où l'on projette le logo de face ou de dos) : t-shirt noir au logo, hoodie de manager au logo dans le
//   dos, haut de Solaire (tabard et soleil, manches en cotte de mailles).
static class CosmeticExtras
{
    static string T(string fr, string en) => RoleKit.T(fr, en);

    sealed class Hat
    {
        public string Name, File, Icon, Fr, En;
        public bool HidesFace;
        public float Margin = 1f;            // grossissement autour de la tête (casque qui l'enveloppe)
        public Func<string, Material> Mat;   // matière d'un groupe du .obj
    }

    static readonly List<Hat> hats = new()
    {
        new Hat { Name = "DaleMod_Sombrero", File = "sombrero.obj", Icon = "icon_sombrero.png", Fr = "Sombrero", En = "Sombrero",
                  Mat = g => g == "bande" ? Textured("cosmetics/sombrero_band.png", 0.1f, 0f) : Textured("cosmetics/straw.png", 0.1f, 0f) },
        new Hat { Name = "DaleMod_Viking", File = "viking.obj", Icon = "icon_viking.png", Fr = "Casque viking", En = "Viking helmet",
                  Mat = g => g == "corne" ? Lit(new Color(0.92f, 0.88f, 0.78f), 0.3f, 0f) : g == "bronze" ? Lit(new Color(0.72f, 0.5f, 0.22f), 0.5f, 0.3f) : Lit(new Color(0.72f, 0.73f, 0.75f), 0.5f, 0.3f) },
        new Hat { Name = "DaleMod_BonnetNoel", File = "santa_hat.obj", Icon = "icon_santa.png", Fr = "Bonnet de Noël", En = "Santa hat",
                  Mat = g => g == "rouge" ? Lit(new Color(0.75f, 0.06f, 0.08f), 0.25f, 0f) : Lit(new Color(0.96f, 0.96f, 0.96f), 0.05f, 0f) },
        new Hat { Name = "DaleMod_Solaire", File = "solaire_helm.obj", Icon = "icon_solaire.png", Fr = "Casque de Solaire", En = "Solaire's helmet", HidesFace = true, Margin = 1.06f,
                  Mat = g => g switch
                  {
                      "fente" => Lit(new Color(0.03f, 0.03f, 0.03f), 0.1f, 0f),
                      "plumet" => Lit(new Color(0.75f, 0.08f, 0.08f), 0.2f, 0f),
                      "rivet" => Lit(new Color(0.42f, 0.42f, 0.44f), 0.5f, 0.3f),
                      "plaque" => Lit(new Color(0.62f, 0.63f, 0.64f), 0.45f, 0.3f),
                      _ => Lit(new Color(0.72f, 0.73f, 0.74f), 0.45f, 0.3f),
                  } },
        // 3e lot (tools/make_cosmetics3.py)
        new Hat { Name = "DaleMod_Couronne", File = "couronne.obj", Icon = "icon_couronne.png", Fr = "Couronne", En = "Crown",
                  Mat = g => g switch
                  {
                      "perle" => Lit(new Color(0.96f, 0.94f, 0.88f), 0.7f, 0f),
                      "rubis" => Lit(new Color(0.75f, 0.04f, 0.1f), 0.85f, 0f),
                      "saphir" => Lit(new Color(0.08f, 0.2f, 0.75f), 0.85f, 0f),
                      _ => Lit(new Color(1f, 0.76f, 0.2f), 0.7f, 0.6f),
                  } },
        new Hat { Name = "DaleMod_Bouffon", File = "bouffon.obj", Icon = "icon_bouffon.png", Fr = "Bonnet de bouffon", En = "Jester hat",
                  Mat = g => g switch
                  {
                      "rose" => Lit(new Color(0.93f, 0.35f, 0.66f), 0.2f, 0f),
                      "violet" => Lit(new Color(0.48f, 0.2f, 0.72f), 0.2f, 0f),
                      _ => Lit(new Color(1f, 0.78f, 0.22f), 0.65f, 0.5f),   // bandeau et grelots
                  } },
        new Hat { Name = "DaleMod_CasqueAudio", File = "casque_audio.obj", Icon = "icon_casque_audio.png", Fr = "Casque audio", En = "Headset",
                  Mat = g => g == "coussin" ? Lit(new Color(0.2f, 0.2f, 0.22f), 0.05f, 0f) : g == "micro" ? Lit(new Color(0.3f, 0.3f, 0.33f), 0.3f, 0f) : Lit(new Color(0.07f, 0.07f, 0.08f), 0.5f, 0f) },
        new Hat { Name = "DaleMod_Pirate", File = "pirate.obj", Icon = "icon_pirate.png", Fr = "Chapeau de pirate", En = "Pirate hat",
                  Mat = g => g switch
                  {
                      "galon" => Lit(new Color(1f, 0.76f, 0.2f), 0.65f, 0.5f),
                      "crane" => Lit(new Color(0.95f, 0.94f, 0.9f), 0.2f, 0f),
                      _ => Lit(new Color(0.06f, 0.06f, 0.07f), 0.15f, 0f),
                  } },
        new Hat { Name = "DaleMod_HautDeForme", File = "haut_de_forme.obj", Icon = "icon_haut_de_forme.png", Fr = "Haut-de-forme", En = "Top hat",
                  Mat = g => g == "ruban" ? Lit(new Color(0.4f, 0.05f, 0.08f), 0.6f, 0f) : Lit(new Color(0.05f, 0.05f, 0.06f), 0.35f, 0f) },
    };

    internal static bool IsHatName(string n) => hats.Exists(h => h.Name == n);

    static readonly Dictionary<string, Material> mats = new();

    internal static Material Lit(Color c, float smooth, float metal)
    {
        string key = $"lit_{ColorUtility.ToHtmlStringRGB(c)}_{smooth}_{metal}";
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "DaleMod_" + key, hideFlags = HideFlags.DontUnloadUnusedAsset };
        m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        return mats[key] = m;
    }

    internal static Material Textured(string tex, float smooth, float metal)
    {
        string key = $"tex_{tex}";
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "DaleMod_" + Path.GetFileNameWithoutExtension(tex), hideFlags = HideFlags.DontUnloadUnusedAsset };
        m.SetColor("_BaseColor", Color.white);
        var t = ModAssets.LoadTexture(tex);
        if (t != null) m.SetTexture("_BaseMap", t);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        return mats[key] = m;
    }

    // ===================== Ajout aux listes =====================
    internal static void AddHats(PlayerCustomizer c, CustomizationParameter hatParam, Transform head, Dictionary<IntPtr, string> itemIcons, Dictionary<IntPtr, string> itemNames)
    {
        // Les chapeaux du jeu écrasent les cheveux dessous (matériau spécial posé sur les cheveux) : on reprend le réglage
        CustomizationItem squash = null;
        foreach (var x in hatParam.customizationItems)
        {
            if (x == null) continue;
            Plugin.L.LogInfo($"[COSMETIC] chapeau du jeu {x.gameObject.name} : écrase les cheveux {x.cutHair}, matériau {(x.clampHairMaterial == null ? "-" : x.clampHairMaterial.name)}");
            if (squash == null && x.cutHair && x.clampHairMaterial != null) squash = x;
            if (x.gameObject.name == "HatCowBoy" && x.clampHairMaterial != null) squash = x;
        }
        foreach (var h in hats)
        {
            var go = new GameObject(h.Name);
            go.transform.SetParent(head, false);
            go.SetActive(false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh { name = h.Name };
            var mr = go.AddComponent<MeshRenderer>();
            var groups = HatGroups(h.File);
            var list = new Material[groups.Count + 1];
            for (int i = 0; i < groups.Count; i++) list[i] = h.Mat(groups[i]);
            list[groups.Count] = Lit(Color.white, 0f, 0f);   // reçoit la couleur choisie dans le jeu (sous-maillage vide)
            mr.sharedMaterials = list;
            var it = go.AddComponent<CustomizationItem>();
            CustomizationItem model = null;
            foreach (var x in hatParam.customizationItems) if (x != null) { model = x; break; }
            it.customizationType = model != null ? model.customizationType : hatParam.customizationType;
            it.itemName = h.Name;
            it.id = hatParam.customizationItems.Count.ToString();
            it.price = 0;
            it.cutHair = squash != null;
            if (squash != null) it.clampHairMaterial = squash.clampHairMaterial;
            it.modelRenderer = mr;
            it.meshFilter = mf;
            it.clonedMesh = mf.sharedMesh;
            it.colorIndexInRenderer = groups.Count;
            hatParam.customizationItems.Add(it);
            itemIcons[it.Pointer] = h.Icon;
            itemNames[it.Pointer] = T(h.Fr, h.En);
        }
    }

    // ===================== Maillages des chapeaux =====================
    sealed class HatSrc { public List<string> Groups = new(); public List<Vector3> V = new(); public List<List<int>> Tris = new(); }
    static readonly Dictionary<string, HatSrc> hatSrc = new();

    static HatSrc LoadHat(string file)
    {
        if (hatSrc.TryGetValue(file, out var h)) return h;
        h = new HatSrc();
        var path = Path.Combine(ModAssets.Dir, "cosmetics", file);
        List<int> cur = null;
        if (File.Exists(path))
            foreach (var raw in File.ReadAllLines(path))
            {
                var p = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2) continue;
                if (p[0] == "g") { h.Groups.Add(p[1]); cur = new List<int>(); h.Tris.Add(cur); }
                else if (p[0] == "v") h.V.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                else if (p[0] == "f" && cur != null)
                {
                    var idx = new int[p.Length - 1];
                    for (int i = 1; i < p.Length; i++) idx[i - 1] = int.Parse(p[i].Split('/')[0]) - 1;
                    for (int i = 1; i + 1 < idx.Length; i++) { cur.Add(idx[0]); cur.Add(idx[i]); cur.Add(idx[i + 1]); }
                }
            }
        else Plugin.L.LogWarning("[COSMETIC] " + path + " introuvable");
        return hatSrc[file] = h;
    }

    static List<string> HatGroups(string file) => LoadHat(file).Groups;
    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    // Pose des chapeaux portés : repère de la tête (sommet, centre, largeur) mesuré sur la tête réelle
    internal static void PlaceHats(PlayerCustomizer c, Transform head, Func<Cosmetics.Face> measure)
    {
        Cosmetics.Face f = null;
        foreach (var h in hats)
        {
            var t = head.Find(h.Name);
            if (t == null || !t.gameObject.activeSelf) continue;
            f ??= measure();
            if (f == null) return;
            Build(t, f, LoadHat(h.File), h.Margin);
        }
    }

    static void Build(Transform obj, Cosmetics.Face f, HatSrc src, float margin)
    {
        float top = float.MinValue, minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in f.pts) { top = Mathf.Max(top, p.y); minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }
        foreach (var p in f.pts) { minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
        float hw = (maxX - minX) * 0.5f, hd = (maxZ - minZ) * 0.5f;
        float s = hw / 0.14f * margin;            // les chapeaux sont faits pour une tête de 28 cm de large
        float sz = Mathf.Max(hd, hw) / 0.14f * margin;   // et aussi profonde : on suit la profondeur réelle
        float zc = (minZ + maxZ) * 0.5f;
        // Sous-maillages à deux faces (sommets à part) + un sous-maillage vide pour la couleur du jeu ; UV en cylindre
        var verts = new List<Vector3>(); var uvs = new List<Vector2>();
        var subs = new List<int[]>();
        foreach (var tris in src.Tris)
        {
            var list = new List<int>();
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                int[] ids = { tris[i], tris[i + 1], tris[i + 2], tris[i], tris[i + 2], tris[i + 1] };
                foreach (var id in ids)
                {
                    var q = src.V[id];
                    var w = f.World(new Vector3(q.x * s, top + q.y * s, zc + q.z * sz));
                    list.Add(verts.Count);
                    verts.Add(obj.InverseTransformPoint(w));
                    uvs.Add(new Vector2(Mathf.Atan2(q.x, q.z) / (2f * Mathf.PI) * 3f, q.y * 8f));
                }
            }
            subs.Add(list.ToArray());
        }
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
    }

    // Casque de Solaire porté : cheveux, barbe, sourcils, yeux, lunettes et accessoires du visage masqués
    internal static void HideFaceUnderHelmet(PlayerCustomizer c, Transform head)
    {
        bool hide = false;
        foreach (var h in hats)
            if (h.HidesFace) { var t = head.Find(h.Name); if (t != null && t.gameObject.activeSelf) hide = true; }
        if (!hide) return;
        foreach (var r in c.GetComponentsInChildren<Renderer>(false))
        {
            string n = r.gameObject.name;
            if (n.StartsWith("Hair") || n.StartsWith("Beard") || n.StartsWith("Brows") || n.StartsWith("Eyes") || n.StartsWith("Glasses")
                || n == Ponpons.Name || n == "DaleMod_MoustacheRonde" || n == "DaleMod_NezRouge" || FaceGear.IsGear(n))
                r.gameObject.SetActive(false);
        }
    }

    // ===================== Vêtements peints =====================
    sealed class Img { public int W, H; public byte[] Px; }
    static readonly Dictionary<string, Img> imgs = new();

    static Img LoadRgba(string file)
    {
        if (imgs.TryGetValue(file, out var im)) return im;
        var path = Path.Combine(ModAssets.Dir, "cosmetics", file);
        if (!File.Exists(path)) { Plugin.L.LogWarning("[COSMETIC] " + path + " introuvable"); return imgs[file] = null; }
        var b = File.ReadAllBytes(path);
        if (b.Length > 2 && b[0] == 0x1F && b[1] == 0x8B)   // compressé (gzip) par les scripts de tools/
        {
            using var z = new System.IO.Compression.GZipStream(new MemoryStream(b), System.IO.Compression.CompressionMode.Decompress);
            using var ms = new MemoryStream();
            z.CopyTo(ms);
            b = ms.ToArray();
        }
        im = new Img { W = BitConverter.ToInt32(b, 0), H = BitConverter.ToInt32(b, 4), Px = new byte[b.Length - 8] };
        Buffer.BlockCopy(b, 8, im.Px, 0, im.Px.Length);
        return imgs[file] = im;
    }

    // Couleur moyenne des coins d'une image (les pixels opaques seulement) ; fond transparent : couleur par défaut
    static Color32 CornerColor(Img im, Color32 fallback)
    {
        long r = 0, g = 0, b = 0, n = 0;
        int s = Mathf.Max(1, Mathf.Min(im.W, im.H) / 20);
        foreach (var (x0, y0) in new[] { (0, 0), (im.W - s, 0), (0, im.H - s), (im.W - s, im.H - s) })
            for (int y = y0; y < y0 + s; y++)
                for (int x = x0; x < x0 + s; x++)
                {
                    int i = (y * im.W + x) * 4;
                    if (im.Px[i + 3] < 200) continue;
                    r += im.Px[i]; g += im.Px[i + 1]; b += im.Px[i + 2]; n++;
                }
        return n < s * s ? fallback : new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
    }

    // Couleur d'une image en (u, v) dans [0,1] (v vers le haut) ; null hors de l'image
    static Color32? Sample(Img im, float u, float v, bool wrap = false)
    {
        if (im == null) return null;
        if (wrap) { u -= Mathf.Floor(u); v -= Mathf.Floor(v); }
        if (u < 0f || u >= 1f || v < 0f || v >= 1f) return null;
        int x = (int)(u * im.W), y = (int)((1f - v) * im.H);
        x = Mathf.Clamp(x, 0, im.W - 1); y = Mathf.Clamp(y, 0, im.H - 1);
        int i = (y * im.W + x) * 4;
        return new Color32(im.Px[i], im.Px[i + 1], im.Px[i + 2], im.Px[i + 3]);
    }

    static (Color32, int) Pick(Color32 baseC, Color32? motif) => motif != null && motif.Value.a > 20 ? (Over(baseC, motif), 2) : (baseC, 0);

    static Color32 Over(Color32 a, Color32? b)
    {
        if (b == null) return a;
        var c = b.Value; float t = c.a / 255f;
        return new Color32((byte)(a.r + (c.r - a.r) * t), (byte)(a.g + (c.g - a.g) * t), (byte)(a.b + (c.b - a.b) * t), 255);
    }

    // Repère du vêtement (espace de référence du maillage, comme le peignoir)
    internal sealed class Body
    {
        public Vector3 up, fwd, right, axis;
        public float chest, upper, halfW, depthC, low, high;
        public float[] widths;   // demi-largeur du torse (hors bras) par tranche de hauteur
        public Vector3 handL, handR, footL, footR;
        public float neck;
        public float WidthAt(float h)
        {
            if (widths == null) return halfW;
            int i = Mathf.Clamp((int)((h - low) / Mathf.Max(1e-9f, high - low) * widths.Length), 0, widths.Length - 1);
            return widths[i] > 0f ? widths[i] : halfW;
        }
        public float Side(Vector3 p) => Vector3.Dot(p - axis, right);
        public float Front(Vector3 p) => Vector3.Dot(p - axis, fwd);
        public float H(Vector3 p) => Vector3.Dot(p, up);
    }

    // Point de la texture -> point du vêtement : peint(position, normale, part des bras, uv) pour chaque texel couvert
    // Renvoie la couleur et sa priorité (0 fond, 1 zone, 2 motif) : un texel partagé garde la plus haute
    delegate (Color32 c, int prio) Painter(Body b, Vector3 pos, Vector3 normal, float arm, Vector2 uv);

    static readonly Dictionary<string, Texture2D> baked = new();

    // Repère du vêtement et part des bras de chaque sommet (null si os manquants)
    internal static Body Frame(SkinnedMeshRenderer smr, out Func<int, float> armOf)
    {
        armOf = null;
        var mesh = smr.sharedMesh;
        var bones = smr.bones;
        Matrix4x4[] bind = mesh.bindposes;
        int Bone(string n) { for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == n) return i; return -1; }
        Vector3 P(int i) => bind[i].inverse.MultiplyPoint3x4(Vector3.zero);
        int spine = Bone("spine"), spine2 = Bone("spine.002"), spine3 = Bone("spine.003"), head = Bone("head"), footL = Bone("foot.L"), toeL = Bone("toe.L");
        if (spine < 0 || spine2 < 0 || spine3 < 0 || head < 0) return null;
        var b = new Body { axis = P(spine), up = (P(head) - P(spine)).normalized };
        var fwd = footL >= 0 && toeL >= 0 ? P(toeL) - P(footL) : Vector3.forward;
        b.fwd = (fwd - b.up * Vector3.Dot(fwd, b.up)).normalized;
        b.right = Vector3.Cross(b.up, b.fwd);
        b.chest = b.H(P(spine2)) + (b.H(P(spine3)) - b.H(P(spine2))) * 0.55f;
        int hl = Bone("hand.L"), hr = Bone("hand.R"), fl = Bone("foot.L"), fr2 = Bone("foot.R");
        if (hl >= 0) b.handL = P(hl); if (hr >= 0) b.handR = P(hr);
        if (fl >= 0) b.footL = P(fl); if (fr2 >= 0) b.footR = P(fr2);
        b.neck = b.H(P(head));
        b.upper = b.H(P(spine3));

        var armBones = new HashSet<int>();
        foreach (var n in new[] { "upper_arm.L", "upper_arm.R", "forearm.L", "forearm.R", "hand.L", "hand.R" })   // pas les épaules : le haut du torse leur est lié
        { int k = Bone(n); if (k >= 0) armBones.Add(k); }
        Vector3[] v = mesh.vertices;
        BoneWeight[] w = mesh.boneWeights;
        float Arm(int i)
        {
            var bw = w[i]; float a = 0f;
            if (armBones.Contains(bw.boneIndex0)) a += bw.weight0;
            if (armBones.Contains(bw.boneIndex1)) a += bw.weight1;
            if (armBones.Contains(bw.boneIndex2)) a += bw.weight2;
            if (armBones.Contains(bw.boneIndex3)) a += bw.weight3;
            return a;
        }
        for (int i0 = 0; i0 < v.Length; i0++)
            if (Mathf.Abs(b.H(v[i0]) - b.chest) < (b.upper - b.H(P(spine))) * 0.12f && Arm(i0) < 0.3f) b.halfW = Mathf.Max(b.halfW, Mathf.Abs(b.Side(v[i0])));
        if (b.halfW <= 0f) b.halfW = (b.upper - b.H(P(spine))) * 0.5f;
        // Profondeur du centre du torse à la poitrine, largeur du torse par tranche de hauteur
        float sum = 0f; int cnt = 0;
        b.low = float.MaxValue; b.high = float.MinValue;
        for (int i0 = 0; i0 < v.Length; i0++)
        {
            if (Arm(i0) >= 0.3f) continue;
            b.low = Mathf.Min(b.low, b.H(v[i0])); b.high = Mathf.Max(b.high, b.H(v[i0]));
            if (Mathf.Abs(b.H(v[i0]) - b.chest) < (b.upper - b.H(P(spine))) * 0.15f) { sum += b.Front(v[i0]); cnt++; }
        }
        b.depthC = cnt > 0 ? sum / cnt : 0f;
        b.widths = new float[16];
        for (int i0 = 0; i0 < v.Length; i0++)
        {
            if (Arm(i0) >= 0.3f) continue;
            int k = Mathf.Clamp((int)((b.H(v[i0]) - b.low) / Mathf.Max(1e-9f, b.high - b.low) * 16), 0, 15);
            b.widths[k] = Mathf.Max(b.widths[k], Mathf.Abs(b.Side(v[i0])));
        }
        armOf = Arm;
        return b;
    }

    static Texture2D Bake(SkinnedMeshRenderer smr, int submesh, Color32 background, Painter paint, string key)
    {
        if (baked.TryGetValue(key, out var done) && done != null) return done;
        var mesh = smr.sharedMesh;
        var b = Frame(smr, out var Arm);
        if (b == null) { Plugin.L.LogWarning("[COSMETIC] os manquants pour peindre " + key); return null; }
        Vector3[] v = mesh.vertices;
        Vector2[] uv = mesh.uv;

        var tri = RobeCut.TrianglesOf(mesh, submesh);
        if (tri == null) { Plugin.L.LogWarning("[COSMETIC] triangles illisibles pour " + key); return null; }
        const int Size = 1024;
        var px = new Color32[Size * Size];
        var prio = new sbyte[Size * Size];
        var facing = new sbyte[Size * Size];   // relevé : 1 devant, -1 dos, 2 les deux
        for (int i = 0; i < px.Length; i++) { px[i] = background; prio[i] = -1; }
        for (int t = 0; t + 2 < tri.Length; t += 3)
        {
            int a = tri[t], c1 = tri[t + 1], c2 = tri[t + 2];
            var n = Vector3.Cross(v[c1] - v[a], v[c2] - v[a]).normalized;
            float arm = (Arm(a) + Arm(c1) + Arm(c2)) / 3f;
            Vector2 A = uv[a] * Size, B = uv[c1] * Size, C = uv[c2] * Size;
            int x0 = Mathf.Max(0, (int)Mathf.Floor(Mathf.Min(A.x, Mathf.Min(B.x, C.x))) - 1), x1 = Mathf.Min(Size - 1, (int)Mathf.Ceil(Mathf.Max(A.x, Mathf.Max(B.x, C.x))) + 1);
            int y0 = Mathf.Max(0, (int)Mathf.Floor(Mathf.Min(A.y, Mathf.Min(B.y, C.y))) - 1), y1 = Mathf.Min(Size - 1, (int)Mathf.Ceil(Mathf.Max(A.y, Mathf.Max(B.y, C.y))) + 1);
            float den = (B.y - C.y) * (A.x - C.x) + (C.x - B.x) * (A.y - C.y);
            if (Mathf.Abs(den) < 1e-8f) continue;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px0 = x + 0.5f, py0 = y + 0.5f;
                    float l1 = ((B.y - C.y) * (px0 - C.x) + (C.x - B.x) * (py0 - C.y)) / den;
                    float l2 = ((C.y - A.y) * (px0 - C.x) + (A.x - C.x) * (py0 - C.y)) / den;
                    float l3 = 1f - l1 - l2;
                    const float e = -0.02f;   // un peu au-delà des bords : pas de liseré aux coutures
                    if (l1 < e || l2 < e || l3 < e) continue;
                    var pos = v[a] * l1 + v[c1] * l2 + v[c2] * l3;
                    var tuv = uv[a] * l1 + uv[c1] * l2 + uv[c2] * l3;
                    int k = y * Size + x;
                    var r = paint(b, pos, n, arm, tuv);
                    sbyte fc = (sbyte)(Vector3.Dot(n, b.fwd) > 0.2f ? 1 : Vector3.Dot(n, b.fwd) < -0.2f ? -1 : 0);
                    if (fc != 0) facing[k] = facing[k] == 0 || facing[k] == fc ? fc : (sbyte)2;
                    if (r.prio >= prio[k]) { px[k] = r.c; prio[k] = (sbyte)r.prio; }
                }
        }
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "DaleMod_" + key, hideFlags = HideFlags.DontUnloadUnusedAsset, wrapMode = TextureWrapMode.Repeat };
        tex.SetPixels32(px);
        tex.Apply(true);
        int both = 0, motif = 0;
        for (int i = 0; i < px.Length; i++) { if (facing[i] == 2) both++; if (prio[i] == 2) motif++; }
        Plugin.L.LogInfo($"[COSMETIC] texture peinte : {key} ({tri.Length / 3} triangles, torse {b.halfW:0.0000}, {motif} texels de motif, {both} texels partagés devant/dos)");
        return baked[key] = tex;
    }

    // Projection d'une image de face (front = true) ou de dos, centrée à la hauteur hc, de largeur size (hauteur selon
    // les proportions de l'image)
    static Color32? Decal(Body b, Vector3 pos, Vector3 n, Img img, bool front, float hc, float size)
    {
        float d = b.Front(pos) - b.depthC;
        if (front ? d <= 0f : d >= 0f) return null;
        float h = img != null && img.W > 0 ? size * img.H / img.W : size;
        // vue de face, la gauche de l'image est à droite du personnage (vue de dos, à sa gauche) : pas d'image en miroir
        float u = 0.5f + (front ? -b.Side(pos) : b.Side(pos)) / size;
        float vv = 0.5f + (b.H(pos) - hc) / h;
        return Sample(img, u, vv);
    }

    internal static void AddClothes(PlayerCustomizer c, Il2CppSystem.Collections.Generic.List<CustomizationParameter> ps, GameObject root,
                                    Dictionary<IntPtr, string> colorIcons, Func<GameObject, CustomizationParameter, int, string, Material, Il2CppSystem.Collections.Generic.List<CustomizationItem>, ColorItem> newColor)
    {
        int shirt = -1, pants = -1, jacket = -1;
        for (int i = 0; i < ps.Count; i++)
        {
            var n = ps[i]?.parameterName;
            if (n == "Shirt") shirt = i; else if (n == "Pants") pants = i; else if (n == "Jacket") jacket = i;
        }
        SkinnedMeshRenderer Smr(string name) { foreach (var r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name == name) return r; return null; }
        CustomizationItem Item(int p, string goName) { foreach (var it in ps[p].customizationItems) if (it != null && it.gameObject.name == goName) return it; return null; }
        ColorItem ColorNamed(int p, string name) { foreach (var ci in ps[p].colorItems) if (ci != null && ci.itemName == name) return ci; return null; }
        Material From(Material model, Texture2D tex, string name)
        {
            var m = new Material(model) { name = "DaleMod_" + name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            return m;
        }
        int SubOf(CustomizationItem it) => it != null ? it.colorIndexInRenderer : 0;

        try
        {
            // T-shirt noir au logo (modèles des t-shirts à motif du jeu)
            var ddss = shirt >= 0 ? ColorNamed(shirt, "DDSS") : null;
            var tee = Smr("Shirt.002");
            if (ddss != null && tee != null)
            {
                var logo = LoadRgba("logo_tee.rgba");
                var tex = Bake(tee, SubOf(Item(shirt, "Shirt.002")), new Color32(22, 22, 24, 255),
                    (b, pos, n, arm, uv) => Pick(new Color32(22, 22, 24, 255), arm < 0.6f ? Decal(b, pos, n, logo, true, b.chest, b.halfW * 1.1f) : null), "tee_logo");
                if (tex != null)
                {
                    var ci = newColor(root, ps[shirt], ps[shirt].colorItems.Count, T("Noir au logo", "Black logo tee"), From(ddss.material, tex, "tee_logo"), ddss.strictItems);
                    colorIcons[ci.Pointer] = "cosmetics/swatch_tee_logo.png";
                    ps[shirt].colorItems.Add(ci);
                }
            }

            // T-shirts imprimés : image entière sur le devant, t-shirt de la couleur du fond de l'image (sans couleur
            // donnée : celle des coins de l'image, ou bleu glace si son fond est transparent)
            if (ddss != null && tee != null)
                foreach (var (file, fr, en, given) in new (string, string, string, Color32?)[]
                {
                    ("tshirt_tarm", "Kaiju chat", "Kaiju cat", new Color32(244, 232, 215, 255)),
                    ("tshirt_tarm_1", "Bouffer en entier", "Swallow whole", new Color32(0, 0, 0, 255)),
                    ("tshirt_pingu", "Pingu", "Pingu", null),
                })
                {
                    var img = LoadRgba(file + ".rgba");
                    if (img == null) continue;
                    var bg = given ?? CornerColor(img, new Color32(200, 228, 245, 255));
                    var tex = Bake(tee, SubOf(Item(shirt, "Shirt.002")), bg,
                        (b, pos, n, arm, uv) => Pick(bg, arm < 0.6f ? Decal(b, pos, n, img, true, b.chest - b.halfW * 0.2f, b.halfW * 1.35f) : null), file);
                    if (tex == null) continue;
                    var ci = newColor(root, ps[shirt], ps[shirt].colorItems.Count, T(fr, en), From(ddss.material, tex, file), ddss.strictItems);
                    colorIcons[ci.Pointer] = "cosmetics/swatch_" + file + ".png";
                    ps[shirt].colorItems.Add(ci);
                }

            // Cotte de mailles (chemise) : motif uniforme, sans souci des zones de texture partagées ; le tabard de Solaire
            // se met par-dessus (catégorie Manteau)
            if (shirt >= 0 && ps[shirt].colorItems.Count > 0)
            {
                var m = new Material(ps[shirt].colorItems[0].material) { name = "DaleMod_chainmail_shirt", hideFlags = HideFlags.DontUnloadUnusedAsset };
                var t = ModAssets.LoadTexture("cosmetics/chainmail.png");
                if (t != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t); m.SetTextureScale("_BaseMap", new Vector2(12f, 12f)); }
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.5f);
                var ci = newColor(root, ps[shirt], ps[shirt].colorItems.Count, T("Cotte de mailles", "Chainmail"), m, null);
                colorIcons[ci.Pointer] = "cosmetics/swatch_chainmail.png";
                ps[shirt].colorItems.Add(ci);
            }

            // Pantalon de Solaire : cotte de mailles
            if (pants >= 0 && ps[pants].colorItems.Count > 0)
            {
                var m = new Material(ps[pants].colorItems[0].material) { name = "DaleMod_chainmail", hideFlags = HideFlags.DontUnloadUnusedAsset };
                var t = ModAssets.LoadTexture("cosmetics/chainmail.png");
                if (t != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t); m.SetTextureScale("_BaseMap", new Vector2(12f, 12f)); }
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.5f);
                var ci = newColor(root, ps[pants], ps[pants].colorItems.Count, T("Mailles (Solaire)", "Chainmail (Solaire)"), m, null);
                colorIcons[ci.Pointer] = "cosmetics/swatch_chainmail.png";
                ps[pants].colorItems.Add(ci);
            }

            // Peignoir façon gilet du Dude (The Big Lebowski) : bandes horizontales d'après la hauteur sur le corps,
            // symétriques autour de l'ouverture ; manches en maille unie
            int coat = -1;
            for (int i = 0; i < ps.Count; i++) if (ps[i]?.parameterName == "Coat") coat = i;
            var robe = Smr(Robe.Name);
            if (coat >= 0 && robe != null)
            {
                var pat = LoadRgba("lebowski.rgba");
                var tex = Bake(robe, 0, new Color32(200, 174, 134, 255), (b, pos, n, arm, uv) =>
                {
                    float ang = Mathf.Abs(Mathf.Atan2(b.Side(pos), b.Front(pos) - b.depthC));
                    float u = ang / 1.8f;
                    if (arm > 0.5f) return (Sample(pat, u, 0.3f, true) ?? new Color32(200, 174, 134, 255), 0);
                    float t = (b.high - b.H(pos)) / Mathf.Max(1e-6f, b.high - b.low);
                    // le bas uni l'emporte sur les texels partagés avec le haut (sinon, bouts de motif près de l'ourlet)
                    return (Sample(pat, u, 1f - Mathf.Clamp(t, 0.001f, 0.999f), true) ?? new Color32(200, 174, 134, 255), t > 0.58f ? 2 : 1);
                }, "robe_lebowski");
                if (tex != null)
                {
                    var strict = new Il2CppSystem.Collections.Generic.List<CustomizationItem>();
                    var it = Item(coat, Robe.Name);
                    if (it != null) strict.Add(it);
                    var m = new Material(Lit(Color.white, 0.05f, 0f)) { name = "DaleMod_lebowski", hideFlags = HideFlags.DontUnloadUnusedAsset };
                    m.SetTexture("_BaseMap", tex);
                    var ci = newColor(root, ps[coat], ps[coat].colorItems.Count, "Lebowski", m, strict);
                    colorIcons[ci.Pointer] = "cosmetics/swatch_lebowski.png";
                    ps[coat].colorItems.Add(ci);
                }
            }

            // Noël (rouge velours, fourrure blanche aux bords) : t-shirt manches longues, jupe, pantalon
            {
                var velvet = new Color32(178, 18, 26, 255);
                Color32 Velvet(Vector2 uv) { float n = Mathf.PerlinNoise(uv.x * 40f, uv.y * 40f); byte k = (byte)(n * 22f); return new Color32((byte)(velvet.r - k), (byte)Mathf.Max(0, velvet.g - k / 3), (byte)Mathf.Max(0, velvet.b - k / 3), 255); }
                Color32 Fur(Vector2 uv) { float n = Mathf.PerlinNoise(uv.x * 90f, uv.y * 90f); byte k = (byte)(222 + n * 33f); return new Color32(k, k, (byte)(k - 4), 255); }
                void AddNoel(int p, string goName, Painter paint, string key)
                {
                    var r = Smr(goName);
                    var it = p >= 0 ? Item(p, goName) : null;
                    if (r == null || it == null || ps[p].colorItems.Count == 0) return;
                    var tex = Bake(r, SubOf(it), velvet, paint, key);
                    if (tex == null) return;
                    var strict = new Il2CppSystem.Collections.Generic.List<CustomizationItem>();
                    strict.Add(it);
                    var ci = newColor(root, ps[p], ps[p].colorItems.Count, T("Noël", "Christmas"), From(ps[p].colorItems[0].material, tex, key), strict);
                    colorIcons[ci.Pointer] = "cosmetics/swatch_noel.png";
                    ps[p].colorItems.Add(ci);
                }
                // T-shirt manches longues : col et poignets en fourrure
                AddNoel(shirt, "Shirt.003", (b, pos, n, arm, uv) =>
                {
                    float L = b.neck - b.H(b.axis);
                    float wrist = Mathf.Min((pos - b.handL).magnitude, (pos - b.handR).magnitude);
                    bool fur = (arm > 0.5f && wrist < L * 0.09f) || (b.H(pos) > b.neck - L * 0.1f && arm < 0.5f);
                    // le velours l'emporte sur les zones de texture partagées (manches et torse se recouvrent sur ce modèle)
                    return fur ? (Fur(uv), 1) : (Velvet(uv), 2);
                }, "noel_tee");
                // Jupe : fourrure en bas, ceinture noire à boucle dorée en haut
                if (pants >= 0)
                    AddNoel(pants, "Skirt", (b, pos, n, arm, uv) =>
                    {
                        float t = (b.high - b.H(pos)) / Mathf.Max(1e-6f, b.high - b.low);   // 0 en haut, 1 en bas
                        if (t > 0.6f) return (Fur(uv), 2);   // le maillage descend plus bas que l'ourlet visible (doublure)
                        if (t < 0.13f)
                        {
                            bool buckle = b.Front(pos) - b.depthC > 0f && Mathf.Abs(b.Side(pos)) < b.halfW * 0.16f;
                            return buckle ? (new Color32(214, 170, 60, 255), 2) : (new Color32(20, 20, 22, 255), 2);
                        }
                        return (Velvet(uv), 1);
                    }, "noel_skirt");
                // Pantalon : revers en fourrure aux chevilles
                if (pants >= 0)
                    AddNoel(pants, "Pants", (b, pos, n, arm, uv) =>
                    {
                        float t = (b.high - b.H(pos)) / Mathf.Max(1e-6f, b.high - b.low);
                        return t > 0.9f ? (Fur(uv), 2) : (Velvet(uv), 1);
                    }, "noel_pants");
            }

            // Hoodie du manager, logo dans le dos
            var hoodie = Smr("Jacket.001");
            if (jacket >= 0 && hoodie != null && ps[jacket].colorItems.Count > 0)
            {
                var logo = LoadRgba("carpenter_back.rgba");
                var dark = new Color32(26, 26, 28, 255);
                // La capuche repliée occupe le haut du dos (~15 % supérieurs du vêtement) : logo en dessous, rien dessus
                var tex = Bake(hoodie, SubOf(Item(jacket, "Jacket.001")), dark, (b, pos, n, arm, uv) =>
                {
                    float hoodLine = b.high - (b.high - b.low) * 0.22f, size = b.halfW * 1.25f;
                    if (arm >= 0.6f || b.H(pos) > hoodLine) return (dark, 0);
                    return Pick(dark, Decal(b, pos, n, logo, false, Mathf.Min(b.chest, hoodLine - size * 0.55f), size));
                }, "hoodie_carpenter_v2");
                if (tex != null)
                {
                    var strict = new Il2CppSystem.Collections.Generic.List<CustomizationItem>();
                    var it = Item(jacket, "Jacket.001");
                    if (it != null) strict.Add(it);
                    var ci = newColor(root, ps[jacket], ps[jacket].colorItems.Count, "Carpenter", From(ps[jacket].colorItems[0].material, tex, "hoodie_carpenter"), strict);
                    colorIcons[ci.Pointer] = "cosmetics/swatch_hoodie_carpenter.png";
                    ps[jacket].colorItems.Add(ci);
                }
            }
        }
        catch (Exception e) { Plugin.L.LogError("[COSMETIC] vêtements peints : " + e); }
    }
}
