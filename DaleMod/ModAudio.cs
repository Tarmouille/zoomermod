using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Joue nos sons comme ceux du jeu : son 3D branché sur le groupe du mixeur d'une source audio du jeu
// (AudioSource.PlayClipAtPoint contourne le mixeur : plein volume, sans les réglages de volume du joueur).
static class ModAudio
{
    static AudioSource reference;

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
        if (src0 != null)
        {
            src.outputAudioMixerGroup = src0.outputAudioMixerGroup;
            src.rolloffMode = src0.rolloffMode;
            src.minDistance = src0.minDistance;
            src.maxDistance = src0.maxDistance;
        }
        return src;
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
