using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using Player.Lobby;
using Player.PlayerEffects;
using Props.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Sachet consommable (ModItem copié du soda), renommé "Coke Bag", modèle Assets\cokebag.obj et action "Sniffer".
static class CokeBag
{
    internal const string Name = "Coke Bag";

    internal static readonly ModItem Item = new(Name, 0xC0CA1001, "cokebag.obj", new Vector3(0.09f, 0.03f, 0.06f),
        new Dictionary<string, Color>
        {
            ["powder"] = new Color(0.95f, 0.95f, 0.93f),
            ["plastic"] = new Color(0.78f, 0.84f, 0.89f),
            ["zip"] = new Color(0.82f, 0.16f, 0.16f),
        })
    {
        OnBuilt = _ => SniffClip ??= ModAssets.LoadWav("sniff.wav"),   // préchargé : pas de latence au premier sniff
        NeverDespawn = true,
        // Modèle à plat (y = épaisseur), pas debout comme la canette du soda : on annule la rotation de la canette et on
        // pose le dessous du sachet sur le pivot (sinon il s'enfonce à moitié dans le sol et flotte à côté de la main)
        ResetChildRotation = true,
        SitOnPivot = true,   // reste par terre jusqu'à ce que quelqu'un le sniffe ou le jette à la poubelle
    };

    internal static AudioClip SniffClip;

    internal static void PlaySniff(Vector3 at)
    {
        if (SniffClip == null) SniffClip = ModAssets.LoadWav("sniff.wav");
        ModAudio.Play(SniffClip, at);
    }

    internal static bool RequireToilets = true;
    const float ToiletRadius = 4f;

    internal static string Spawn(Vector3 pos) => Item.Spawn(pos) != null ? "Sachet posé" : "Impossible (hôte uniquement, voir log)";
    internal static void GiveTo(NetworkIdentity player) => Item.GiveTo(player);

    // Diagnostic : tout ce qui concerne l'objet interactif visé par la caméra
    internal static string InspectAimed()
    {
        Plugin.L.LogInfo($"[INSPECT] langue du jeu = '{LocalizationManager.instance?.language}'");
        var cam = Camera.main;
        if (cam == null || !Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 6f)) return "Rien de visé";
        var it = hit.collider.GetComponentInParent<Interactable>();
        if (it == null) return $"Visé : {hit.collider.name} (pas interactif)";
        var col = it.TryCast<Collectible>();
        Plugin.L.LogInfo($"[INSPECT] {it.gameObject.name} type={it.GetIl2CppType().Name} name='{it.interactableName}' label='{it.label}' " +
                         $"canInteract={it.canInteract} owner={(it.lobbyPlayerOwner == null ? "-" : it.lobbyPlayerOwner.name)} layer={it.gameObject.layer} " +
                         $"active={it.isActiveAndEnabled} netId={it.netId} stationary={col?.isStationary} block={col?.blockCollection} holder={(col?.currentHolder == null ? "-" : "oui")}");
        var alts = it.alternatives;
        Plugin.L.LogInfo($"[INSPECT]   alternatives={(alts == null ? "null" : alts.Count.ToString())} courantes={(it._currentAlternatives == null ? "null" : it._currentAlternatives.Count.ToString())}");
        if (alts != null)
            foreach (var a in alts)
                Plugin.L.LogInfo($"[INSPECT]   - '{a.text}' perm={a.interactionPermission} requireUsable={a.requireUsable} duringUse={a.duringUse} show={(a.showAlternative == null ? "-" : "cond")}");
        return $"Inspecté : {it.interactableName} (voir log)";
    }

    internal static bool LocalPlayerNearToilet()
    {
        var cam = Camera.main;
        if (cam == null) return false;
        foreach (var t in Object.FindObjectsOfType<Props.WC.Toilet.Toilet>())
            if (Vector3.Distance(cam.transform.position, t.transform.position) < ToiletRadius) return true;
        return false;
    }

    internal static string SpawnInToilets()
    {
        var toilets = Object.FindObjectsOfType<Props.WC.Toilet.Toilet>();
        foreach (var t in toilets) Item.Spawn(t.transform.position + Vector3.up * 1.0f);
        return $"{toilets.Length} sachet(s) posé(s) dans les toilettes";
    }

    internal static string ListConsumables()
    {
        int n = 0;
        foreach (var p in NetworkManager.singleton.spawnPrefabs)
        {
            var c = p == null ? null : p.GetComponent<Consumable>();
            if (c == null) continue;
            Plugin.L.LogInfo($"[COKE] consommable: prefab='{p.name}' nom='{c.interactableName}'");
            n++;
        }
        return $"{n} consommables listés dans le log";
    }
}

static class CokeBagPatches
{
    // Le texte de l'action "Consume" devient "Sniffer" pour notre sachet (dernière alternative ajoutée)
    [HarmonyPatch(typeof(Consumable), nameof(Consumable.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void RenameAction(Consumable __instance)
    {
        if (__instance.interactableName != CokeBag.Name || __instance.alternatives == null || __instance.alternatives.Count == 0) return;
        __instance.alternatives[__instance.alternatives.Count - 1].text = CustomTaskPatches.Text("Sniffer", "Sniff");
    }

    // Son de sniff chez tous les joueurs : le sachet est détruit (sur chaque client) alors qu'il est encore en main.
    // Un sachet jeté ou disparu au sol n'est plus tenu, donc ne fait pas de bruit.
    [HarmonyPatch(typeof(Collectible), nameof(Collectible.OnDestroy))]
    [HarmonyPrefix]
    static void SniffOnConsume(Collectible __instance)
    {
        if (__instance.interactableName != CokeBag.Name || !NetworkClient.active) return;
        bool held = __instance.currentHolder != null || __instance.usingPlayerController != null;
        Plugin.L.LogInfo($"[COKE] sachet détruit, tenu={held}");
        if (!held) return;
        try { CokeBag.PlaySniff(__instance.transform.position); }
        catch (Exception e) { Plugin.L.LogError($"[COKE] son de sniff : {e}"); }
    }

    // Effet "speed" du café (PlayerEffects.SpeedBoost = 1), appliqué par le serveur au joueur qui sniffe.
    // On reprend la durée du café dès qu'on l'a vue passer ; avant ça, valeur par défaut.
    const int SpeedBoost = 1;
    static float speedDuration = 30f;
    static bool applyingOurs;

    [HarmonyPatch(typeof(PlayerEffectController), nameof(PlayerEffectController.ServerSetEffect))]
    [HarmonyPrefix]
    static void LearnCoffeeDuration(int effect, float duration)
    {
        if (applyingOurs || effect != SpeedBoost || duration <= 0f || duration == speedDuration) return;
        speedDuration = duration;
        Plugin.L.LogInfo($"[COKE] durée du speed du café : {duration}s");
    }

    // Serveur : effet pour quiconque sniffe, avec ou sans la tâche. Consume ne tourne que chez le joueur ;
    // c'est sa commande CmdConsume qui arrive au serveur (aussi pour l'hôte).
    [HarmonyPatch(typeof(Consumable), nameof(Consumable.InvokeUserCode_CmdConsume__NetworkIdentity__NetworkConnectionToClient))]
    [HarmonyPrefix]
    static void SpeedOnConsume(NetworkBehaviour obj, NetworkConnectionToClient senderConnection)
    {
        var c = obj?.TryCast<Consumable>();
        var player = senderConnection?.identity;
        if (c == null || c.interactableName != CokeBag.Name || player == null) return;
        Plugin.L.LogInfo($"[COKE] sniffé par {player.name}");
        ApplySpeed(player);
    }

    // Côté serveur : même speed que le café
    internal static void ApplySpeed(NetworkIdentity player)
    {
        var fx = player?.GetComponent<PlayerEffectController>();
        if (fx == null) { Plugin.L.LogWarning("[SPEED] PlayerEffectController introuvable sur le joueur"); return; }
        applyingOurs = true;
        try { fx.ServerSetEffect(SpeedBoost, speedDuration); }
        finally { applyingOurs = false; }
        Plugin.L.LogInfo($"[SPEED] speed appliqué pour {speedDuration}s");
    }
}
