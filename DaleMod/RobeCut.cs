using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Peignoir propre, à partir des triangles de la veste du jeu (lus par une fonction interne du moteur : l'accès normal,
// Mesh.triangles, est retiré du jeu) :
// 1. la veste reste telle quelle, avec sa texture d'origine ;
// 2. les deux pans de devant sont séparés en suivant leur surface (le pan du dessus au bouton reste de son côté), puis
//    écartés de la poitrine jusqu'en bas ;
// 3. l'ourlet est prolongé jusqu'aux genoux par de nouvelles rangées, dont la texture continue celle du tissu au-dessus
//    (même sens, même échelle) : pas de couture.
static class RobeCut
{
    // Unity 2023.2 : GetTrianglesImpl_Injected(objet natif, sous-maillage, applyBaseVertex, out BlittableArrayWrapper)
    [StructLayout(LayoutKind.Sequential)]
    struct ArrayWrapper { public IntPtr data; public int size; public int flags; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void IndicesFn(IntPtr self, int submesh, [MarshalAs(UnmanagedType.U1)] bool applyBaseVertex, out ArrayWrapper ret);

    static IndicesFn indices;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr OwnedFn(IntPtr nativeOwnedMemory);
    static bool searched;

    // Triangles d'un seul sous-maillage
    internal static int[] TrianglesOf(Mesh m, int submesh)
    {
        if (submesh < 0 || submesh >= m.subMeshCount) submesh = 0;
        var all = Triangles(m, submesh);
        return all;
    }

    internal static int[] Triangles(Mesh m, int only = -1)
    {
        if (!searched)
        {
            searched = true;
            foreach (var name in new[] { "UnityEngine.Mesh::GetTrianglesImpl_Injected", "UnityEngine.Mesh::GetIndicesImpl_Injected" })
            {
                var p = IL2CPP.il2cpp_resolve_icall(name);
                Plugin.L.LogInfo($"[PEIGNOIR] fonction interne {name} : {(p == IntPtr.Zero ? "absente" : "trouvée")}");
                if (p != IntPtr.Zero) { indices = Marshal.GetDelegateForFunctionPointer<IndicesFn>(p); break; }
            }
        }
        if (indices == null) return null;
        var all = new List<int>();
        for (int s = 0; s < m.subMeshCount; s++)
        {
            if (only >= 0 && s != only) continue;
            indices(m.m_CachedPtr, s, true, out var r);
            Plugin.L.LogInfo($"[PEIGNOIR] sous-maillage {s} : {r.size} indices (drapeaux {r.flags})");
            if (r.data == IntPtr.Zero || r.size <= 0) return null;
            int vc = m.vertexCount;
            int[] a = null;
            // Drapeau 3 (DataIsNativeOwnedMemory) : data désigne un bloc du moteur ; ses données via
            // BindingsAllocator.GetNativeOwnedDataPointer, sinon premier pointeur du bloc
            var candidates = new List<IntPtr>();
            if (r.flags == 3)
            {
                var g = IL2CPP.il2cpp_resolve_icall("UnityEngine.Bindings.BindingsAllocator::GetNativeOwnedDataPointer");
                if (g != IntPtr.Zero) candidates.Add(Marshal.GetDelegateForFunctionPointer<OwnedFn>(g)(r.data));
                candidates.Add(Marshal.ReadIntPtr(r.data));
            }
            candidates.Add(r.data);
            foreach (var ptr in candidates)
            {
                if (ptr == IntPtr.Zero) continue;
                var b = new int[r.size];
                Marshal.Copy(ptr, b, 0, r.size);
                bool ok = true;
                foreach (var x in b) if (x < 0 || x >= vc) { ok = false; break; }
                if (ok) { a = b; break; }
            }
            if (a == null) { Plugin.L.LogWarning("[PEIGNOIR] indices introuvables dans le bloc"); return null; }
            all.AddRange(a);
        }
        return all.Count % 3 == 0 && all.Count > 0 ? all.ToArray() : null;
    }

    const float Flare = 0.16f;
    const bool DropJunction = false;
    internal static bool DebugLabels => System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "dalemod_robe_debug"));

    internal static Mesh Build(Mesh src, Il2CppReferenceArray<Transform> bones)
    {
        int[] tri;
        try { tri = Triangles(src); }
        catch (Exception e) { Plugin.L.LogError("[PEIGNOIR] triangles : " + e.Message); return null; }
        if (tri == null) { Plugin.L.LogWarning("[PEIGNOIR] triangles illisibles"); return null; }
        int vc = src.vertexCount;
        foreach (var x in tri) if (x < 0 || x >= vc) { Plugin.L.LogWarning("[PEIGNOIR] indices incohérents"); return null; }
        Plugin.L.LogInfo($"[PEIGNOIR] triangles lus : {tri.Length / 3}");

        Matrix4x4[] bind = src.bindposes;
        int Bone(string n) { for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == n) return i; return -1; }
        Vector3 P(int i) => bind[i].inverse.MultiplyPoint3x4(Vector3.zero);
        int spine = Bone("spine"), spine1 = Bone("spine.001"), spine2 = Bone("spine.002"), head = Bone("head");
        int thighL = Bone("thigh.L"), thighR = Bone("thigh.R"), shinL = Bone("shin.L"), shinR = Bone("shin.R");
        int footL = Bone("foot.L"), toeL = Bone("toe.L");
        if (spine < 0 || spine1 < 0 || spine2 < 0 || head < 0 || thighL < 0 || thighR < 0 || shinL < 0 || shinR < 0) return null;

        var up = (P(head) - P(spine)).normalized;
        float H(Vector3 v) => Vector3.Dot(v, up);
        var fwd = footL >= 0 && toeL >= 0 ? P(toeL) - P(footL) : Vector3.Cross(P(thighL) - P(thighR), up);
        fwd = (fwd - up * Vector3.Dot(fwd, up)).normalized;
        var right = Vector3.Cross(up, fwd);
        var axis = P(spine);
        float waist = H(P(spine1)), chest = H(P(spine2));
        float knee = (H(P(shinL)) + H(P(shinR))) * 0.5f;

        var v = new List<Vector3>(src.vertices);
        var uv = new List<Vector2>(src.uv);
        var w = new List<BoneWeight>(src.boneWeights);
        var t = new List<int>(tri);
        int nTri = t.Count / 3;
        float Side(Vector3 x) => Vector3.Dot(x - axis, right);
        float Front(Vector3 x) => Vector3.Dot(x - axis, fwd);

        float hem = float.MaxValue, halfW = 0f;
        foreach (var x in v) hem = Mathf.Min(hem, H(x));
        foreach (var x in v) if (Mathf.Abs(H(x) - waist) < (waist - knee) * 0.06f) halfW = Mathf.Max(halfW, Mathf.Abs(Side(x)));

        // Sommets soudés par position (les coutures de texture dupliquent les sommets)
        float q = (waist - knee) * 1e-4f;
        var weld = new List<int>(new int[v.Count]);
        var map = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < v.Count; i++)
        {
            var k = ((int)Mathf.Round(v[i].x / q), (int)Mathf.Round(v[i].y / q), (int)Mathf.Round(v[i].z / q));
            if (!map.TryGetValue(k, out var id)) { id = map.Count; map[k] = id; }
            weld[i] = id;
        }

        // ---- 1. Pans de devant : étiquette gauche/droite par parcours de surface depuis les côtés ----
        Vector3 Cen(int f) => (v[t[f * 3]] + v[t[f * 3 + 1]] + v[t[f * 3 + 2]]) / 3f;
        bool InFront(int f) { var c = Cen(f); return Front(c) > 0f && H(c) < chest + (chest - waist) * 0.8f; }
        var byWeld = new Dictionary<int, List<int>>();
        for (int f = 0; f < nTri; f++)
            for (int k = 0; k < 3; k++)
            {
                int id = weld[t[f * 3 + k]];
                if (!byWeld.TryGetValue(id, out var l)) byWeld[id] = l = new List<int>();
                l.Add(f);
            }
        var label = new int[nTri];   // 0 : pas devant ; -1 / +1 : pan gauche / droit (repère du maillage)
        var queue = new Queue<int>();
        for (int f = 0; f < nTri; f++)
        {
            if (!InFront(f)) continue;
            float s = Side(Cen(f));
            if (Mathf.Abs(s) > halfW * 0.45f) { label[f] = s > 0 ? 1 : -1; queue.Enqueue(f); }
        }
        while (queue.Count > 0)
        {
            int f = queue.Dequeue();
            for (int k = 0; k < 3; k++)
                foreach (var g in byWeld[weld[t[f * 3 + k]]])
                    if (label[g] == 0 && InFront(g)) { label[g] = label[f]; queue.Enqueue(g); }
        }
        // Sommets partagés par les deux pans : dupliqués, chaque pan garde sa copie
        var owner = new List<int>(new int[v.Count]);   // 0 inconnu, -1, +1, 2 = les deux
        for (int f = 0; f < nTri; f++)
            if (label[f] != 0)
                for (int k = 0; k < 3; k++) { int i = t[f * 3 + k]; owner[i] = owner[i] == 0 || owner[i] == label[f] ? label[f] : 2; }
        int split = 0;
        var copyOf = new Dictionary<int, int>();   // sommet séparé -> sa copie (pan gauche)
        for (int f = 0; f < nTri; f++)
            if (label[f] == -1)
                for (int k = 0; k < 3; k++)
                {
                    int i = t[f * 3 + k];
                    if (owner[i] != 2) continue;
                    v.Add(v[i]); uv.Add(uv[i]); w.Add(w[i]); weld.Add(-1 - weld[i]); owner.Add(-1);   // copie : soudure à part (bord ouvert)
                    int ni = v.Count - 1;
                    // toutes les références de ce pan à ce sommet
                    for (int g = 0; g < nTri; g++)
                        if (label[g] == -1)
                            for (int kk = 0; kk < 3; kk++) if (t[g * 3 + kk] == i) t[g * 3 + kk] = ni;
                    owner[i] = 1;
                    copyOf[i] = ni;
                    split++;
                }
        // Triangles hors des pans qui touchent un sommet séparé : rattachés à la copie de leur côté (sinon, accrochés au
        // mauvais pan, ils s'étiraient en aiguille au bas du V)
        int rehooked = 0;
        for (int f = 0; f < nTri; f++)
        {
            if (label[f] != 0) continue;
            for (int k = 0; k < 3; k++)
            {
                int i = t[f * 3 + k];
                if (copyOf.TryGetValue(i, out var ci) && Side(Cen(f)) < 0f) { t[f * 3 + k] = ci; rehooked++; }
            }
        }
        if (rehooked > 0) Plugin.L.LogInfo($"[PEIGNOIR] {rehooked} attache(s) de triangles déplacée(s) vers le pan gauche");
        var side = new int[v.Count];
        for (int f = 0; f < nTri; f++) if (label[f] != 0) for (int k = 0; k < 3; k++) side[t[f * 3 + k]] = label[f];
        // Écartement : nul en haut du V, maximal à la taille et en dessous
        var before = v.ToArray();
        for (int i = 0; i < v.Count; i++)
        {
            if (side[i] == 0) continue;
            float h = H(v[i]);
            float open = Mathf.Clamp01((chest + (chest - waist) * 0.5f - h) / Mathf.Max(1e-6f, (chest - waist) * 1.5f));
            float frontness = Mathf.Clamp01(Front(v[i]) / Mathf.Max(1e-6f, halfW * 0.5f));
            float near = 1f - Mathf.Clamp01(Mathf.Abs(Side(v[i])) / halfW);
            float sp = halfW * 0.38f * open * frontness * near;
            // écarté vers le côté, et avancé d'autant : le pan passe par-dessus le ventre au lieu de le traverser
            v[i] += right * (side[i] * sp) + fwd * (sp * 0.55f);
        }
        // Boutons : petites pièces détachées du tissu, au milieu du devant. Restés suspendus au milieu de l'ouverture,
        // ils faisaient des traits : retirés (un peignoir n'a pas de boutons).
        var comp = new int[nTri];
        for (int f = 0; f < nTri; f++) comp[f] = -1;
        var button = new bool[nTri];
        int nc = 0;
        for (int f0 = 0; f0 < nTri; f0++)
        {
            if (comp[f0] >= 0) continue;
            var members = new List<int> { f0 };
            comp[f0] = nc;
            for (int m = 0; m < members.Count; m++)
            {
                int f = members[m];
                for (int k = 0; k < 3; k++)
                    foreach (var g in byWeld[weld[tri[f * 3 + k]]])
                        if (comp[g] < 0) { comp[g] = nc; members.Add(g); }
            }
            nc++;
            if (members.Count > 60) continue;
            var cc = Vector3.zero;
            foreach (var f in members) cc += (before[tri[f * 3]] + before[tri[f * 3 + 1]] + before[tri[f * 3 + 2]]) / 3f;
            cc /= members.Count;
            if (Front(cc) > 0f && Mathf.Abs(Side(cc)) < halfW * 0.6f && H(cc) < chest + (chest - waist) && H(cc) > hem)
                foreach (var f in members) button[f] = true;
        }
        int buttons = 0; foreach (var b0 in button) if (b0) buttons++;
        Plugin.L.LogInfo($"[PEIGNOIR] {nc} pièce(s) dans la veste, {buttons} triangle(s) de boutons retirés");

        // Triangles de la jonction des pans étirés en aiguille par l'écartement : retirés (lamelles visibles au bas du V)
        int dropped = 0, junction = 0;
        var copies = new HashSet<int>(copyOf.Values);
        // Recherche du trait au bas du V (debug) : candidats triés par index ; BepInEx/dalemod_robe_search = "début fin"
        var candidates = new List<int>();
        for (int f = 0; f < nTri; f++)
        {
            int b0 = t[f * 3], b1 = t[f * 3 + 1], b2 = t[f * 3 + 2];
            if (side[b0] == 0 && side[b1] == 0 && side[b2] == 0) continue;
            var cb = (before[b0] + before[b1] + before[b2]) / 3f;
            if (Front(cb) <= 0f || Mathf.Abs(Side(cb)) > halfW * 0.6f) continue;
            if (H(cb) < waist - (chest - waist) * 0.5f || H(cb) > chest + (chest - waist) * 0.8f) continue;
            candidates.Add(f);
        }
        var removeSet = new HashSet<int>();
        // Triangles de la veste du jeu cachés sous l'autre pan quand elle est fermée, qui dépassent au bas du V une fois
        // ouverte (trouvés un par un, 2026-11-05) : seulement pour ce maillage précis
        if (nTri == 922) foreach (var f in new[] { 14, 15, 363, 374, 393, 428, 429, 430, 445, 447 }) removeSet.Add(f);
        var searchFile = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "dalemod_robe_search");
        if (System.IO.File.Exists(searchFile))
        {
            var parts = System.IO.File.ReadAllText(searchFile).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int lo = int.Parse(parts[0]), hi = int.Parse(parts[1]);
            for (int pi = 0; pi + 1 < parts.Length; pi += 2)
                for (int k = int.Parse(parts[pi]); k < int.Parse(parts[pi + 1]) && k < candidates.Count; k++) removeSet.Add(candidates[k]);
            var ids = new List<string>(); foreach (var f in removeSet) ids.Add(f.ToString());
            Plugin.L.LogInfo($"[PEIGNOIR] recherche : {candidates.Count} candidats, retirés [{lo},{hi}) = {string.Join(",", ids)}");
        }
        else Plugin.L.LogInfo($"[PEIGNOIR] recherche : {candidates.Count} candidats");
        var kept = new List<int>();
        for (int f = 0; f < nTri; f++)
        {
            int a0 = t[f * 3], a1 = t[f * 3 + 1], a2 = t[f * 3 + 2];
            float Grow(int x, int y) => (v[x] - v[y]).magnitude / Mathf.Max(1e-9f, (before[x] - before[y]).magnitude);
            bool moved = side[a0] != 0 || side[a1] != 0 || side[a2] != 0;
            if (moved && Mathf.Max(Grow(a0, a1), Mathf.Max(Grow(a1, a2), Grow(a2, a0))) > 2f) { dropped++; continue; }
            if (button[f] || removeSet.Contains(f)) { dropped++; continue; }
            if (DropJunction && (copyOf.ContainsKey(a0) || copyOf.ContainsKey(a1) || copyOf.ContainsKey(a2) || copies.Contains(a0) || copies.Contains(a1) || copies.Contains(a2)))
            { dropped++; junction++; continue; }
            kept.Add(a0); kept.Add(a1); kept.Add(a2);
        }
        Plugin.L.LogInfo($"[PEIGNOIR] triangles de la jonction retirés : {junction}");
        if (dropped > 0) { t = kept; nTri = t.Count / 3; Plugin.L.LogInfo($"[PEIGNOIR] {dropped} triangle(s) étiré(s) retiré(s) à l'ouverture"); }

        // ---- 3. Longueur : la rangée de l'ourlet descend jusqu'aux genoux ----
        // (l'ourlet de la veste est replié, sans bord libre où accrocher des rangées). Sa texture est recalculée par le
        // gradient du triangle au-dessus : le motif continue sur la bande allongée au lieu de s'étirer.
        float bottom = knee + (waist - knee) * 0.08f;
        float ringTop = hem + (waist - hem) * 0.25f;
        var tris = new Dictionary<int, List<int>>();
        for (int f = 0; f < nTri; f++)
            for (int k = 0; k < 3; k++)
            {
                int i0 = t[f * 3 + k];
                if (!tris.TryGetValue(i0, out var l)) tris[i0] = l = new List<int>();
                l.Add(f);
            }
        var moves = new List<(int i, Vector3 p, Vector2 uv, BoneWeight w)>();
        for (int i = 0; i < v.Count; i++)
        {
            if (H(v[i]) > ringTop || !tris.TryGetValue(i, out var fs)) continue;
            // triangle le plus haut qui touche ce sommet : son gradient de texture
            int best = fs[0]; float bh = float.MinValue;
            foreach (var f in fs) { float hc = H(Cen(f)); if (hc > bh) { bh = hc; best = f; } }
            var grad = Gradient(v[t[best * 3]], v[t[best * 3 + 1]], v[t[best * 3 + 2]], uv[t[best * 3]], uv[t[best * 3 + 1]], uv[t[best * 3 + 2]]);
            var lat = v[i] - axis; lat -= up * Vector3.Dot(lat, up);
            var np = v[i] + up * (bottom - H(v[i])) + lat * Flare;
            var d = np - v[i];
            int thigh = Mathf.Abs(Side(v[i]) - Side(P(thighL))) < Mathf.Abs(Side(v[i]) - Side(P(thighR))) ? thighL : thighR;
            moves.Add((i, np, uv[i] + new Vector2(Vector3.Dot(d, grad.du), Vector3.Dot(d, grad.dv)), Blend(w[i], thigh, 0.75f)));
        }
        foreach (var m in moves) { v[m.i] = m.p; uv[m.i] = m.uv; w[m.i] = m.w; }
        // Le reste du bas (entre la taille et l'ourlet) suit un peu les cuisses
        for (int i = 0; i < v.Count; i++)
        {
            float h = H(v[i]);
            if (h >= waist || h <= ringTop) continue;
            float k2 = Mathf.InverseLerp(waist, ringTop, h);
            int thigh = Mathf.Abs(Side(v[i]) - Side(P(thighL))) < Mathf.Abs(Side(v[i]) - Side(P(thighR))) ? thighL : thighR;
            w[i] = Blend(w[i], thigh, 0.3f * k2);
        }
        var extruded = moves;
        int newTris = 0;

        var mesh = new Mesh { name = Robe.Name, hideFlags = HideFlags.DontUnloadUnusedAsset };
        if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = v.ToArray();
        mesh.uv = uv.ToArray();
        mesh.triangles = t.ToArray();
        mesh.boneWeights = w.ToArray();
        mesh.bindposes = bind;
        mesh.RecalculateNormals();
        // Légèrement gonflé : la chemise ne ressort plus à l'arrière des bras
        // (direction moyenne par position : les sommets dupliqués aux coutures bougent ensemble, sans fente)
        Vector3[] nrm = mesh.normals;
        var inflated = v.ToArray();
        var avg = new Dictionary<(int, int, int), Vector3>();
        (int, int, int) Key(Vector3 x) => ((int)Mathf.Round(x.x / q), (int)Mathf.Round(x.y / q), (int)Mathf.Round(x.z / q));
        for (int i = 0; i < inflated.Length && i < nrm.Length; i++) { var k = Key(inflated[i]); avg[k] = (avg.TryGetValue(k, out var a0) ? a0 : Vector3.zero) + nrm[i]; }
        for (int i = 0; i < inflated.Length; i++) inflated[i] += avg[Key(v[i])].normalized * (halfW * 0.03f);
        // Doublure : la veste du jeu n'a pas de face intérieure (on voyait à travers par l'ouverture). Copie de chaque
        // triangle tournée vers l'intérieur, sur des sommets à part (normales vers l'intérieur, éclairage juste).
        int n0 = inflated.Length;
        var allV = new Vector3[n0 * 2]; var allUv = new Vector2[n0 * 2]; var allW = new BoneWeight[n0 * 2];
        for (int i = 0; i < n0; i++)
        {
            allV[i] = inflated[i]; allV[n0 + i] = inflated[i];   // même position : un décalage ressortait à travers les plis fins (revers)
            allUv[i] = allUv[n0 + i] = uv[i];
            allW[i] = allW[n0 + i] = w[i];
        }
        var allT = new int[t.Count * 2];
        for (int i = 0; i < t.Count; i += 3)
        {
            allT[i] = t[i]; allT[i + 1] = t[i + 1]; allT[i + 2] = t[i + 2];
            allT[t.Count + i] = n0 + t[i]; allT[t.Count + i + 1] = n0 + t[i + 2]; allT[t.Count + i + 2] = n0 + t[i + 1];
        }
        mesh.triangles = new int[0];
        mesh.vertices = allV;
        mesh.uv = allUv;
        mesh.boneWeights = allW;
        mesh.triangles = allT;
        if (DebugLabels)
        {
            // Diagnostic : 0 = reste de la veste, 1 = pan "-1", 2 = pan "+1", 3 = doublure
            var subs = new List<int>[4];
            for (int k = 0; k < 4; k++) subs[k] = new List<int>();
            for (int i = 0; i < t.Count; i += 3)
            {
                int sgn = (i / 3 < nTri) ? side[Mathf.Min(t[i], side.Length - 1)] + side[Mathf.Min(t[i + 1], side.Length - 1)] + side[Mathf.Min(t[i + 2], side.Length - 1)] : 0;
                int sub = sgn < 0 ? 1 : sgn > 0 ? 2 : 0;
                subs[sub].Add(allT[i]); subs[sub].Add(allT[i + 1]); subs[sub].Add(allT[i + 2]);
                subs[3].Add(allT[t.Count + i]); subs[3].Add(allT[t.Count + i + 1]); subs[3].Add(allT[t.Count + i + 2]);
            }
            mesh.subMeshCount = 4;
            for (int k = 0; k < 4; k++) mesh.SetTriangles(subs[k].ToArray(), k);
        }
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Plugin.L.LogInfo($"[PEIGNOIR] veste découpée : {split} sommets séparés au devant, ourlet abaissé ({extruded.Count} sommets)");
        return mesh;
    }

    // Gradients (du, dv) tels que uv(p) ≈ uv0 + (p - p0)·du , (p - p0)·dv sur le triangle
    static (Vector3 du, Vector3 dv) Gradient(Vector3 p0, Vector3 p1, Vector3 p2, Vector2 t0, Vector2 t1, Vector2 t2)
    {
        var e1 = p1 - p0; var e2 = p2 - p0;
        var n = Vector3.Cross(e1, e2);
        float nn = n.sqrMagnitude;
        if (nn < 1e-20f) return (Vector3.zero, Vector3.zero);
        // base duale du plan du triangle
        var g1 = Vector3.Cross(e2, n) / nn;    // gradient de la coordonnée barycentrique 1
        var g2 = Vector3.Cross(n, e1) / nn;    // gradient de la coordonnée barycentrique 2
        var d1 = t1 - t0; var d2 = t2 - t0;
        return (g1 * d1.x + g2 * d2.x, g1 * d1.y + g2 * d2.y);
    }

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
