using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Sang du broyeur ("Attention Mr. Zoomer"), chez tous les clients (aussi en marron : caca de la tâche "Recyclage") :
//  - Splash : gerbe de gouttes qui jaillissent du haut du broyeur et retombent (gravité) ; chaque goutte qui touche
//    le sol y laisse une petite tache, et quelques grandes flaques apparaissent autour du broyeur.
//  - Drip : quelques gouttes qui tombent régulièrement tant que la main est coincée (appelé par Zoomer).
//  - Release : les taches s'effacent quelques secondes après la libération.
// Fait main (cubes et quads) : le système de particules est trop amputé dans l'interop.
static class Blood
{
    const float Gravity = 9f, SplatFadeDelay = 6f, SplatFadeSeconds = 4f, SplatMaxLife = 180f;
    const int MaxSplats = 90;

    // Couleurs (sang, caca) : gouttes et textures de taches propres à chacune
    internal class Palette
    {
        public Color Dark, Bright; public int Seed;
        public Material DropMat; public Texture2D BigTex, SmallTex;
    }
    internal static readonly Palette Red = new() { Dark = new(0.32f, 0f, 0.02f), Bright = new(0.6f, 0.02f, 0.04f), Seed = 11 };
    internal static readonly Palette Poop = new() { Dark = new(0.2f, 0.11f, 0.03f), Bright = new(0.46f, 0.29f, 0.09f), Seed = 31 };

    class Drop { public Transform t; public Vector3 v; public float floor; public Palette pal; public float life; }
    class Splat { public Transform t; public Material m; public float fadeAt = float.MaxValue; public float alpha; public bool big; }
    static readonly List<Drop> drops = new();
    static readonly List<Splat> splats = new();
    static Shader splatShader;
    static bool splatUrp;
    static float layer;   // petit décalage en hauteur entre taches (pas de scintillement quand elles se chevauchent)

    // Haut et sol d'un objet (le broyeur est posé par terre)
    internal static (Vector3 top, float floor) Bounds(Component c)
    {
        var rs = c.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return (c.transform.position + Vector3.up * 0.8f, c.transform.position.y);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return (new Vector3(b.center.x, b.max.y, b.center.z), b.min.y);
    }

    internal static void Splash(Component shredder)
    {
        var (top, floor) = Bounds(shredder);
        for (int i = 0; i < 70; i++)
        {
            var dir = UnityEngine.Random.insideUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 1.5f + 0.4f;
            SpawnDrop(top, dir.normalized * UnityEngine.Random.Range(1.2f, 3.6f), floor, UnityEngine.Random.Range(0.012f, 0.03f));
        }
        for (int i = 0; i < 4; i++)   // grandes flaques autour du broyeur
        {
            var off = UnityEngine.Random.insideUnitCircle * 0.7f;
            SpawnSplat(new Vector3(top.x + off.x, floor, top.z + off.y), UnityEngine.Random.Range(0.35f, 0.7f), true);
        }
    }

    // Giclure plus petite à un point précis (ex. l'agrafeuse d'un bureau) ; les gouttes retombent jusqu'au sol donné
    // (life > 0 : les taches s'effacent d'elles-mêmes après life secondes)
    internal static void SplashAt(Vector3 at, float floor, int drops, int bigSplats, Palette pal = null, float life = -1f)
    {
        for (int i = 0; i < drops; i++)
        {
            var dir = UnityEngine.Random.insideUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 1.2f + 0.3f;
            SpawnDrop(at, dir.normalized * UnityEngine.Random.Range(0.6f, 1.8f), floor, UnityEngine.Random.Range(0.008f, 0.02f), pal, life);
        }
        for (int i = 0; i < bigSplats; i++)
        {
            var off = UnityEngine.Random.insideUnitCircle * 0.4f;
            SpawnSplat(new Vector3(at.x + off.x, floor, at.z + off.y), UnityEngine.Random.Range(0.25f, 0.4f), true, pal, life);
        }
    }

    // Programme l'effacement de toutes les taches (sans goutte-à-goutte en cours)
    internal static void FadeAll()
    {
        float at = Time.time + SplatFadeDelay;
        foreach (var sp in splats) sp.fadeAt = Mathf.Min(sp.fadeAt, at);
    }

    internal static void Drip(Component shredder)
    {
        var (top, floor) = Bounds(shredder);
        int n = UnityEngine.Random.Range(2, 6);
        for (int i = 0; i < n; i++)
        {
            var v = new Vector3(UnityEngine.Random.Range(-0.6f, 0.6f), UnityEngine.Random.Range(0.3f, 1.4f), UnityEngine.Random.Range(-0.6f, 0.6f));
            SpawnDrop(top, v, floor, UnityEngine.Random.Range(0.01f, 0.02f));
        }
    }

    // Gouttes régulières tant que la main est coincée
    static readonly Dictionary<IntPtr, (Component c, float next)> dripping = new();
    internal static void StartDripping(Component shredder) => dripping[shredder.Pointer] = (shredder, Time.time + 1f);

    // Les taches restent un moment après la libération, puis s'effacent
    internal static void Release(Component shredder)
    {
        dripping.Remove(shredder.Pointer);
        float at = Time.time + SplatFadeDelay;
        foreach (var s in splats) s.fadeAt = Mathf.Min(s.fadeAt, at);
    }

    static void SpawnDrop(Vector3 at, Vector3 v, float floor, float size, Palette pal = null, float life = -1f)
    {
        pal ??= Red;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "DaleMod_Blood";
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = at + UnityEngine.Random.insideUnitSphere * 0.04f;
        go.transform.localScale = Vector3.one * size;
        go.transform.rotation = UnityEngine.Random.rotation;
        var mat = DropMaterial(pal);
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        drops.Add(new Drop { t = go.transform, v = v, floor = floor, pal = pal, life = life });
    }

    static void SpawnSplat(Vector3 at, float size, bool big, Palette pal = null, float life = -1f)
    {
        // Plafond : on retire la plus vieille PETITE tache (les grandes flaques restent jusqu'au nettoyage)
        if (splats.Count >= MaxSplats)
        {
            int i = splats.FindIndex(x => !x.big);
            if (i < 0) return;
            Kill(splats[i]); splats.RemoveAt(i);
        }
        var mat = SplatMaterial(big, pal ?? Red);
        if (mat == null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "DaleMod_BloodSplat";
        Object.Destroy(go.GetComponent<Collider>());
        layer = (layer + 0.0004f) % 0.008f;
        go.transform.position = at + Vector3.up * (0.004f + layer);
        go.transform.rotation = Quaternion.Euler(90f, UnityEngine.Random.Range(0f, 360f), 0f);   // à plat, face vers le haut
        go.transform.localScale = new Vector3(size, size * UnityEngine.Random.Range(0.8f, 1.2f), 1f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        float alpha = UnityEngine.Random.Range(0.8f, 0.95f);
        SetColor(mat, alpha);
        // Plus aucune main coincée (goutte encore en l'air au moment de la libération) : effacement programmé aussi ;
        // et dans tous les cas, pas plus de SplatMaxLife (une tache ne doit jamais rester pour toujours)
        float fadeAt = life > 0f ? Time.time + life : dripping.Count == 0 ? Time.time + SplatFadeDelay : Time.time + SplatMaxLife;
        splats.Add(new Splat { t = go.transform, m = mat, alpha = alpha, big = big, fadeAt = fadeAt });
    }

    static void Kill(Splat s)
    {
        if (s.t != null) Object.Destroy(s.t.gameObject);
        if (s.m != null) Object.Destroy(s.m);
    }

    internal static void Tick()
    {
        float dt = Time.deltaTime;
        foreach (var key in new List<IntPtr>(dripping.Keys))
        {
            var (c, next) = dripping[key];
            if (c == null) { dripping.Remove(key); continue; }
            if (Time.time < next) continue;
            Drip(c);
            dripping[key] = (c, Time.time + UnityEngine.Random.Range(0.6f, 2f));
        }
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            var d = drops[i];
            if (d.t == null) { drops.RemoveAt(i); continue; }
            d.v.y -= Gravity * dt;
            var p = d.t.position + d.v * dt;
            if (p.y <= d.floor)   // touche le sol : petite tache
            {
                SpawnSplat(new Vector3(p.x, d.floor, p.z), UnityEngine.Random.Range(0.06f, 0.16f), false, d.pal, d.life);
                Object.Destroy(d.t.gameObject);
                drops.RemoveAt(i);
                continue;
            }
            d.t.position = p;
        }
        for (int i = splats.Count - 1; i >= 0; i--)
        {
            var s = splats[i];
            if (Time.time < s.fadeAt) continue;
            float k = (Time.time - s.fadeAt) / SplatFadeSeconds;
            if (k >= 1f || s.t == null) { Kill(s); splats.RemoveAt(i); continue; }
            SetColor(s.m, s.alpha * (1f - k));
        }
    }

    // --- Matériaux ---
    static Material DropMaterial(Palette pal)
    {
        if (pal.DropMat != null) return pal.DropMat;
        foreach (var name in new[] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default" })
        {
            var sh = Shader.Find(name);
            if (sh == null) continue;
            pal.DropMat = new Material(sh) { hideFlags = HideFlags.DontUnloadUnusedAsset, color = pal.Bright };
            if (pal.DropMat.HasProperty("_BaseColor")) pal.DropMat.SetColor("_BaseColor", pal.Bright);
            return pal.DropMat;
        }
        return null;
    }

    // Texture de tache (aussi pour les éclaboussures à l'écran)
    internal static Texture2D SplatTexture(Palette pal, bool big) =>
        big ? (pal.BigTex ??= BuildSplat(128, pal.Seed, true, pal)) : (pal.SmallTex ??= BuildSplat(48, pal.Seed + 12, false, pal));

    // Une instance par tache (chacune s'efface à son rythme), texture partagée
    static Material SplatMaterial(bool big, Palette pal)
    {
        if (splatShader == null)
        {
            splatShader = Shader.Find("Sprites/Default");
            if (splatShader == null) { splatShader = Shader.Find("Universal Render Pipeline/Unlit"); splatUrp = splatShader != null; }
            if (splatShader == null) return null;
        }
        var tex = SplatTexture(pal, big);
        var m = new Material(splatShader) { mainTexture = tex };
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (splatUrp)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", 5f); m.SetFloat("_DstBlend", 10f); m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
        }
        return m;
    }

    static void SetColor(Material m, float a)
    {
        var c = new Color(1f, 1f, 1f, a);   // la texture porte déjà la couleur
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
    }

    // Tache : flaque irrégulière (disques superposés), plus sombre au centre, traînées en étoile terminées par une
    // gouttelette, et éclats projetés autour (réglages vérifiés sur un aperçu Python)
    static Texture2D BuildSplat(int S, int seed, bool big, Palette pal)
    {
        var rnd = new System.Random(seed);
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
        var blobs = new List<(float x, float y, float r)> { (0.5f, 0.5f, big ? 0.15f : 0.24f) };
        for (int i = 0; i < (big ? 16 : 6); i++)
        {
            double a = rnd.NextDouble() * Math.PI * 2, d = 0.06 + rnd.NextDouble() * (big ? 0.13 : 0.12);
            float r = big ? 0.035f + (float)rnd.NextDouble() * 0.07f : 0.06f + (float)rnd.NextDouble() * 0.08f;
            blobs.Add((0.5f + (float)(Math.Cos(a) * d), 0.5f + (float)(Math.Sin(a) * d), r));
        }
        var spikes = new List<(double a, float len, float w)>();
        if (big)
        {
            for (int i = 0; i < 11; i++)
            {
                double a = rnd.NextDouble() * Math.PI * 2;
                float len = 0.24f + (float)rnd.NextDouble() * 0.22f, w = 0.018f + (float)rnd.NextDouble() * 0.02f;
                spikes.Add((a, len, w));
                blobs.Add((0.5f + (float)(Math.Cos(a) * (len + w * 0.6f)), 0.5f + (float)(Math.Sin(a) * (len + w * 0.6f)), w * 1.15f));
            }
            for (int i = 0; i < 22; i++)
            {
                double a = rnd.NextDouble() * Math.PI * 2, d = 0.24 + rnd.NextDouble() * 0.24;
                blobs.Add((0.5f + (float)(Math.Cos(a) * d), 0.5f + (float)(Math.Sin(a) * d), 0.007f + (float)rnd.NextDouble() * 0.02f));
            }
        }

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S, cover = 0f;
                foreach (var (bx, by, br) in blobs)
                {
                    float d = Mathf.Sqrt((u - bx) * (u - bx) + (v - by) * (v - by)) / br;
                    cover = Mathf.Max(cover, Mathf.Clamp01((1f - d) * br * S * 0.6f));   // bord net mais antialiasé
                }
                foreach (var (a, len, w) in spikes)   // traînée fine qui s'amincit vers l'extérieur
                {
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float along = dx * (float)Math.Cos(a) + dy * (float)Math.Sin(a);
                    float across = Mathf.Abs(-dx * (float)Math.Sin(a) + dy * (float)Math.Cos(a));
                    if (along <= 0f || along > len) continue;
                    float width = w * (0.45f + 0.55f * (1f - along / len));
                    cover = Mathf.Max(cover, Mathf.Clamp01((width - across) * S * 0.8f));
                }
                if (cover <= 0f) { t.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }
                float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                var c = Color.Lerp(pal.Dark, pal.Bright, Mathf.Clamp01(r * 1.6f) * (0.85f + 0.15f * (float)rnd.NextDouble()));
                c.a = cover;
                t.SetPixel(x, y, c);   // SetPixel unitaire (pas de Span dans l'interop)
            }
        t.Apply();
        return t;
    }
}
