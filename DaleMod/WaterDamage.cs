using System;
using System.Collections.Generic;
using Computer.Scripts.System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Mirror;
using Objects.Scripts;
using Player.Lobby;
using Player.Tasks;
using Props.Scripts;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace DaleMod;

// Tâche "Dégât des eaux" (rouge, Slacker ; voir TaskPools) : remplir un gobelet à la fontaine, le renverser sur un PC (celui d'un collègue ou le sien).
// Le PC court-circuite : étincelles, fumée et grésillement pour tous, et son écran affiche une erreur (l'écran
// "pas de connexion" du jeu, texte remplacé). N'importe qui peut "Sécher le PC" (10 s) pour le réparer.
static class WaterDamage
{
    internal const string Key = "dalemod.degatDesEaux";
    internal const string SpilledAction = "Spilled", ComputerObj = "Computer";
    const float SpilledSignal = -72.1f, DamagedSignal = -72.2f, RepairedSignal = -72.3f, SpillAction = -72.5f, DryAction = -72.6f;
    const float SpillSeconds = 2f, DrySeconds = 10f;

    static bool TaskActive() => CustomTaskPatches.IsActive(CustomTaskPatches.ActiveTask(), Key);

    class Fx
    {
        public WorkStationController ws;
        public float nextPuff;
        public AudioSource crackle;
        public float nextBurst;
        public bool screenWasActive;
        public List<(TMP_Text t, string text)> texts = new();
    }

    static readonly Dictionary<IntPtr, Fx> damaged = new();   // tous les clients
    static AudioClip crackleClip;

    internal static void Register()
    {
        // L'étape revient au joueur qui a renversé l'eau (signal adressé à lui seul)
        NetSignal.OnLocalPlayer(SpilledSignal, () =>
        {
            if (!TaskActive()) return;
            TaskHook.TriggerTaskHookCommandStatic(WaterCup.Hook(SpilledAction, ComputerObj));
            Plugin.L.LogInfo("[WATERPC] PC aspergé");
        });
        NetSignal.On(DamagedSignal, it =>
        {
            var ws = it.TryCast<WorkStationController>();
            if (ws != null && !damaged.ContainsKey(ws.Pointer)) damaged[ws.Pointer] = StartFx(ws);
        });
        NetSignal.On(RepairedSignal, it =>
        {
            if (damaged.Remove(it.Pointer, out var fx)) StopFx(fx);
            Plugin.L.LogInfo("[WATERPC] PC séché");
        });
    }

    static string Owner(WorkStationController ws) => ws.computerController?.user?.username;

    // --- Actions sur les postes de travail ---
    [HarmonyPatch(typeof(WorkStationController), nameof(WorkStationController.SetUpInitialAlternatives))]
    [HarmonyPostfix]
    static void AddAlternatives(WorkStationController __instance)
    {
        var alts = __instance.alternatives;
        if (alts == null) return;
        var ws = __instance;

        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Renverser l'eau sur le PC", "Spill water on the computer"), InteractionPermission.All,
            NetSignal.ServerAction(ws, SpillAction, player => Spill(player, ws)), Il2CppType.Of<WaterCupController>(), false,
            Show(() => TaskActive() && !damaged.ContainsKey(ws.Pointer) && ws.computerController != null
                       && WaterCup.HasWater(NetworkClient.localPlayer)),   // n'importe quel PC, le sien compris
            SpillSeconds, false, NoStart(), false));

        alts.Add(new InteractionAlternative(CustomTaskPatches.Text("Sécher le PC", "Dry the computer"), InteractionPermission.All,
            NetSignal.ServerAction(ws, DryAction, _ => Dry(ws)), false, Show(() => damaged.ContainsKey(ws.Pointer)), DrySeconds, NoTimer(), NoStart(), false, false));
    }

    static readonly HashSet<IntPtr> serverDamaged = new();

    // Serveur
    static void Spill(NetworkIdentity player, WorkStationController ws)
    {
        if (!NetworkServer.active || serverDamaged.Contains(ws.Pointer)) return;
        var cup = WaterCup.Held(player);
        Plugin.L.LogInfo($"[WATERPC] {player?.name} renverse de l'eau sur le PC de {Owner(ws)}");
        if (cup == null || cup.waterAmount <= 0.01f) return;
        WaterCup.Empty(cup);
        serverDamaged.Add(ws.Pointer);
        NetSignal.SendToPlayer(player, SpilledSignal);
        NetSignal.Send(ws, DamagedSignal);
    }

    static void Dry(WorkStationController ws)
    {
        if (!NetworkServer.active || !serverDamaged.Remove(ws.Pointer)) return;
        NetSignal.Send(ws, RepairedSignal);
    }

    // --- Effets (chez tous les clients) ---
    static Vector3 ScreenPos(WorkStationController ws) =>
        ws.computerController != null ? ws.computerController.transform.position : ws.transform.position + Vector3.up;

    static Fx StartFx(WorkStationController ws)
    {
        var fx = new Fx { ws = ws };
        var at = ScreenPos(ws);
        crackleClip ??= ModAssets.LoadWav("crackle.wav");
        fx.crackle = ModAudio.PlayLoop(crackleClip, at, null, 0.7f);

        // Écran : celui de "pas de connexion" du jeu, avec notre texte
        var screen = ws.computerController?.noConnectionScreen;
        if (screen != null)
        {
            fx.screenWasActive = screen.activeSelf;
            var texts = screen.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                fx.texts.Add((texts[i], texts[i].text));
                texts[i].text = i == 0
                    ? CustomTaskPatches.Text("ERREUR FATALE : liquide détecté dans l'unité centrale", "FATAL ERROR: liquid detected in the computer")
                    : CustomTaskPatches.Text("Faites sécher le PC avant de l'utiliser.", "Dry the computer before using it.");
            }
            screen.SetActive(true);
        }
        Plugin.L.LogInfo($"[WATERPC] PC de {Owner(ws)} en court-circuit (écran {(screen == null ? "introuvable" : "remplacé")})");
        return fx;
    }

    // Fumée : bouffées grises communes (Puffs), juste au-dessus du PC
    const float PuffEvery = 0.2f;
    static readonly Color SmokeGrey = new(0.58f, 0.58f, 0.6f);

    static void EmitPuff(Vector3 at) =>
        Puffs.Emit(at + new Vector3(UnityEngine.Random.Range(-0.05f, 0.05f), 0.1f, UnityEngine.Random.Range(-0.05f, 0.05f)),
                   SmokeGrey, UnityEngine.Random.Range(0.25f, 0.4f), rise: 0.55f, life: 2.4f, alpha: 0.5f, drift: 0.1f);


    static void StopFx(Fx fx)
    {
        ModAudio.Stop(fx.crackle);
        foreach (var (t, text) in fx.texts) if (t != null) t.text = text;
        var screen = fx.ws?.computerController?.noConnectionScreen;
        if (screen != null) screen.SetActive(fx.screenWasActive);
    }

    // Étincelles "maison" : petits cubes jaunes animés à la main (le système de particules est trop amputé dans l'interop :
    // pas de modules main/emission/shape ni de champs EmitParams).
    static readonly List<(Transform t, Vector3 v, float die)> sparks = new();
    const float Gravity = 6f;

    static void Burst(Vector3 at, int n)
    {
        var mat = SparkMaterial();
        for (int i = 0; i < n; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "DaleMod_Spark";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = at + UnityEngine.Random.insideUnitSphere * 0.05f;
            go.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.008f, 0.018f);
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            var dir = UnityEngine.Random.insideUnitSphere;
            dir.y = Mathf.Abs(dir.y) + 0.3f;
            sparks.Add((go.transform, dir.normalized * UnityEngine.Random.Range(0.8f, 2.2f), Time.time + UnityEngine.Random.Range(0.15f, 0.4f)));
        }
    }

    static void UpdateSparks()
    {
        float dt = Time.deltaTime;
        for (int i = sparks.Count - 1; i >= 0; i--)
        {
            var (t, v, die) = sparks[i];
            if (t == null || Time.time >= die) { if (t != null) Object.Destroy(t.gameObject); sparks.RemoveAt(i); continue; }
            v.y -= Gravity * dt;
            t.position += v * dt;
            sparks[i] = (t, v, die);
        }
    }

    static Material sparkMat;
    static Material SparkMaterial()
    {
        if (sparkMat != null) return sparkMat;
        foreach (var name in new[] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default" })
        {
            var sh = Shader.Find(name);
            if (sh == null) continue;
            sparkMat = new Material(sh) { hideFlags = HideFlags.DontUnloadUnusedAsset, color = new Color(1f, 0.85f, 0.3f) };
            if (sparkMat.HasProperty("_BaseColor")) sparkMat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.3f));   // URP
            Plugin.L.LogInfo("[WATERPC] shader des étincelles : " + name);
            return sparkMat;
        }
        return sparkMat;
    }

    internal static void Tick()
    {
        UpdateSparks();
        foreach (var fx in damaged.Values)
        {
            if (Time.time >= fx.nextPuff) { EmitPuff(ScreenPos(fx.ws)); fx.nextPuff = Time.time + PuffEvery; }
            if (Time.time < fx.nextBurst) continue;
            Burst(ScreenPos(fx.ws), UnityEngine.Random.Range(6, 16));
            fx.nextBurst = Time.time + UnityEngine.Random.Range(0.15f, 1.2f);
        }
    }

    // Le jeu peut réafficher/masquer son écran "pas de connexion" : on le maintient pendant la panne
    internal static void LateTick()
    {
        foreach (var fx in damaged.Values)
        {
            var screen = fx.ws?.computerController?.noConnectionScreen;
            if (screen != null && !screen.activeSelf) screen.SetActive(true);
        }
    }

    // Debug (hôte) : court-circuiter le poste le plus proche
    internal static string TestNearest()
    {
        if (!NetworkServer.active) return "Hôte uniquement";
        var me = NetworkClient.localPlayer;
        if (me == null) return "Pas de joueur";
        WorkStationController best = null;
        float bestD = float.MaxValue;
        foreach (var ws in Object.FindObjectsOfType<WorkStationController>())
        {
            float d = Vector3.Distance(ws.transform.position, me.transform.position);
            if (d < bestD) { bestD = d; best = ws; }
        }
        if (best == null) return "Aucun poste";
        if (serverDamaged.Contains(best.Pointer)) { Dry(best); return "Poste séché"; }
        serverDamaged.Add(best.Pointer);
        NetSignal.Send(best, DamagedSignal);
        return "Poste le plus proche en court-circuit";
    }

    static Il2CppSystem.Func<bool> Show(Func<bool> f) => DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(f);
    static UnityAction<float> NoTimer() => DelegateSupport.ConvertDelegate<UnityAction<float>>(new Action<float>(_ => { }));
    static UnityAction<NetworkIdentity> NoStart() => DelegateSupport.ConvertDelegate<UnityAction<NetworkIdentity>>(new Action<NetworkIdentity>(_ => { }));
}
