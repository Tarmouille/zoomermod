using System.Collections.Generic;
using Props.ServerRack;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Pendant la "Panne générale" : les LED des baies serveur clignotent en rouge et l'alarme sonne, comme lors d'une vraie coupure,
// sans couper les connexions. Chaque ServerUnit a un emplacement de matériau qui alterne Green/NoLight
// (normal) ou Red/NoLight (coupure) : on impose Red/NoLight nous-mêmes, en LateUpdate pour passer après le jeu.
// Début et fin de la panne sont diffusés à tous les joueurs par NetSignal.
// Aussi en BLEU, sans alarme et pour une durée donnée : sauvegarde des serveurs (BlinkBlue). Le rouge reste prioritaire.
static class ServerBlink
{
    internal const float OnSignal = -21.1f, OffSignal = -21.2f;
    const float Period = 1f;                     // allumé une demi-période, éteint l'autre
    static readonly Color RedBase = new(0.840f, 0.227f, 0.091f), RedEmission = new(1.842f, 0.382f, 0.321f);
    static readonly Color BlueBase = new(0.12f, 0.42f, 0.95f), BlueEmission = new(0.35f, 0.9f, 2.4f);

    struct Slot { public Renderer R; public int Index; public bool Odd; }
    static readonly List<Slot> slots = new();
    static Material red, off, green, blue;
    static bool active;                          // rouge (panne)
    static float blueUntil = -1f;                // bleu (sauvegarde) jusqu'à cette heure

    internal static void Register()
    {
        NetSignal.On(OnSignal, _ => Begin());
        NetSignal.On(OffSignal, _ => End());
    }

    // Côté serveur
    internal static void Broadcast(bool on)
    {
        var any = Object.FindObjectOfType<ServerController>();
        NetSignal.Send(any, on ? OnSignal : OffSignal);
    }

    static bool IsLed(Material m) => m != null && (m.name.StartsWith("Green") || m.name.StartsWith("NoLight") || m.name.StartsWith("Red"));

    static void Begin()
    {
        blueUntil = -1f;
        CollectSlots();
        foreach (var rack in Object.FindObjectsOfType<ServerController>()) SetSound(rack, rack.alarmSound);
        var basis = green ?? off;
        if (red == null && basis != null)
        {
            red = new Material(basis) { name = "Red (DaleMod)", hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (red.HasProperty("_BaseColor")) red.SetColor("_BaseColor", RedBase);
            if (red.HasProperty("_EmissionColor")) red.SetColor("_EmissionColor", RedEmission);
            red.color = RedBase;
        }
        active = red != null && off != null;
        Plugin.L.LogInfo($"[SRV] clignotement rouge : {slots.Count} LED, rouge={(red == null ? "-" : red.name)} éteint={(off == null ? "-" : off.name)} -> {(active ? "actif" : "IMPOSSIBLE")}");
    }

    static void End()
    {
        active = false;
        if (green != null)
            foreach (var s in slots) Set(s, green);   // le clignotement vert du jeu reprend ensuite
        foreach (var rack in Object.FindObjectsOfType<ServerController>()) SetSound(rack, rack.normalSound);
        slots.Clear();
        Plugin.L.LogInfo("[SRV] clignotement rouge arrêté");
    }


    // Emplacements de matériau des LED de toutes les baies (Green / NoLight / Red), en alternance une unité sur deux
    static void CollectSlots()
    {
        slots.Clear();
        foreach (var rack in Object.FindObjectsOfType<ServerController>())
        {
            int unit = 0;
            foreach (var r in rack.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool found = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!IsLed(m)) continue;
                    found = true;
                    slots.Add(new Slot { R = r, Index = i, Odd = unit % 2 == 1 });
                    if (m.name.StartsWith("Green") && green == null) green = m;
                    if (m.name.StartsWith("NoLight") && off == null) off = m;
                    if (m.name.StartsWith("Red") && red == null) red = m;
                }
                if (found) unit++;
            }
        }
    }

    // Bleu pendant `seconds` (chez ce client) ; ignoré si la panne rouge est en cours
    internal static void BlinkBlue(float seconds)
    {
        if (active) return;
        CollectSlots();
        var basis = green ?? off;
        if (blue == null && basis != null)
        {
            blue = new Material(basis) { name = "Blue (DaleMod)", hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (blue.HasProperty("_BaseColor")) blue.SetColor("_BaseColor", BlueBase);
            if (blue.HasProperty("_EmissionColor")) blue.SetColor("_EmissionColor", BlueEmission);
            blue.color = BlueBase;
        }
        if (blue == null || off == null) return;
        blueUntil = Time.time + seconds;
    }

    internal static void StopBlue()
    {
        if (blueUntil < 0f) return;
        blueUntil = -1f;
        if (!active && green != null) foreach (var sl in slots) if (sl.R != null) Set(sl, green);
    }

    // Appelé par DebugMenu.LateUpdate
    internal static void LateTick()
    {
        if (!active && blueUntil > 0f && Time.time >= blueUntil) StopBlue();
        var on = active ? red : blueUntil > 0f ? blue : null;
        if (on == null) return;
        bool phase = Time.time % Period < Period / 2f;
        foreach (var s in slots)
            if (s.R != null) Set(s, phase ^ s.Odd ? on : off);
    }

    // Le haut-parleur de chaque baie : alarme en boucle pendant la panne, ronronnement normal ensuite
    static void SetSound(ServerController rack, AudioClip clip)
    {
        var src = rack.audioSource;
        if (src == null || clip == null) return;
        src.clip = clip;
        src.loop = true;
        src.Play();
    }

    static void Set(Slot s, Material m)
    {
        var mats = s.R.sharedMaterials;
        if (s.Index >= mats.Length || mats[s.Index] == m) return;
        mats[s.Index] = m;
        s.R.sharedMaterials = mats;
    }
}
