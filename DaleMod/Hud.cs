using TMPro;
using UnityEngine;

namespace DaleMod;

// Message temporaire en haut de l'écran du joueur local (comptes rendus de tâche : réussite, échec, compteurs…),
// en TextMeshPro avec la police du jeu (GameUi), fondu à la fin.
static class Hud
{
    static TextMeshProUGUI label;
    static float until = -1f, shownFor = 1f;

    internal static void Show(string message, float seconds)
    {
        if (label == null)
        {
            label = GameUi.NewText("DaleMod_Hud");
            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -Screen.height * 0.2f);
            rt.sizeDelta = new Vector2(Screen.width * 0.6f, 200f);
            label.alignment = TextAlignmentOptions.Top;
            label.color = Color.white;
        }
        GameUi.Style(label);
        label.fontSize = Mathf.Max(22f, Screen.height / 32f);
        label.text = message;
        until = Time.time + seconds;
        shownFor = seconds;
        label.gameObject.SetActive(true);
    }

    // Gros message au milieu de l'écran (ex. "Tu es bâillonné"), même fondu
    static TextMeshProUGUI big;
    static float bigUntil = -1f;

    internal static void ShowBig(string message, float seconds)
    {
        if (big == null)
        {
            big = GameUi.NewText("DaleMod_HudBig");
            var rt = big.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(Screen.width * 0.7f, Screen.height * 0.4f);
            big.alignment = TextAlignmentOptions.Center;
        }
        GameUi.Style(big);
        big.fontSize = Mathf.Max(36f, Screen.height / 16f);
        big.text = message;
        big.color = Color.white;
        bigUntil = Time.time + seconds;
        big.gameObject.SetActive(true);
    }

    internal static void HideBig() { if (big != null) big.gameObject.SetActive(false); bigUntil = -1f; }

    // Appelé par DebugMenu.Update : fondu sur la dernière demi-seconde, puis masqué
    internal static void Tick()
    {
        if (big != null && big.gameObject.activeSelf)
        {
            float l = bigUntil - Time.time;
            if (l <= 0f) big.gameObject.SetActive(false);
            else big.color = new Color(1f, 1f, 1f, Mathf.Clamp01(l / 0.5f));
        }
        if (label == null || !label.gameObject.activeSelf) return;
        float left = until - Time.time;
        if (left <= 0f) { label.gameObject.SetActive(false); return; }
        var c = label.color;
        c.a = Mathf.Clamp01(left / Mathf.Min(0.5f, shownFor));
        label.color = c;
    }
}
