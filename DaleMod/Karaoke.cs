using System;
using HarmonyLib;
using Mirror;
using Player;
using Player.Lobby;
using Player.Tasks;
using Props.Stereo;
using Room;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Karaoké en salle de pause, avec la chaîne du jeu et un CD ajouté : "Poker Face" de Lady Gaga (album The Fame).
// - La chanson est ajoutée à la liste de la chaîne (StereoController.songs) chez tous les joueurs, au même rang ;
//   pochette Assets\karaoke\thefame.jpg, son Assets\karaoke\pokerface.wav (converti du .mp3 en 22 kHz par tools/convert_audio.py).
// - Son CD apparaît avec les autres au chargement de la carte (présentoir CDHolder) ; sinon le serveur en ajoute un.
// Tâche "Karaoké" (rouge, Slacker ; voir TaskPools) : 1) mettre le CD de Lady Gaga dans la chaîne de la salle de pause ;
// 2) chanter 15 s dans la salle de pause pendant la chanson, sans que le manager entre dans la pièce (sinon il coupe la
// musique chez tous et il faut relancer le CD).
static class Karaoke
{
    internal const string Key = "dalemod.karaoke";
    internal const string StartedAction = "Played", SangAction = "Sang", KaraokeObj = "Karaoke";
    const string SongName = "Lady Gaga - Poker Face", SongFile = "karaoke/pokerface.wav", CoverFile = "karaoke/thefame.jpg";
    const float Needed = 15f, Poll = 0.25f, StopUp = -89.1f;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    internal static void Register()
    {
        // Le joueur qui a la tâche voit le manager arriver : le serveur coupe la chaîne chez tous
        NetSignal.OnServer(StopUp, (it, _) =>
        {
            var stereo = it.TryCast<StereoController>();
            if (stereo != null) { stereo.RpcStop(); Plugin.L.LogInfo("[KARAOKE] musique coupée (le manager arrive)"); }
        });
    }

    // ===================== La chanson et son CD =====================
    static int gagaIndex = -1;
    static AudioClip gagaClip;

    // Ajoutée une seule fois, à la fin de la liste : même rang chez tous les joueurs (même mod)
    static void EnsureSong()
    {
        var stereo = StereoController.instance ?? Object.FindObjectOfType<StereoController>();
        var songs = stereo?.songs;
        if (songs == null) return;
        for (int i = 0; i < songs.Count; i++)
            if (songs[i]?.songName == SongName) { gagaIndex = i; return; }
        var song = new Song { songName = SongName };
        // WAV (le jeu n'embarque pas le décodage MP3 : DownloadHandlerAudioClip retiré). Une erreur de chargement
        // n'empêche pas d'ajouter la chanson (sinon le CD n'existerait pas et les rangs seraient décalés)
        try { gagaClip ??= ModAssets.LoadWav(SongFile); song.songAudioClip = gagaClip; }
        catch (Exception e) { Plugin.L.LogError("[KARAOKE] son : " + e.Message); }
        try { var cover = ModAssets.LoadTexture(CoverFile); if (cover != null) { cover.hideFlags = HideFlags.DontUnloadUnusedAsset; song.songCover = cover; } }
        catch (Exception e) { Plugin.L.LogError("[KARAOKE] pochette : " + e.Message); }
        songs.Add(song);
        gagaIndex = songs.Count - 1;
        Plugin.L.LogInfo($"[KARAOKE] CD '{SongName}' ajouté à la chaîne (n° {gagaIndex})");
    }

    [HarmonyPatch(typeof(StereoController), nameof(StereoController.BeforeAwake))]
    [HarmonyPostfix]
    static void AfterStereoAwake() => EnsureSong();

    // Serveur : la chanson existe avant que le présentoir crée ses CD ; s'il n'en crée pas pour elle, on en ajoute un.
    // Le présentoir crée ses CD dans une coroutine (InitCoroutine) : on ne vérifie que quelques secondes plus tard (Tick)
    [HarmonyPatch(typeof(CDHolder), nameof(CDHolder.OnReadyForObjectInstantiation))]
    [HarmonyPrefix]
    static void BeforeCDs() => EnsureSong();

    [HarmonyPatch(typeof(CDHolder), nameof(CDHolder.OnReadyForObjectInstantiation))]
    [HarmonyPostfix]
    static void AfterCDs(CDHolder __instance)
    {
        if (!NetworkServer.active) return;
        holder = __instance;
        checkCdAt = Time.time + 8f;
    }

    static CDHolder holder;
    static float checkCdAt = -1f;

    static void CheckCd()
    {
        if (checkCdAt < 0f || Time.time < checkCdAt) return;
        checkCdAt = -1f;
        if (!NetworkServer.active || gagaIndex < 0 || holder == null || holder.cdCasePrefab == null) return;
        foreach (var cd in Object.FindObjectsOfType<CDCase>()) if (cd.songIndex == gagaIndex) return;
        var go = Object.Instantiate(holder.cdCasePrefab, holder.transform.position + Vector3.up * 0.25f, holder.transform.rotation);
        var cdCase = go.GetComponent<CDCase>();
        if (cdCase != null) cdCase.NetworksongIndex = gagaIndex;
        NetworkServer.Spawn(go);
        Plugin.L.LogInfo("[KARAOKE] CD de Lady Gaga posé près du présentoir");
    }

    // Ce que joue la chaîne (chez tous) : RpcPlayCD(n° de chanson) / RpcStop (corps des RPC, appelés chez chaque client)
    static int playing = -1;

    [HarmonyPatch(typeof(StereoController), nameof(StereoController.UserCode_RpcPlayCD__Int32))]
    [HarmonyPrefix]
    static void OnPlay(int cdIndex)
    {
        playing = cdIndex;
        Plugin.L.LogInfo($"[KARAOKE] la chaîne joue le CD n° {cdIndex}{(cdIndex == gagaIndex ? " (Poker Face)" : "")}");
    }

    [HarmonyPatch(typeof(StereoController), nameof(StereoController.UserCode_RpcStop))]
    [HarmonyPrefix]
    static void OnStop() => playing = -1;

    // ===================== La tâche (chez le joueur qui l'a) =====================
    static IntPtr taskPtr;
    static bool started, done;
    static float progress, nextPoll;

    internal static void Tick()
    {
        if (gagaIndex < 0) EnsureSong();   // filet de sécurité (chaîne créée sans passer par BeforeAwake)
        CheckCd();
        var tp = CustomTaskPatches.ActiveTask()?.Pointer ?? IntPtr.Zero;
        if (tp != taskPtr) { taskPtr = tp; started = done = false; progress = 0f; }
        if (!TaskActive() || done || Time.time < nextPoll) return;
        nextPoll = Time.time + Poll;

        var stereo = StereoController.instance ?? Object.FindObjectOfType<StereoController>();
        var me = NetworkClient.localPlayer?.GetComponent<PlayerController>();
        if (stereo == null || me == null) return;
        var room = BreakRoom(stereo);
        bool inRoom = room != null ? RoomOf(me) == room : Vector3.Distance(me.transform.position, stereo.transform.position) < 5f;
        bool gaga = (playing == gagaIndex && gagaIndex >= 0) || GagaPlaying(stereo);

        if (gaga && inRoom && !started)
        {
            started = true;
            TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, KaraokeObj, null, StartedAction, null, null));
            Plugin.L.LogInfo("[KARAOKE] CD de Lady Gaga lancé");
        }
        if (!gaga || !inRoom) { progress = 0f; return; }

        // Le manager entre dans la salle de pause : musique coupée chez tous, il faut relancer
        foreach (var pc in Object.FindObjectsOfType<PlayerController>())
        {
            if (pc.Pointer == me.Pointer || pc.lobbyPlayer?.playerRole != PlayerRole.Manager) continue;
            bool managerIn = room != null ? RoomOf(pc) == room : Vector3.Distance(pc.transform.position, stereo.transform.position) < 5f;
            if (!managerIn) continue;
            progress = 0f;
            if (NetworkServer.active) stereo.RpcStop(); else NetSignal.SendToServer(stereo, StopUp);
            Hud.Show(CustomTaskPatches.Text("Le manager débarque ! Musique coupée, relance le CD.", "The manager walks in! Music stopped, play the CD again."), 3f);
            return;
        }

        progress += Poll;
        Hud.Show(CustomTaskPatches.Text($"Karaoké : {Mathf.Min(progress, Needed):0}/{Needed:0} s", $"Karaoke: {Mathf.Min(progress, Needed):0}/{Needed:0} s"), 0.6f);
        if (progress < Needed) return;
        done = true;
        TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, KaraokeObj, null, SangAction, null, null));
        Hud.Show(CustomTaskPatches.Text("Quelle performance ! La salle de pause s'en souviendra.", "What a performance! The break room will remember it."), 3f);
        Plugin.L.LogInfo("[KARAOKE] karaoké tenu 15 s");
    }

    // Ce que la chaîne joue réellement : sa source audio (les RPC ne passent pas toujours par nos patchs)
    static string lastHeard;
    static bool GagaPlaying(StereoController stereo)
    {
        bool gaga = false;
        string heard = null;
        // Sources de la chaîne, sinon toutes celles de la scène (le son peut être joué ailleurs)
        var sources = stereo.GetComponentsInChildren<AudioSource>(true);
        bool any = false;
        foreach (var src in sources) if (src != null && src.isPlaying) { any = true; break; }
        if (!any) sources = Object.FindObjectsOfType<AudioSource>();
        foreach (var src in sources)
        {
            if (src == null || !src.isPlaying || src.clip == null) continue;
            heard = src.clip.name;
            if ((gagaClip != null && src.clip.Pointer == gagaClip.Pointer) || src.clip.name.Contains("pokerface")) gaga = true;
        }
        if (heard != lastHeard) { lastHeard = heard; Plugin.L.LogInfo($"[KARAOKE] la chaîne joue : {heard ?? "rien"}{(gaga ? " (Poker Face)" : "")}"); }
        return gaga;
    }

    static RoomController RoomOf(PlayerController pc) => Rooms.Of(pc);   // pièce d'après la position (Rooms)

    static RoomController breakRoom;
    static bool logged;
    static RoomController BreakRoom(StereoController stereo)
    {
        if (breakRoom != null) return breakRoom;
        breakRoom = Rooms.Of(stereo);
        if (!logged) { logged = true; Plugin.L.LogInfo($"[KARAOKE] salle de pause : {(breakRoom == null ? "introuvable (repli 5 m)" : $"'{breakRoom.roomName}'")}"); }
        return breakRoom;
    }
}
