using System;
using System.Collections.Generic;
using Player.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Catégorie "Crâne" (15e) : "Crâne raboté", le coin du haut du crâne (côté gauche du personnage) coupé net en biais,
// comme d'un coup de rabot à 45°. L'objet du jeu n'est qu'un repère (maillage vide sous l'os de la tête) ; quand il est
// actif, la tête de CE personnage reçoit une copie de son maillage où les sommets au-delà du plan sont ramenés sur le
// plan. Refait après chaque changement d'apparence (la corpulence déforme aussi la tête), retiré quand on l'enlève.
static class SkullCut
{
    internal const string Name = "DaleMod_CraneRabote";
    const float Cut = 0.80f;   // distance du plan au centre de la tête, en demi-largeurs de tête

    internal static CustomizationItem NewItem(Transform head, CustomizationParameter p, int id, Material dummy)
    {
        var go = new GameObject(Name);
        go.transform.SetParent(head, false);
        go.SetActive(false);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = new Mesh { name = Name };
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = dummy;
        var it = go.AddComponent<CustomizationItem>();
        it.customizationType = p.customizationType;
        it.itemName = Name;
        it.id = id.ToString();
        it.price = 0;
        it.cutHair = false;
        it.modelRenderer = mr;
        it.meshFilter = mf;
        it.clonedMesh = mf.sharedMesh;
        it.colorIndexInRenderer = 0;
        return it;
    }

    // Sommets d'avant la coupe et sommets coupés, par maillage de tête (le maillage reste celui du jeu : on ne fait que
    // modifier ses sommets, et les rétablir). Le jeu recrée ses maillages quand il change d'apparence : un maillage
    // inconnu est simplement repris tel quel.
    static readonly Dictionary<IntPtr, Vector3[]> before = new(), after = new();

    internal static void Apply(PlayerCustomizer c, Transform head)
    {
        var marker = head.Find(Name);
        bool on = marker != null && marker.gameObject.activeSelf;
        foreach (var smr in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!smr.name.StartsWith("Head")) continue;
            var mesh = smr.sharedMesh;
            if (mesh == null) continue;
            var key = mesh.Pointer;
            Vector3[] now = mesh.vertices;
            bool carved = after.TryGetValue(key, out var last) && Same(now, last);
            if (!on || !smr.gameObject.activeInHierarchy)
            {
                if (carved && before.TryGetValue(key, out var orig)) { mesh.vertices = orig; mesh.RecalculateNormals(); mesh.RecalculateBounds(); }
                before.Remove(key); after.Remove(key);
                continue;
            }
            if (!mesh.name.EndsWith("(Clone)")) { Plugin.L.LogWarning($"[CRANE] maillage de tête partagé ({mesh.name}) : pas de coupe"); continue; }
            var src = carved ? before[key] : now;
            try
            {
                var o = Carve(src, mesh.bindposes, smr.bones);
                mesh.vertices = o;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                before[key] = src; after[key] = o;
            }
            catch (Exception e) { Plugin.L.LogError("[CRANE] " + e); }
        }
    }

    static bool Same(Vector3[] a, Vector3[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if ((a[i] - b[i]).sqrMagnitude > 1e-14f) return false;
        return true;
    }

    // Sommets au-delà du plan (coin haut, côté gauche du personnage, à 45°) ramenés sur le plan
    static Vector3[] Carve(Vector3[] v, Matrix4x4[] bind, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Transform> bones)
    {
        int Bone(string n) { for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == n) return i; return -1; }
        Vector3 P(int i) => bind[i].inverse.MultiplyPoint3x4(Vector3.zero);
        int spine = Bone("spine"), head = Bone("head"), footL = Bone("foot.L"), toeL = Bone("toe.L");
        Vector3 up = head >= 0 && spine >= 0 ? (P(head) - P(spine)).normalized : Vector3.up;
        var fwd = footL >= 0 && toeL >= 0 ? P(toeL) - P(footL) : Vector3.forward;
        fwd = (fwd - up * Vector3.Dot(fwd, up)).normalized;
        var right = Vector3.Cross(up, fwd);
        // Centre et demi-largeur de la tête (partie au-dessus de l'os de la tête : sans le cou)
        float h0 = head >= 0 ? Vector3.Dot(P(head), up) : float.MinValue;
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        foreach (var x in v) if (Vector3.Dot(x, up) > h0) { min = Vector3.Min(min, x); max = Vector3.Max(max, x); }
        var c = (min + max) * 0.5f;
        float R = 0f;
        foreach (var x in v) if (Vector3.Dot(x, up) > h0) R = Mathf.Max(R, Mathf.Abs(Vector3.Dot(x - c, right)));
        var n = (up - right).normalized;   // vers le haut et vers la gauche du personnage
        float d = Cut * R;
        var o = new Vector3[v.Length];
        int moved = 0;
        for (int i = 0; i < v.Length; i++)
        {
            float k = Vector3.Dot(v[i] - c, n);
            o[i] = k > d ? v[i] - n * (k - d) : v[i];
            if (k > d) moved++;
        }
        Plugin.L.LogInfo($"[CRANE] crâne raboté : {moved} sommet(s) sur {v.Length} ramenés sur le plan");
        return o;
    }
}
