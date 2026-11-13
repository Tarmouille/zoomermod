using System.Collections.Generic;
using Room;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Joue nos sons comme ceux du jeu : son 3D branché sur le groupe du mixeur d'une source audio du jeu
// (AudioSource.PlayClipAtPoint contourne le mixeur : plein volume, sans les réglages de volume du joueur).
// Entre pièces, comme les sons du jeu (Audio.RoomEffectController, posé sur chaque objet sonore : toilettes, chats…) :
// quand la caméra n'est pas dans la pièce de la source, volume réduit et son étouffé (passe-bas), avec les réglages
// (minVolume, minLowPassFilter) lus sur un contrôleur du jeu. Pièce de la caméra : celle que le jeu détecte, ou la nôtre.
static class ModAudio
{
    static AudioSource reference;
    static readonly List<(AudioSource src, AudioLowPassFilter lpf, RoomController room, float volume)> playing = new();
    static AudioListener listener;
    static Audio.RoomEffectController model;
    static bool dumped;
    const float FadePerSecond = 4f, OpenCutoff = 22000f;
    const float MinDistance = 1.5f, MaxDistance = 15f;

    internal static void Play(AudioClip clip, Vector3 at, float volume = 1f, AudioSource like = null)
    {
        if (clip == null) return;
        var src = Create(clip, at, volume, like);
        src.Play();
        Object.Destroy(src.gameObject, clip.length + 0.2f);
    }

    // Son en boucle jusqu'à Stop (alarme)
    internal static AudioSource PlayLoop(AudioClip clip, Vector3 at, AudioSource like = null, float volume = 1f)
    {
        if (clip == null) return null;
        var src = Create(clip, at, volume, like);
        src.loop = true;
        src.Play();
        return src;
    }

    internal static void Stop(AudioSource src)
    {
        if (src != null) Object.Destroy(src.gameObject);
    }

    static AudioSource Create(AudioClip clip, Vector3 at, float volume, AudioSource like)
    {
        var src0 = like != null && like.outputAudioMixerGroup != null ? like : Reference();
        var go = new GameObject("DaleMod_Sound");
        go.transform.position = at;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.spatialBlend = 1f;
        if (src0 != null) src.outputAudioMixerGroup = src0.outputAudioMixerGroup;
        // Portée fixe : la source du jeu prise comme modèle pouvait porter très loin (ou en logarithmique, qui ne
        // tombe jamais à zéro) et nos sons s'entendaient dans tout le bureau. Plein volume jusqu'à MinDistance, puis
        // baisse linéaire jusqu'au silence à MaxDistance.
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = MinDistance;
        src.maxDistance = MaxDistance;
        src.dopplerLevel = 0f;
        var lpf = go.AddComponent<AudioLowPassFilter>();
        var room = Rooms.At(at + Vector3.up * 0.5f) ?? Rooms.At(at);
        playing.Add((src, lpf, room, volume));
        Apply(src, lpf, room, volume, 1f);   // réglé d'emblée, sans fondu
        return src;
    }

    internal static void Tick()
    {
        if (playing.Count == 0) return;
        float t = Mathf.Clamp01(FadePerSecond * Time.deltaTime);
        for (int i = playing.Count - 1; i >= 0; i--)
        {
            var (src, lpf, room, volume) = playing[i];
            if (src == null) { playing.RemoveAt(i); continue; }
            Apply(src, lpf, room, volume, t);
        }
    }

    static void Apply(AudioSource src, AudioLowPassFilter lpf, RoomController room, float volume, float t)
    {
        var m = Model();
        float minVol = m != null ? m.minVolume : 0.3f, minCut = m != null ? m.minLowPassFilter : 1000f;
        // Pièce de la caméra selon le jeu OU selon nos volumes : le détecteur du jeu peut rester bloqué (ascenseur de New York)
        bool same = (m?.mainCamera != null && SameRoom(room, m.mainCamera.currentRoom)) || SameRoom(room, OurListenerRoom());
        src.volume = Mathf.Lerp(src.volume, same ? volume : volume * minVol, t);
        if (lpf != null) lpf.cutoffFrequency = Mathf.Lerp(lpf.cutoffFrequency, same ? OpenCutoff : minCut, t);
    }

    static bool SameRoom(RoomController a, RoomController b) =>
        a == b || (a != null && b != null && !string.IsNullOrEmpty(a.roomName) && a.roomName == b.roomName);

    static RoomController OurListenerRoom()
    {
        if (listener == null || !listener.isActiveAndEnabled) listener = Object.FindObjectOfType<AudioListener>();
        if (listener == null) return null;
        var p = listener.transform.position;
        return Rooms.At(p) ?? Rooms.At(p + Vector3.down * 1f);
    }

    // Un contrôleur du jeu (réglages d'étouffement, détecteur de pièce de la caméra)
    static Audio.RoomEffectController Model()
    {
        if (model != null && model.mainCamera != null) return model;
        var all = Object.FindObjectsOfType<Audio.RoomEffectController>();
        foreach (var c in all) if (c.mainCamera != null) { model = c; break; }
        if (!dumped && all.Length > 0)
        {
            dumped = true;
            foreach (var c in all)
                Plugin.L.LogInfo($"[AUDIO] étouffement du jeu sur '{c.name}' : volume min {c.minVolume}, passe-bas min {c.minLowPassFilter}, épaisseur {c.thickness}, pièce '{c.roomTrigger?.currentRoom?.roomName}', caméra '{c.mainCamera?.currentRoom?.roomName}'");
        }
        return model;
    }

    // Une source du jeu déjà branchée sur le mixeur (effets sonores)
    static AudioSource Reference()
    {
        if (reference != null) return reference;
        foreach (var s in Object.FindObjectsOfType<AudioSource>())
            if (s.outputAudioMixerGroup != null && s.spatialBlend > 0.5f) { reference = s; break; }
        Plugin.L.LogInfo($"[AUDIO] groupe du mixeur : {(reference == null ? "aucun trouvé" : reference.outputAudioMixerGroup.name + " (via " + reference.name + ")")}");
        return reference;
    }
}
