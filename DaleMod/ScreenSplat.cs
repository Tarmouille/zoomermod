using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DaleMod;

// Éclaboussures sur l'écran (chez le joueur seul) : taches posées en bas de l'écran, qui glissent un peu vers le bas
// puis s'effacent. Images sur le canevas du mod (GameUi), sous les textes.
static class ScreenSplat
{
    const float Stay = 3.5f, Fade = 2.5f;

    class Spot { public RectTransform rt; public RawImage img; public float born, slide; }
    static readonly List<Spot> spots = new();

    internal static void Burst(Blood.Palette pal, int count)
    {
        var root = GameUi.Root();
        float w = Screen.width, h = Screen.height;
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("DaleMod_ScreenSplat");
            go.transform.SetParent(root, false);
            go.transform.SetAsFirstSibling();   // sous les textes du mod
            var img = go.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.texture = Blood.SplatTexture(pal, Random.value < 0.6f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;   // coin bas gauche
            float size = h * Random.Range(0.1f, 0.26f);
            rt.sizeDelta = new Vector2(size, size * Random.Range(0.8f, 1.2f));
            rt.anchoredPosition = new Vector2(Random.Range(0.04f, 0.96f) * w, Random.Range(-0.02f, 0.16f) * h);
            rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            spots.Add(new Spot { rt = rt, img = img, born = Time.time + Random.Range(0f, 0.25f), slide = h * Random.Range(0.005f, 0.03f) });
            img.color = new Color(1f, 1f, 1f, 0f);
        }
    }

    internal static void Tick()
    {
        for (int i = spots.Count - 1; i >= 0; i--)
        {
            var s = spots[i];
            if (s.rt == null) { spots.RemoveAt(i); continue; }
            float age = Time.time - s.born;
            if (age < 0f) continue;
            if (age >= Stay + Fade) { Object.Destroy(s.rt.gameObject); spots.RemoveAt(i); continue; }
            float a = age < 0.08f ? age / 0.08f : age < Stay ? 1f : 1f - (age - Stay) / Fade;
            s.img.color = new Color(1f, 1f, 1f, 0.92f * a);
            s.rt.anchoredPosition += Vector2.down * (s.slide * Time.deltaTime / (Stay + Fade));   // coule doucement
        }
    }
}
