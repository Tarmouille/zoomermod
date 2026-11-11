using System;
using System.Collections.Generic;
using Player.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Gilet jaune (catégorie Manteau) : copie du t-shirt du jeu sans les manches, gonflée pour passer par-dessus la chemise.
// Mêmes os et mêmes coordonnées de texture que le t-shirt : la texture (CosmeticExtras.GiletMaterial) est peinte sur
// le t-shirt lui-même. Le jeu le déforme selon la corpulence comme le peignoir. La couleur du jeu tombe sur un
// sous-maillage vide ; la veste du manager est masquée dessous (Cosmetics.HideJacketUnderRobe).
static class Gilet
{
    internal const string Name = "DaleMod_GiletJaune";
    internal const string Source = "Shirt.002";

    internal static CustomizationItem NewItem(PlayerCustomizer c, CustomizationParameter p, int id, int submesh, Material dummy)
    {
        SkinnedMeshRenderer tee = null;
        foreach (var r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name == Source) { tee = r; break; }
        if (tee == null || tee.sharedMesh == null) { Plugin.L.LogWarning("[GILET] t-shirt du jeu introuvable"); return null; }
        Mesh mesh;
        Material vest;
        try { mesh = Build(tee, submesh); vest = CosmeticExtras.GiletMaterial(tee, submesh); }
        catch (Exception e) { Plugin.L.LogError("[GILET] " + e); return null; }
        if (mesh == null || vest == null) return null;

        var go = Object.Instantiate(tee.gameObject, tee.transform.parent, false);
        go.name = Name;
        go.SetActive(false);
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.sharedMaterials = new[] { vest, dummy };
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
        it.colorIndexInRenderer = 1;
        try { it.OnAwake(); } catch (Exception e) { Plugin.L.LogWarning("[GILET] données de déformation : " + e.Message); }
        return it;
    }

    static Mesh Build(SkinnedMeshRenderer tee, int submesh)
    {
        var src = tee.sharedMesh;
        var bones = tee.bones;
        Matrix4x4[] bind = src.bindposes;
        int Bone(string n) { for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == n) return i; return -1; }
        Vector3 P(int i) => bind[i].inverse.MultiplyPoint3x4(Vector3.zero);
        int spine = Bone("spine"), head = Bone("head");
        if (spine < 0 || head < 0) throw new Exception("os manquants");
        var up = (P(head) - P(spine)).normalized;
        var axis = P(spine);

        var arms = new HashSet<int>();
        foreach (var n in new[] { "upper_arm.L", "upper_arm.R", "forearm.L", "forearm.R", "hand.L", "hand.R" }) { int k = Bone(n); if (k >= 0) arms.Add(k); }
        Vector3[] v = src.vertices;
        Vector2[] uv = src.uv;
        BoneWeight[] w = src.boneWeights;
        Vector3[] nrm = null;
        try { nrm = src.normals; } catch { }
        if (nrm != null && nrm.Length != v.Length) nrm = null;
        float Arm(int i)
        {
            var bw = w[i]; float a = 0f;
            if (arms.Contains(bw.boneIndex0)) a += bw.weight0; if (arms.Contains(bw.boneIndex1)) a += bw.weight1;
            if (arms.Contains(bw.boneIndex2)) a += bw.weight2; if (arms.Contains(bw.boneIndex3)) a += bw.weight3;
            return a;
        }
        Vector3 Lateral(Vector3 x) { var r = x - axis; return r - up * Vector3.Dot(r, up); }

        // Rayon typique du torse : sert d'unité pour l'épaisseur du gilet
        var radii = new List<float>();
        for (int i = 0; i < v.Length; i++) if (Arm(i) < 0.3f) radii.Add(Lateral(v[i]).magnitude);
        if (radii.Count == 0) throw new Exception("pas de torse");
        radii.Sort();
        float R = radii[radii.Count / 2];

        var nv = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            var lat = Lateral(v[i]);
            var outward = nrm != null ? nrm[i].normalized : lat.normalized;
            nv[i] = v[i] + outward * (R * 0.07f) + lat * 0.03f;
        }
        var tri = RobeCut.TrianglesOf(src, submesh);
        if (tri == null) throw new Exception("triangles illisibles");
        var kept = new List<int>();
        for (int t = 0; t + 2 < tri.Length; t += 3)
            if ((Arm(tri[t]) + Arm(tri[t + 1]) + Arm(tri[t + 2])) / 3f < 0.35f) { kept.Add(tri[t]); kept.Add(tri[t + 1]); kept.Add(tri[t + 2]); }

        var mesh = new Mesh { name = Name, hideFlags = HideFlags.DontUnloadUnusedAsset };
        mesh.vertices = nv;
        mesh.uv = uv;
        mesh.boneWeights = w;
        mesh.bindposes = bind;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(kept.ToArray(), 0);
        mesh.SetTriangles(new int[0], 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Plugin.L.LogInfo($"[GILET] construit : {kept.Count / 3} triangles sur {tri.Length / 3} (manches retirées), rayon du torse {R:0.0000}, normales {(nrm != null ? "du jeu" : "radiales")}");
        return mesh;
    }
}
