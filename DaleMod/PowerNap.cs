using System;
using HarmonyLib;
using Mirror;
using Player;
using Player.Lobby;
using Player.Scripts;
using Player.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Power Nap" (rouge, Slacker ; voir TaskPools) : s'asseoir à son poste et y dormir 20 s.
// L'écran s'assombrit peu à peu ; 5 s avant la fin, un ronflement (snore1 ou snore2 au hasard, une fois par sieste)
// est entendu par tout le monde. Si le manager passe à moins de 3 m, c'est raté : la sieste recommence de zéro.
// Le ronflement part du client du dormeur : signal client -> serveur -> tous (NetSignal).
static class PowerNap
{
    internal const string Key = "dalemod.powerNap";
    internal const string NappedAction = "Napped", DeskObj = "Desk";
    // Un signal par son : le dormeur tire le son au hasard, tout le monde joue le même
    static readonly float[] SnoreUp = { -73.1f, -73.2f }, Snore = { -73.3f, -73.4f };
    static readonly string[] SnoreFiles = { "snore1.wav", "snore2.wav" };
    const float NapSeconds = 20f, SnoreBeforeEnd = 5f, ManagerNear = 3f, CaughtSeconds = 3f;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);
    static string MyName() => LobbyManager.instance?.GetLocalPlayer()?.username;

    static bool seated;          // assis à son propre poste (hooks du jeu)
    static bool napping, snored;
    static float progress, caughtUntil = -1f;
    static IntPtr doneFor;       // tâche déjà validée

    static readonly AudioClip[] snoreClips = new AudioClip[2];

    internal static void Register()
    {
        for (int i = 0; i < Snore.Length; i++)
        {
            int k = i;
            // Serveur : relaie le ronflement du dormeur à tout le monde
            NetSignal.OnServer(SnoreUp[k], (it, _) => NetSignal.Send(it, Snore[k]));
            NetSignal.On(Snore[k], it =>
            {
                snoreClips[k] ??= ModAssets.LoadWav(SnoreFiles[k]);
                ModAudio.Play(snoreClips[k], it.transform.position + Vector3.up);
            });
        }
    }

    // Assis / levé de son propre poste
    [HarmonyPatch(typeof(TaskHook), nameof(TaskHook.TriggerTaskHookCommandStatic))]
    [HarmonyPrefix]
    static void TrackDesk(TaskHook taskHook)
    {
        if (taskHook == null || taskHook.passiveObjectName != "WorkStation") return;
        if (taskHook.action == "Enter") seated = taskHook.passiveObjectOwner == MyName();
        else if (taskHook.action == "Stopped Using") seated = false;
    }

    internal static void Tick()
    {
        var task = CustomTaskPatches.ActiveTask();
        bool can = seated && TaskActive() && task.Pointer != doneFor && Time.time >= caughtUntil;
        if (!can) { if (napping) Stop(); return; }
        if (!napping) Start();

        if (ManagerWatching())
        {
            Stop();
            caughtUntil = Time.time + CaughtSeconds;
            Hud.Show(CustomTaskPatches.Text("Le manager t'a vu dormir ! Recommence.", "The manager caught you sleeping! Start over."), CaughtSeconds);
            Plugin.L.LogInfo("[NAP] le manager m'a vu");
            return;
        }
        progress += Time.deltaTime;
        Hud.Show(new string('z', 1 + (int)(progress / 4f)), 0.3f);   // zzz qui s'allonge (police du jeu)
        if (!snored && progress >= NapSeconds - SnoreBeforeEnd)
        {
            snored = true;
            var me = MyInteractable();
            if (me != null) NetSignal.SendToServer(me, SnoreUp[UnityEngine.Random.Range(0, SnoreUp.Length)]);
        }
        if (progress < NapSeconds) return;
        Stop();
        doneFor = task.Pointer;
        TaskHook.TriggerTaskHookCommandStatic(new TaskHook(null, DeskObj, null, NappedAction, null, null));
        Plugin.L.LogInfo("[NAP] sieste réussie");
    }

    static void Start()
    {
        napping = true;
        snored = false;
        progress = 0f;
    }

    static void Stop()
    {
        napping = false;
        progress = 0f;
    }

    static PlayerInteractable myPi;
    static PlayerInteractable MyInteractable()
    {
        var me = NetworkClient.localPlayer;
        if (myPi != null && myPi.netIdentity == me) return myPi;
        foreach (var p in Object.FindObjectsOfType<PlayerInteractable>())
            if (p.netIdentity == me) return myPi = p;
        return null;
    }

    static bool ManagerWatching()
    {
        var me = NetworkClient.localPlayer;
        if (me == null) return false;
        foreach (var pc in Object.FindObjectsOfType<PlayerController>())
            if (pc.netIdentity != me && pc.lobbyPlayer?.playerRole == PlayerRole.Manager
                && Vector3.Distance(pc.transform.position, me.transform.position) < ManagerNear)
                return true;
        return false;
    }

    // Écran qui s'assombrit pendant la sieste ; message si le manager l'interrompt
    static Texture2D black;
    internal static void DrawOverlay()
    {
        if (napping && progress > 0f)
        {
            black ??= Solid(Color.black);
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(progress / NapSeconds) * 0.9f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), black);
            GUI.color = prev;
        }
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontUnloadUnusedAsset };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
