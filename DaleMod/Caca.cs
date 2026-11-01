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

// Tâche "Recyclage" : "Manger son caca" aux toilettes (5 s) -> effet "Nauséeux".
// Le son de dégustation est diffusé à tous les joueurs par NetSignal, avec des giclées marron autour des toilettes ;
// le mangeur reçoit en plus des éclaboussures en bas de son écran (ScreenSplat).
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
        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Manger son caca", "Eat your poop"), InteractionPermission.All,
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
            // Giclées marron autour des toilettes (chez tous), effacées au bout de 30 s
            try
            {
                var (top, floor) = Blood.Bounds(it);
                var at = Vector3.Lerp(new Vector3(top.x, floor, top.z), top, 0.6f);
                Blood.SplashAt(at, floor, 30, 2, Blood.Poop, 30f);
            }
            catch (Exception e) { Plugin.L.LogError("[CACA] taches : " + e); }
            Plugin.L.LogInfo("[CACA] son joué");
        });
        // Chez chaque client : le joueur qui a la tâche et qui est à ces toilettes valide son étape
        NetSignal.OnLocalPlayer(EatenSignal, () =>
        {
            if (TaskActive()) TaskHook.TriggerTaskHookCommandStatic(Hook(AteAction));
            ScreenSplat.Burst(Blood.Poop, 8);   // éclaboussures en bas de l'écran du mangeur
        });
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
