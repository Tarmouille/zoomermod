using System;
using System.Collections.Generic;
using Player.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Peignoir : copie de la veste du jeu (celle du manager, low-poly et déjà ajustée au corps), dont le bas est étiré
// jusqu'aux genoux, légèrement évasé, et lié aux cuisses. Tout se fait dans l'espace de référence du maillage (pose de
// liaison), indépendamment de la posture du personnage. Porté par-dessus la chemise.
static class Robe
{
    internal const string Name = "DaleMod_Peignoir";
    const string Source = "Jacket";
    const float Flare = 0.18f;          // évasement au bas (proportion de l'écart à l'axe du corps)

    internal static CustomizationItem NewItem(PlayerCustomizer c, CustomizationParameter p, int id, Material placeholder)
    {
        SkinnedMeshRenderer jacket = null;
        foreach (var r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name == Source) { jacket = r; break; }
        if (jacket == null || jacket.sharedMesh == null) { Plugin.L.LogWarning("[PEIGNOIR] veste du jeu introuvable"); return null; }

        var go = Object.Instantiate(jacket.gameObject, jacket.transform.parent, false);
        go.name = Name;
        go.SetActive(false);
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        smr.sharedMaterial = placeholder;
        Mesh mesh;
        try { mesh = RobeCut.Build(jacket.sharedMesh, jacket.bones) ?? Lengthen(jacket.sharedMesh, jacket.bones); }
        catch (Exception e) { Plugin.L.LogError("[PEIGNOIR] " + e); Object.Destroy(go); return null; }
        smr.sharedMesh = mesh;
        if (RobeCut.DebugLabels)
        {
            var mats = new Material[4];
            var cols = new[] { Color.gray, Color.red, Color.green, Color.yellow };
            for (int k = 0; k < 4; k++) { mats[k] = new Material(placeholder); mats[k].SetColor("_BaseColor", cols[k]); }
            smr.sharedMaterials = mats;
        }
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
        it.colorIndexInRenderer = 0;
        // Corpulence : le jeu déforme le peignoir comme la veste ; ses données internes sont reprises du nouveau maillage
        try { it.OnAwake(); } catch (Exception e) { Plugin.L.LogWarning("[PEIGNOIR] données de déformation : " + e.Message); }
        return it;
    }

    static readonly Dictionary<IntPtr, Mesh> made = new();
    static Mesh Lengthen(Mesh src, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Transform> bones)
    {
        if (made.TryGetValue(src.Pointer, out var done) && done != null) return done;
        Matrix4x4[] bind = src.bindposes;
        int Bone(string n) { for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == n) return i; return -1; }
        Vector3 P(int i) => bind[i].inverse.MultiplyPoint3x4(Vector3.zero);   // position de l'os dans l'espace du maillage

        int spine = Bone("spine"), spine1 = Bone("spine.001"), head = Bone("head");
        int thighL = Bone("thigh.L"), thighR = Bone("thigh.R"), shinL = Bone("shin.L"), shinR = Bone("shin.R");
        int spine2 = Bone("spine.002"), footL = Bone("foot.L"), toeL = Bone("toe.L");
        if (spine < 0 || spine1 < 0 || head < 0 || thighL < 0 || thighR < 0 || shinL < 0 || shinR < 0) throw new Exception("os manquants");

        var up = (P(head) - P(spine)).normalized;
        float H(Vector3 v) => Vector3.Dot(v, up);
        float waist = H(P(spine1));
        float knee = (H(P(shinL)) + H(P(shinR))) * 0.5f;
        var axis = P(spine);   // axe vertical du corps passant par les hanches
        // Avant : du pied vers les orteils ; droite : perpendiculaire
        var fwd = footL >= 0 && toeL >= 0 ? P(toeL) - P(footL) : Vector3.Cross(P(thighL) - P(thighR), up);
        fwd = (fwd - up * Vector3.Dot(fwd, up)).normalized;
        var right = Vector3.Cross(up, fwd);
        float chest = spine2 >= 0 ? H(P(spine2)) : waist + (waist - knee) * 0.3f;

        Vector3[] v = src.vertices;
        BoneWeight[] w = src.boneWeights;
        Vector2[] uv = src.uv;
        // Demi-largeur du torse à la taille, densité de texture du haut (texture par unité de longueur)
        float halfW = 0f;
        foreach (var x in v) if (Mathf.Abs(H(x) - waist) < (waist - knee) * 0.05f) halfW = Mathf.Max(halfW, Mathf.Abs(Vector3.Dot(x - axis, right)));
        var ratios = new List<float>();
        for (int i = 1; i < v.Length; i++)
        {
            float d = (v[i] - v[i - 1]).magnitude;
            if (d > 1e-6f && H(v[i]) > waist && H(v[i - 1]) > waist) ratios.Add((uv[i] - uv[i - 1]).magnitude / d);
        }
        ratios.Sort();
        float density = ratios.Count > 0 ? ratios[ratios.Count / 2] : 1f / Mathf.Max(1e-6f, waist - knee);
        float hem = float.MaxValue;
        foreach (var x in v) hem = Mathf.Min(hem, H(x));
        float bottom = knee + (waist - knee) * 0.08f;   // juste au-dessus du genou
        if (hem >= waist) throw new Exception("ourlet au-dessus de la taille");

        var nv = new Vector3[v.Length];
        var nw = new BoneWeight[w.Length];
        var nuv = new Vector2[uv.Length];
        // Projection autour du corps, raccord devant (dans l'ouverture), à la densité de texture de la veste
        Vector2 Cyl(Vector3 x)
        {
            var r2 = x - axis; r2 -= up * Vector3.Dot(r2, up);
            float ang = Mathf.Atan2(Vector3.Dot(r2, right), -Vector3.Dot(r2, fwd));
            return new Vector2(ang * halfW * 1.3f * density, (H(x) - waist) * density);
        }
        var armBones = new HashSet<int>();
        foreach (var n in new[] { "shoulder.L", "shoulder.R", "upper_arm.L", "upper_arm.R", "forearm.L", "forearm.R", "hand.L", "hand.R" })
        { int b = Bone(n); if (b >= 0) armBones.Add(b); }
        bool IsSleeve(BoneWeight bw)
        {
            float a = 0f;
            if (armBones.Contains(bw.boneIndex0)) a += bw.weight0;
            if (armBones.Contains(bw.boneIndex1)) a += bw.weight1;
            if (armBones.Contains(bw.boneIndex2)) a += bw.weight2;
            if (armBones.Contains(bw.boneIndex3)) a += bw.weight3;
            return a > 0.5f;
        }
        for (int i = 0; i < v.Length; i++)
        {
            float h = H(v[i]);
            nuv[i] = uv[i];
            // Ouverture : les pans de devant s'écartent, de la poitrine (rien) jusqu'en bas
            var rel = v[i] - axis;
            float side = Vector3.Dot(rel, right), front = Vector3.Dot(rel, fwd);
            float open = Mathf.Clamp01((chest - h) / Mathf.Max(1e-6f, chest - waist));
            float frontness = Mathf.Clamp01(front / Mathf.Max(1e-6f, halfW * 0.6f));
            float spread = halfW * 0.40f * open * frontness * (1f - Mathf.Clamp01(Mathf.Abs(side) / halfW));
            var opened = v[i] + right * (Mathf.Sign(side == 0f ? 1f : side) * spread);
            if (h >= waist) { nv[i] = opened; nw[i] = w[i]; continue; }
            v[i] = opened;
            float t = (waist - h) / (waist - hem);               // 0 à la taille, 1 à l'ourlet
            float nh = waist - t * (waist - bottom);
            var lateral = v[i] - axis; lateral -= up * Vector3.Dot(lateral, up);
            nv[i] = v[i] + up * (nh - h) + lateral * (Flare * t);
            // Pans liés aux cuisses, de plus en plus vers le bas
            float dl = (v[i] - P(thighL)).sqrMagnitude, dr = (v[i] - P(thighR)).sqrMagnitude;
            int thigh = dl < dr ? thighL : thighR;
            nw[i] = Blend(w[i], thigh, 0.75f * t);
            // Texture projetée autour du corps, à la densité du haut (étirer la veste étirait aussi les taches)
            if (!IsSleeve(nw[i])) nuv[i] = Cyl(nv[i]);
        }

        var mesh = Object.Instantiate(src);   // copie avec ses triangles (illisibles depuis le mod)
        mesh.name = Name;
        mesh.vertices = nv;
        mesh.boneWeights = nw;
        mesh.uv = nuv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
        made[src.Pointer] = mesh;

        Plugin.L.LogInfo($"[PEIGNOIR] veste rallongée : {v.Length} sommets, ourlet {hem:0.0000} -> {bottom:0.0000} (taille {waist:0.0000}, genou {knee:0.0000})");
        return mesh;
    }

    // Poids d'origine réduits de k, le reste sur l'os donné
    static BoneWeight Blend(BoneWeight a, int bone, float k)
    {
        var d = new Dictionary<int, float>();
        void Add(int b, float x) { if (x > 0f) d[b] = (d.TryGetValue(b, out var o) ? o : 0f) + x; }
        Add(a.boneIndex0, a.weight0 * (1 - k)); Add(a.boneIndex1, a.weight1 * (1 - k));
        Add(a.boneIndex2, a.weight2 * (1 - k)); Add(a.boneIndex3, a.weight3 * (1 - k));
        Add(bone, k);
        var l = new List<KeyValuePair<int, float>>(d);
        l.Sort((x, y) => y.Value.CompareTo(x.Value));
        float s = 0f; for (int i = 0; i < l.Count && i < 4; i++) s += l[i].Value;
        var r = new BoneWeight();
        float W(int i) => i < l.Count ? l[i].Value / Mathf.Max(1e-6f, s) : 0f;
        int I(int i) => i < l.Count ? l[i].Key : 0;
        r.boneIndex0 = I(0); r.weight0 = W(0); r.boneIndex1 = I(1); r.weight1 = W(1);
        r.boneIndex2 = I(2); r.weight2 = W(2); r.boneIndex3 = I(3); r.weight3 = W(3);
        return r;
    }
}
