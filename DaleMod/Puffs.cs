using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Bouffées douces (fumée du PC mouillé, odeur verte des aliments périmés…), chez chaque client :
// quad face caméra + texture de nuage générée, qui monte, tourne lentement, grossit et s'efface.
// Fait main : le système de particules est trop amputé dans l'interop (le clone de la fumée de la poubelle partait au loin).
static class Puffs
{
    class Puff { public Transform t; public Material m; public float born, life, rise, alpha, spin, angle, size; public Color tint; public Vector3 from, drift; }
    static readonly List<Puff> puffs = new();
    static Shader shader;
    static bool urp;
    static Texture2D tex;

    // tint : couleur (gris = fumée, vert = odeur) ; size : taille finale (m) ; drift : dérive horizontale max (m)
    internal static void Emit(Vector3 at, Color tint, float size, float rise, float life, float alpha, float drift)
    {
        var mat = Material();
        if (mat == null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "DaleMod_Puff";
        Object.Destroy(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.transform.position = at;
        go.transform.localScale = Vector3.zero;
        float shade = UnityEngine.Random.Range(0.85f, 1.15f);   // teinte légèrement variable
        puffs.Add(new Puff
        {
            t = go.transform, m = mat, born = Time.time, from = at, life = life, rise = rise, alpha = alpha, size = size,
            tint = new Color(tint.r * shade, tint.g * shade, tint.b * shade),
            drift = new Vector3(UnityEngine.Random.Range(-drift, drift), 0f, UnityEngine.Random.Range(-drift, drift)),
            angle = UnityEngine.Random.Range(0f, 360f), spin = UnityEngine.Random.Range(-40f, 40f),
        });
    }

    internal static void Tick()
    {
        var cam = Camera.main;
        for (int i = puffs.Count - 1; i >= 0; i--)
        {
            var p = puffs[i];
            float k = (Time.time - p.born) / p.life;
            if (p.t == null || k >= 1f)
            {
                if (p.t != null) Object.Destroy(p.t.gameObject);
                if (p.m != null) Object.Destroy(p.m);
                puffs.RemoveAt(i);
                continue;
            }
            float ease = 1f - (1f - k) * (1f - k);   // monte vite puis ralentit
            p.t.position = p.from + Vector3.up * (p.rise * ease) + p.drift * ease;
            if (cam != null)   // face à la caméra, tournée sur elle-même
                p.t.rotation = Quaternion.LookRotation(p.t.position - cam.transform.position, cam.transform.up)
                               * Quaternion.Euler(0f, 0f, p.angle + p.spin * (Time.time - p.born));
            p.t.localScale = Vector3.one * Mathf.Lerp(p.size * 0.3f, p.size, ease);
            SetColor(p.m, p.tint, p.alpha * Mathf.Min(1f, k * 5f) * (1f - k) * (1f - k));
        }
    }

    // Matériau transparent par bouffée (chacune a son opacité), texture partagée
    static Material Material()
    {
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
            if (shader == null) { shader = Shader.Find("Universal Render Pipeline/Unlit"); urp = shader != null; }
            Plugin.L.LogInfo("[PUFF] shader : " + (shader == null ? "aucun" : shader.name));
            if (shader == null) return null;
        }
        tex ??= BuildTexture();
        var m = new Material(shader) { mainTexture = tex };
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (urp)   // URP Unlit en mode transparent
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", 5f); m.SetFloat("_DstBlend", 10f); m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
        }
        SetColor(m, Color.white, 0f);
        return m;
    }

    // Nuage doux : plusieurs disques flous superposés (bosses irrégulières), bord effiloché, blanc (teinté par la couleur)
    static Texture2D BuildTexture()
    {
        const int S = 64;
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
        var rnd = new System.Random(5);
        var blobs = new List<(float x, float y, float r)> { (0.5f, 0.5f, 0.32f) };
        for (int i = 0; i < 7; i++)
        {
            double a = rnd.NextDouble() * Math.PI * 2, d = 0.12 + rnd.NextDouble() * 0.12;
            blobs.Add((0.5f + (float)(Math.Cos(a) * d), 0.5f + (float)(Math.Sin(a) * d), 0.14f + (float)rnd.NextDouble() * 0.1f));
        }
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S, a = 0f;
                foreach (var (bx, by, br) in blobs)
                {
                    float d = Mathf.Sqrt((u - bx) * (u - bx) + (v - by) * (v - by)) / br;
                    a = Mathf.Max(a, Mathf.Clamp01(1f - d * d));   // disque au bord très doux
                }
                a *= 0.75f + 0.25f * (float)rnd.NextDouble();      // grain
                float edge = Mathf.Clamp01((0.5f - Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f))) / 0.08f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * edge));   // SetPixel unitaire (pas de Span dans l'interop)
            }
        t.Apply();
        return t;
    }

    static void SetColor(Material m, Color tint, float a)
    {
        var c = new Color(tint.r, tint.g, tint.b, a);
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
    }
}
