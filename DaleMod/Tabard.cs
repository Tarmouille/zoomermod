using System;
using System.Collections.Generic;
using Player.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tabard de Solaire (catégorie Manteau) : un pan devant et un pan derrière, reliés aux épaules, jusqu'à mi-cuisse.
// Posé sur le torse réel (sommets de la chemise et du pantalon, dans l'espace de référence des maillages du jeu) et lié
// à la colonne et aux cuisses. Matière fixe (texture crème au soleil) : la couleur du jeu tombe sur un sous-maillage vide.
static class Tabard
{
    internal const string Name = "DaleMod_Tabard";
    const int Cols = 8, Rows = 12;

    internal static CustomizationItem NewItem(PlayerCustomizer c, CustomizationParameter p, int id, Material tabard, Material dummy)
    {
        SkinnedMeshRenderer shirt = null, pants = null;
        foreach (var r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r.name == "Shirt.003") shirt = r;
            if (r.name == "Pants") pants = r;
        }
        if (shirt == null) { Plugin.L.LogWarning("[TABARD] chemise de référence introuvable"); return null; }
        Mesh mesh;
        try { mesh = Build(shirt, pants); }
        catch (Exception e) { Plugin.L.LogError("[TABARD] " + e); return null; }
        if (mesh == null) return null;

        var go = Object.Instantiate(shirt.gameObject, shirt.transform.parent, false);
        go.name = Name;
        go.SetActive(false);
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.sharedMaterials = new[] { tabard, dummy };
        smr.updateWhenOffscreen = true;
        var it = go.GetComponent<CustomizationItem>() ?? go.AddComponent<CustomizationItem>();
        it.customizationType = p.customizationType;
        it.itemName = Name;
        it.id = id.ToString();
        it.price = 0;
        it.cutHair = false;
        it.modelRenderer = smr;
        it.skinnedMeshRenderer = smr;
        it.clonedMesh = mesh;
        it.colorIndexInRenderer = 1;   // la couleur du jeu va sur le sous-maillage vide
        return it;
    }

    static Mesh Build(SkinnedMeshRenderer shirt, SkinnedMeshRenderer pants)
    {
        var src = shirt.sharedMesh;
        var bones = shirt.bones;
        Matrix4x4[] bind = src.bindposes;
        int Bone(string n) { for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == n) return i; return -1; }
        Vector3 P(int i) => bind[i].inverse.MultiplyPoint3x4(Vector3.zero);
        int spine = Bone("spine"), spine1 = Bone("spine.001"), spine2 = Bone("spine.002"), spine3 = Bone("spine.003"), head = Bone("head");
        int thighL = Bone("thigh.L"), thighR = Bone("thigh.R"), shinL = Bone("shin.L"), footL = Bone("foot.L"), toeL = Bone("toe.L");
        int upperL = Bone("upper_arm.L"), upperR = Bone("upper_arm.R"), foreL = Bone("forearm.L"), foreR = Bone("forearm.R");
        if (spine < 0 || spine1 < 0 || spine2 < 0 || spine3 < 0 || head < 0 || thighL < 0 || thighR < 0 || shinL < 0) return null;

        var up = (P(head) - P(spine)).normalized;
        var fwd = footL >= 0 && toeL >= 0 ? P(toeL) - P(footL) : Vector3.forward;
        fwd = (fwd - up * Vector3.Dot(fwd, up)).normalized;
        var right = Vector3.Cross(up, fwd);
        var axis = P(spine);
        float H(Vector3 x) => Vector3.Dot(x, up);
        float Side(Vector3 x) => Vector3.Dot(x - axis, right);
        float Front(Vector3 x) => Vector3.Dot(x - axis, fwd);

        // Corps (hors bras) : chemise + pantalon
        var arms = new HashSet<int> { upperL, upperR, foreL, foreR };
        var body = new List<Vector3>();
        Vector3[] sv = src.vertices; BoneWeight[] sw = src.boneWeights;
        for (int i = 0; i < sv.Length; i++)
        {
            var bw = sw[i]; float a = 0f;
            if (arms.Contains(bw.boneIndex0)) a += bw.weight0; if (arms.Contains(bw.boneIndex1)) a += bw.weight1;
            if (arms.Contains(bw.boneIndex2)) a += bw.weight2; if (arms.Contains(bw.boneIndex3)) a += bw.weight3;
            if (a < 0.3f) body.Add(sv[i]);
        }
        if (pants != null && pants.sharedMesh != null) foreach (var x in pants.sharedMesh.vertices) body.Add(x);

        float waist = H(P(spine1)), knee = H(P(shinL)), hips = H(P(spine));
        float top = float.MinValue, W = 0f;
        foreach (var x in body) if (Mathf.Abs(Side(x)) < (waist - knee) * 0.15f) top = Mathf.Max(top, H(x));
        float chest = H(P(spine2)) + (H(P(spine3)) - H(P(spine2))) * 0.5f;
        foreach (var x in body) if (Mathf.Abs(H(x) - chest) < (waist - knee) * 0.08f) W = Mathf.Max(W, Mathf.Abs(Side(x)));
        top -= (waist - knee) * 0.04f;
        float bottom = hips - (hips - knee) * 0.55f;
        float L = top - bottom, halfPanel = W * 0.92f, margin = W * 0.14f;

        // Profondeur du corps devant / derrière en (côté, hauteur), sans jamais rentrer vers le corps en descendant
        float Depth(float s, float h, bool front)
        {
            float best = front ? float.MinValue : float.MaxValue;
            foreach (var x in body)
            {
                if (Mathf.Abs(Side(x) - s) > W * 0.32f || Mathf.Abs(H(x) - h) > L * 0.09f) continue;
                float f = Front(x);
                best = front ? Mathf.Max(best, f) : Mathf.Min(best, f);
            }
            return best;
        }

        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var ws = new List<BoneWeight>(); var tris = new List<int>();
        BoneWeight Weight(float h, float s)
        {
            var d = new Dictionary<int, float>();
            void Add(int b, float v) { if (b >= 0 && v > 0f) d[b] = (d.TryGetValue(b, out var o) ? o : 0f) + v; }
            float y1 = H(P(spine1)), y2 = H(P(spine2)), y3 = H(P(spine3));
            if (h >= y3) Add(spine3, 1f);
            else if (h >= y2) { float t = Mathf.InverseLerp(y2, y3, h); Add(spine2, 1 - t); Add(spine3, t); }
            else if (h >= y1) { float t = Mathf.InverseLerp(y1, y2, h); Add(spine1, 1 - t); Add(spine2, t); }
            else if (h >= hips) { float t = Mathf.InverseLerp(hips, y1, h); Add(spine, 1 - t); Add(spine1, t); }
            else
            {
                float t = Mathf.Clamp01((hips - h) / Mathf.Max(1e-6f, hips - knee)) * 0.8f;
                int near = Mathf.Abs(s - Side(P(thighL))) < Mathf.Abs(s - Side(P(thighR))) ? thighL : thighR;
                int far = near == thighL ? thighR : thighL;
                float k = Mathf.Clamp01(Mathf.Abs(s) / (W * 0.5f));
                Add(spine, 1 - t); Add(near, t * (0.5f + 0.5f * k)); Add(far, t * (0.5f - 0.5f * k));
            }
            var l = new List<KeyValuePair<int, float>>(d);
            l.Sort((a, b) => b.Value.CompareTo(a.Value));
            float sum = 0f; for (int i = 0; i < l.Count && i < 4; i++) sum += l[i].Value;
            var r = new BoneWeight();
            float Wt(int i) => i < l.Count ? l[i].Value / Mathf.Max(1e-6f, sum) : 0f;
            int I(int i) => i < l.Count ? l[i].Key : 0;
            r.boneIndex0 = I(0); r.weight0 = Wt(0); r.boneIndex1 = I(1); r.weight1 = Wt(1);
            r.boneIndex2 = I(2); r.weight2 = Wt(2); r.boneIndex3 = I(3); r.weight3 = Wt(3);
            return r;
        }

        // Pans : grille (côté x hauteur), devant puis derrière
        int[,] Panel(bool front)
        {
            var idx = new int[Rows + 1, Cols + 1];
            for (int c = 0; c <= Cols; c++)
            {
                float s = -halfPanel + 2f * halfPanel * c / Cols;
                float prev = float.NaN;
                for (int r = 0; r <= Rows; r++)
                {
                    float h = top - L * r / Rows;
                    float dpt = Depth(s, h, front);
                    if (float.IsInfinity(dpt) || float.IsNaN(dpt) || dpt == float.MinValue || dpt == float.MaxValue) dpt = float.IsNaN(prev) ? 0f : prev;
                    if (!float.IsNaN(prev)) dpt = front ? Mathf.Max(dpt, prev) : Mathf.Min(dpt, prev);   // tombe droit, ne rentre pas
                    prev = dpt;
                    float z = dpt + (front ? margin : -margin);
                    var p = axis + right * s + fwd * z;
                    p += up * (h - H(p));
                    idx[r, c] = verts.Count;
                    verts.Add(p);
                    float u = (float)c / Cols * 0.5f;
                    uvs.Add(new Vector2(front ? u : 1f - u, 1f - (float)r / Rows));
                    ws.Add(Weight(h, s));
                }
            }
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    int a = idx[r, c], b = idx[r, c + 1], d = idx[r + 1, c + 1], e = idx[r + 1, c];
                    tris.Add(a); tris.Add(b); tris.Add(d); tris.Add(a); tris.Add(d); tris.Add(e);
                }
            return idx;
        }
        var fr = Panel(true);
        var bk = Panel(false);
        for (int c = 0; c < Cols; c++)   // épaules : le haut des deux pans relié
        {
            int a = fr[0, c], b = fr[0, c + 1], d = bk[0, c + 1], e = bk[0, c];
            tris.Add(a); tris.Add(d); tris.Add(b); tris.Add(a); tris.Add(e); tris.Add(d);
        }

        // Deux faces (sommets à part) + sous-maillage vide pour la couleur du jeu
        var ov = new List<Vector3>(); var ou = new List<Vector2>(); var ow = new List<BoneWeight>(); var ot = new List<int>();
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            int n0 = ov.Count;
            foreach (var x in new[] { tris[i], tris[i + 1], tris[i + 2], tris[i], tris[i + 2], tris[i + 1] }) { ov.Add(verts[x]); ou.Add(uvs[x]); ow.Add(ws[x]); }
            for (int k = 0; k < 6; k++) ot.Add(n0 + k);
        }
        var mesh = new Mesh { name = Name, hideFlags = HideFlags.DontUnloadUnusedAsset };
        mesh.vertices = ov.ToArray();
        mesh.uv = ou.ToArray();
        mesh.boneWeights = ow.ToArray();
        mesh.bindposes = bind;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(ot.ToArray(), 0);
        mesh.SetTriangles(new int[0], 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Plugin.L.LogInfo($"[TABARD] construit : {ov.Count} sommets, haut {top:0.0000}, bas {bottom:0.0000}, demi-largeur {W:0.0000}");
        return mesh;
    }
}
