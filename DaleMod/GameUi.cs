using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Textes du mod dans le style du jeu : un canevas d'interface Unity (comme l'interface du jeu, pas une surcouche IMGUI)
// et des TextMeshPro avec la police et le matériau (contour, ombre) d'un texte du jeu lui-même.
// Le style est repris du texte d'interaction du HUD en partie, sinon de n'importe quel texte d'interface (menu).
static class GameUi
{
    static Canvas canvas;
    static TMP_FontAsset font;
    static Material fontMaterial;
    static float nextStyleCheck;

    internal static Transform Root()
    {
        if (canvas != null) return canvas.transform;
        var go = new GameObject("DaleMod_UI");
        Object.DontDestroyOnLoad(go);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;   // au-dessus du HUD du jeu
        return canvas.transform;
    }

    // Sortie de partie : tous les textes et images du mod masqués, sauf le badge du menu (nom et logo du mod)
    internal static void HideAll()
    {
        if (canvas == null) return;
        var root = canvas.transform;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.name != "DaleMod_Badge" && c.name != "DaleMod_BadgeLogo") c.gameObject.SetActive(false);
        }
    }

    internal static TextMeshProUGUI NewText(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(Root(), false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.raycastTarget = false;
        t.enableWordWrapping = true;
        Style(t);
        return t;
    }

    // Police et matériau d'un texte du jeu (réappliqués si la scène change : menu -> partie)
    internal static void Style(TextMeshProUGUI t)
    {
        if (t == null) return;
        if (Time.unscaledTime >= nextStyleCheck || font == null)
        {
            nextStyleCheck = Time.unscaledTime + 2f;
            TextMeshProUGUI source = Object.FindObjectOfType<CameraController>()?.interactText;
            if (source == null)
                foreach (var x in Object.FindObjectsOfType<TextMeshProUGUI>())
                    if (x.font != null && !x.transform.IsChildOf(Root())) { source = x; break; }
            if (source != null && source.font != null) { font = source.font; fontMaterial = source.fontSharedMaterial; }
        }
        if (font != null && t.font != font) t.font = font;
        if (fontMaterial != null && t.fontSharedMaterial != fontMaterial) t.fontSharedMaterial = fontMaterial;
    }
}
