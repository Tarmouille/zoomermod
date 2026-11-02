using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Player.PlayerEffects;
using Player.Tasks;
using Props.Scripts;
using Props.WC.Toilet;
using UnityEngine;
using UnityEngine.Events;

namespace DaleMod;

// Tâche "Recyclage" : "Manger du caca" aux toilettes (5 s) -> effet "Nauséeux".
// Le son de dégustation est diffusé à tous les joueurs par NetSignal, avec des giclées marron autour des toilettes en continu
// pendant les 5 s ; le mangeur reçoit en plus des éclaboussures en bas de son écran (ScreenSplat), elles aussi en continu.
// L'action tourne sur le serveur (NetSignal.ServerAction) : effet appliqué au mangeur, étape validée chez lui seul.
static class Caca
{
    internal const string Key = "dalemod.recyclage";
    internal const string AteAction = "Ate", ToiletObj = "DaleToilet";
    const float EatDuration = 5f;
    const float SoundSignal = -13.7f, EatenSignal = -13.8f, EatAction = -13.5f, StartAction = -13.6f;
    static AudioClip eatClip;
    static bool soundSent;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    internal static TaskHook Hook(string action) => new(null, ToiletObj, null, action, null, null);

    [HarmonyPatch(typeof(Toilet), nameof(Toilet.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddEat(Toilet __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var toilet = __instance;
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Manger du caca", "Eat poop"), InteractionPermission.All,
            NetSignal.ServerAction(toilet, EatAction, player => Eat(player)),
            false,
            DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(new Func<bool>(TaskActive)),
            EatDuration,
            DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { })),
            DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => StartEat(toilet))),
            false, false));
    }

    // Début de l'action (5 s, le son dure ~5 s) : le serveur demande à tous les clients de jouer le son
    static void StartEat(Toilet toilet)
    {
        Plugin.L.LogInfo($"[CACA] début de l'action (serveur={NetworkServer.active})");
        screenUntil = Time.time + EatDuration;   // éclaboussures d'écran en continu chez le mangeur (Tick)
        nextScreen = 0f;
        if (!NetworkServer.active) { NetSignal.SendToServer(toilet, StartAction); return; }   // client : le serveur lance le son
        NetSignal.Send(toilet, SoundSignal);
        soundSent = true;
    }

    // Chez chaque client : le signal joue le son à la position des toilettes
    internal static void Register()
    {
        NetSignal.OnServer(StartAction, (it, _) => { NetSignal.Send(it, SoundSignal); soundSent = true; });
        NetSignal.On(SoundSignal, it =>
        {
            if (eatClip == null) eatClip = ModAssets.LoadWav("cookiemonster.wav");
            ModAudio.Play(eatClip, it.transform.position);
            // Giclées marron autour des toilettes (chez tous), en continu pendant la dégustation, effacées au bout de 30 s
            Splash(it, 30, 2);
            splashing.Add((it.transform, Time.time + EatDuration));
            Plugin.L.LogInfo("[CACA] son joué");
        });
        // Chez chaque client : le joueur qui a la tâche et qui est à ces toilettes valide son étape
        NetSignal.OnLocalPlayer(EatenSignal, () =>
        {
            if (TaskActive()) TaskHook.TriggerTaskHookCommandStatic(Hook(AteAction));
            ScreenSplat.Burst(Blood.Poop, 8);   // éclaboussures en bas de l'écran du mangeur
        });
    }

    static void Splash(Component toilet, int drops, int big)
    {
        try
        {
            var (top, floor) = Blood.Bounds(toilet);
            var at = Vector3.Lerp(new Vector3(top.x, floor, top.z), top, 0.6f);
            Blood.SplashAt(at, floor, drops, big, Blood.Poop, 30f);
        }
        catch (Exception e) { Plugin.L.LogError("[CACA] taches : " + e); }
    }

    // Giclées en cours (toilettes, fin) et éclaboussures d'écran du mangeur local
    static readonly System.Collections.Generic.List<(Transform toilet, float until)> splashing = new();
    static float nextSplash, screenUntil = -1f, nextScreen;
    const float SplashEvery = 0.45f, ScreenEvery = 0.7f;

    internal static void Tick()
    {
        if (splashing.Count > 0 && Time.time >= nextSplash)
        {
            nextSplash = Time.time + SplashEvery;
            for (int i = splashing.Count - 1; i >= 0; i--)
            {
                var (t, until) = splashing[i];
                if (t == null || Time.time >= until) { splashing.RemoveAt(i); continue; }
                Splash(t, 10, UnityEngine.Random.value < 0.25f ? 1 : 0);
            }
        }
        if (screenUntil > 0f && Time.time >= nextScreen)
        {
            if (Time.time >= screenUntil) { screenUntil = -1f; return; }
            nextScreen = Time.time + ScreenEvery;
            ScreenSplat.Burst(Blood.Poop, 2);
        }
    }

    static void Eat(NetworkIdentity player)
    {
        // Repli si le début d'action n'est pas passé par le serveur : son à la fin plutôt que jamais
        if (!soundSent && NetworkServer.active)
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Toilet>())
                if (Vector3.Distance(t.transform.position, player.transform.position) < 3f) { NetSignal.Send(t, SoundSignal); break; }
        soundSent = false;

        if (!NetworkServer.active) return;
        var fx = player?.GetComponent<PlayerEffectController>();
        if (fx != null) Nausea.Apply(fx);
        NetSignal.SendToPlayer(player, EatenSignal);   // l'étape est validée chez le mangeur seul
        Plugin.L.LogInfo("[CACA] caca mangé");
    }
}
