using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Player;
using Player.Tasks;
using Props.Printer;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "L'Autographe-gate" (rouge, Slacker ; voir TaskPools) : comme le Moulin à vent (poste, Paint, impression),
// mais au lieu de punaiser le dessin, on le fait signer par un autre joueur (n'importe lequel).
// - "Mon" dessin : la première image imprimée qui apparaît juste après l'impression depuis le Paint de mon PC.
// - Signature : commande du jeu (Document.CmdSignDocument -> RpcSignDocument(signataire), chez tous les clients).
// Le jeu ne montre pas la signature sur une image imprimée : on lui donne le texte de signature d'un document normal.
// Même mécanique pour "STONKS" (bleue) : la belle courbe qui monte doit être signée par le MANAGER.
static class Autograph
{
    internal const string Key = "dalemod.autographeGate";
    internal const string SignedAction = "Signed";
    internal const string StonksKey = "dalemod.stonks", ManagerSignedAction = "Manager Signed";
    const float MineWindow = 6f;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    static float lastPrint = -99f;
    static uint myDrawing;
    static readonly HashSet<uint> seen = new(), fixedImages = new();

    // Appelé par Windmill quand le Paint du PC local imprime
    internal static void NotePrint() { lastPrint = Time.time; myDrawing = 0; }

    internal static void Tick()
    {
        foreach (var img in Object.FindObjectsOfType<PrintedImage>())
        {
            if (seen.Add(img.netId) && Time.time - lastPrint < MineWindow && myDrawing == 0)
            {
                myDrawing = img.netId;
                lastPrint = -99f;
                Plugin.L.LogInfo($"[AUTOGRAPH] mon dessin : {img.netId}");
            }
            // Image déjà signée (joueur arrivé après la signature) : signature affichée aussi
            if (img.signed && !fixedImages.Contains(img.netId)) EnsureSignature(img, null, null);
        }
    }

    // --- Signature : signataire lu dans le message (sans le consommer), puis traitement après le jeu ---
    [HarmonyPatch(typeof(Document), nameof(Document.InvokeUserCode_RpcSignDocument__NetworkIdentity))]
    [HarmonyPrefix]
    static void BeforeSign(NetworkReader reader, out NetworkIdentity __state)
    {
        __state = null;
        if (reader == null) return;
        int pos = reader.Position;
        try { __state = NetworkReaderExtensions.ReadNetworkIdentity(reader); }
        catch { }
        finally { reader.Position = pos; }
    }

    [HarmonyPatch(typeof(Document), nameof(Document.InvokeUserCode_RpcSignDocument__NetworkIdentity))]
    [HarmonyPostfix]
    static void AfterSign(NetworkBehaviour obj, NetworkIdentity __state)
    {
        var img = obj?.TryCast<PrintedImage>();
        if (img == null) return;
        var signer = __state?.GetComponent<PlayerController>()?.lobbyPlayer;
        string name = signer?.username;
        Plugin.L.LogInfo($"[AUTOGRAPH] image {img.netId} signée par {name ?? "?"} (mon dessin={img.netId == myDrawing})");
        try { EnsureSignature(img, name, signer); }
        catch (Exception e) { Plugin.L.LogError("[AUTOGRAPH] affichage : " + e); }

        var me = NetworkClient.localPlayer;
        if (img.netId != myDrawing || __state == null) return;
        bool self = __state == me;
        if (TaskActive())
        {
            if (self) return;   // Autographe-gate : il faut un AUTRE joueur
            TaskHook.TriggerTaskHookCommandStatic(Windmill.Hook(SignedAction));
            Plugin.L.LogInfo("[AUTOGRAPH] dessin signé par un collègue : tâche validée");
        }
        else if (CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), StonksKey))
        {
            // Manager uniquement ; un Manager qui a lui-même la tâche (test via F7) peut signer sa propre courbe
            if (signer?.playerRole != Player.Lobby.PlayerRole.Manager)
            {
                Plugin.L.LogInfo($"[STONKS] signé par {name} qui n'est pas le manager : ne compte pas");
                return;
            }
            TaskHook.TriggerTaskHookCommandStatic(Windmill.Hook(ManagerSignedAction));
            Plugin.L.LogInfo("[STONKS] courbe signée par le manager : tâche validée");
        }
    }

    // Le texte de signature d'un document normal, posé sur l'image (ramené dans l'image, près du bas), à la couleur du joueur
    static void EnsureSignature(PrintedImage img, string name, Player.Lobby.LobbyPlayer signer)
    {
        var st = img.signedText;
        bool diag = fixedImages.Add(img.netId);
        if (diag)
            Plugin.L.LogInfo($"[AUTOGRAPH] signedText={(st == null ? "absent" : $"actif={st.gameObject.activeInHierarchy} texte='{st.text}' pos={st.transform.localPosition} échelle={st.transform.localScale}")}" +
                             $" rendu={(img.renderer == null ? "-" : img.renderer.bounds.size.ToString())}");
        if (st == null)
        {
            var tpl = Printer.instance?.documentPrefab?.signedText;
            if (tpl == null) { Plugin.L.LogWarning("[AUTOGRAPH] modèle de signature introuvable"); return; }
            var go = Object.Instantiate(tpl.gameObject, img.transform);
            go.transform.localPosition = tpl.transform.localPosition;
            go.transform.localRotation = tpl.transform.localRotation;
            go.transform.localScale = tpl.transform.localScale;
            st = go.GetComponent<TextMeshPro>();
            img.signedText = st;
        }
        if (st == null) return;
        st.gameObject.SetActive(true);
        st.enabled = true;
        if (!string.IsNullOrEmpty(name) && string.IsNullOrWhiteSpace(st.text)) st.text = name;
        var colors = LobbyManager.instance?.playerColors;
        if (signer != null && colors != null && signer.playerColorIndex >= 0 && signer.playerColorIndex < colors.Count)
            st.color = colors[signer.playerColorIndex];   // comme sur les documents : couleur du signataire
        if (diag)
        {
            InsideImage(img, st.transform);
            st.transform.position -= st.transform.forward * 0.002f;   // un poil devant l'image (le texte regarde vers -z)
            st.sortingOrder = 10;
            Plugin.L.LogInfo($"[AUTOGRAPH] signature placée en {st.transform.localPosition}");
        }
    }

    // La position copiée d'un document tombe sous l'image (dans le vide) : on la ramène dans le rectangle de l'image,
    // avec une marge, sur les deux grands axes du maillage (le 3e est l'épaisseur de la feuille)
    static void InsideImage(PrintedImage img, Transform sig)
    {
        var mesh = img.renderer?.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null) return;
        var rt = img.renderer.transform;
        var b = mesh.bounds;
        var lp = rt.InverseTransformPoint(sig.position);
        var size = b.size;
        int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        for (int i = 0; i < 3; i++)
        {
            if (i == thin) continue;
            float m = size[i] * 0.12f;
            lp[i] = Mathf.Clamp(lp[i], b.min[i] + m, b.max[i] - m);
        }
        sig.position = rt.TransformPoint(lp);
    }
}
