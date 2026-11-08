using System;
using Mirror;
using Player.Scripts;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace DaleMod;

// Touche de pouvoir des rôles bonus (E, maintenue) : joueur visé à portée, et barre de chargement des actions du jeu
// copiée en bas de l'écran (celle du jeu, CameraController.loadingBar, est cachée et remise à zéro quand rien n'est
// visé ; la copie n'appartient qu'à nous).
static class RoleKey
{
    internal static bool Held => Keyboard.current != null && Keyboard.current.eKey.isPressed;

    // Autre joueur visé : le rayon de la caméra passe à moins de BodyRadius de son corps (segment vertical des pieds à
    // la tête), devant soi, et il est à portée (distance au sol). Un angle vers le torse ne marche pas de près.
    const float BodyRadius = 0.4f, BodyLow = 0.2f, BodyHigh = 1.8f;

    internal static Player.PlayerController AimedPlayer(float range)
    {
        var cam = GameCamera();
        var me = NetworkClient.localPlayer;
        if (cam == null || me == null) { Trace(cam == null ? "pas de caméra" : "pas de joueur local"); return null; }
        Player.PlayerController best = null;
        float bestGap = BodyRadius;
        string seen = "";
        foreach (var pc in Object.FindObjectsOfType<Player.PlayerController>())
        {
            if (pc == null || pc.netIdentity == null || pc.netIdentity == me) continue;
            var feet = pc.transform.position;
            float dist = Vector3.Distance(Flat(feet), Flat(me.transform.position));
            float gap = RayGap(cam, feet);
            seen += $" {pc.lobbyPlayer?.username}: {dist:0.00} m, écart {gap:0.00} m;";
            if (dist > range || gap >= bestGap) continue;
            bestGap = gap; best = pc;
        }
        Trace($"joueurs :{seen} -> {(best == null ? "aucun" : best.lobbyPlayer?.username)}");
        return best;
    }

    // Plus petite distance entre le rayon de la caméra (vers l'avant) et le corps debout sur feet
    static float RayGap(Transform cam, Vector3 feet)
    {
        float best = float.MaxValue;
        for (int k = 0; k <= 16; k++)
        {
            var point = feet + Vector3.up * Mathf.Lerp(BodyLow, BodyHigh, k / 16f);
            var v = point - cam.position;
            float along = Vector3.Dot(v, cam.forward);
            if (along <= 0f) continue;   // derrière la caméra
            best = Mathf.Min(best, (v - cam.forward * along).magnitude);
        }
        return best;
    }

    // Traces de la touche E : au plus une ligne par sujet toutes les 0,5 s, tant que E est maintenue
    static readonly System.Collections.Generic.Dictionary<string, float> traced = new();
    internal static void Trace(string what)
    {
        if (!Held) { traced.Clear(); return; }
        string key = what.Split(':')[0];
        if (traced.TryGetValue(key, out var t) && Time.time - t < 0.5f) return;
        traced[key] = Time.time;
        Plugin.L.LogInfo("[ROLES] touche E : " + what);
    }

    // La caméra du jeu (Camera.main n'est pas fiable ici)
    static Transform GameCamera()
    {
        var cc = Object.FindObjectOfType<CameraController>();
        if (cc != null && cc.mainCamera != null) return cc.mainCamera.transform;
        if (cc != null && cc.camera != null) return cc.camera;
        return Camera.main != null ? Camera.main.transform : null;
    }

    // --- Mannequin de test (menu F7, Debug) : une cible pour la touche E quand on joue seul ---
    static GameObject dummy;

    internal static string ToggleDummy()
    {
        if (dummy != null) { Object.Destroy(dummy); dummy = null; return "Mannequin retiré"; }
        var me = NetworkClient.localPlayer;
        if (me == null) return "Joueur local introuvable";
        var fwd = me.transform.forward; fwd.y = 0f; fwd.Normalize();
        dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        dummy.name = "DaleMod_Mannequin";
        Object.Destroy(dummy.GetComponent<Collider>());
        dummy.transform.position = me.transform.position + fwd * 0.8f + Vector3.up * 0.9f;
        dummy.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
        var r = dummy.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = DummyMaterial();
        Plugin.L.LogInfo($"[ROLES] mannequin en {dummy.transform.position} (joueur en {me.transform.position}), shader {r?.sharedMaterial?.shader?.name}");
        return "Mannequin posé devant toi (vise-le et maintiens E)";
    }

    // Le shader par défaut d'une primitive n'est pas rendu par le pipeline du jeu : on part d'un matériau du jeu
    static Material DummyMaterial()
    {
        var orange = new Color(1f, 0.55f, 0.05f);
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        Material m = null;
        if (lit != null) m = new Material(lit);
        else
            foreach (var mr in Object.FindObjectsOfType<MeshRenderer>())
                if (mr.sharedMaterial != null && mr.gameObject.name != "DaleMod_Mannequin") { m = new Material(mr.sharedMaterial); break; }
        if (m == null) return null;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", orange);
        if (m.HasProperty("_Color")) m.SetColor("_Color", orange);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
        return m;
    }

    // Le mannequin est visé (mêmes règles qu'un joueur)
    internal static bool AimedDummy(float range)
    {
        if (dummy == null) return false;
        var cam = GameCamera();
        var me = NetworkClient.localPlayer;
        if (cam == null || me == null) return false;
        var feet = dummy.transform.position - Vector3.up * 0.9f;
        float dist = Vector3.Distance(Flat(feet), Flat(me.transform.position));
        float gap = RayGap(cam, feet);
        Trace($"mannequin : {dist:0.00} m, écart {gap:0.00} m");
        return dist <= range && gap < BodyRadius;
    }

    internal static PlayerInteractable InteractableOf(Player.PlayerController pc)
    {
        if (pc == null || pc.netIdentity == null) return null;
        foreach (var p in Object.FindObjectsOfType<PlayerInteractable>())
            if (p.netIdentity != null && p.netIdentity.Pointer == pc.netIdentity.Pointer) return p;
        return null;
    }

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    // --- Barre ---
    static LoadingBar bar;
    static float shown;

    // progress <= 0 : cachée. y : bord haut de la barre, en pixels depuis le bas de l'écran
    internal static void Bar(float progress, float y)
    {
        if (shown <= 0f && progress <= 0f) return;
        shown = progress;
        var b = GetBar();
        if (b == null) return;
        b.gameObject.SetActive(progress > 0f);
        if (progress <= 0f) return;
        var rt = b.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
        }
        b.SetProgress(progress);
        try { b.UpdateLoadingBar(); } catch (Exception e) { Plugin.L.LogWarning("[ROLES] barre : " + e.Message); }
    }

    static LoadingBar GetBar()
    {
        if (bar != null) return bar;
        var source = Object.FindObjectOfType<CameraController>()?.loadingBar;
        if (source == null) return null;
        var go = Object.Instantiate(source.gameObject, GameUi.Root(), false);
        go.name = "DaleMod_RoleBar";
        bar = go.GetComponent<LoadingBar>();
        // Même taille à l'écran que l'originale (son canevas a sa propre échelle)
        var root = GameUi.Root();
        float k = root.lossyScale.x > 0f ? source.transform.lossyScale.x / root.lossyScale.x : 1f;
        go.transform.localScale = Vector3.one * k;
        Plugin.L.LogInfo($"[ROLES] barre du jeu copiée (échelle {k:0.00})");
        return bar;
    }
}
