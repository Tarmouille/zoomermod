using System.Collections.Generic;
using System.Text;
using Mirror;
using Props.ServerRack;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Diagnostic : qu'est-ce qui change visuellement sur une baie serveur quand ses connexions sont coupées ?
// Instantané des lumières / renderers / matériaux de la baie visée, puis différence avec l'instantané précédent.
static class ServerProbe
{
    static Dictionary<string, string> last;

    internal static string ToggleAll()
    {
        if (!NetworkServer.active) return "Hôte uniquement";
        var me = DebugMenu.LocalTC?.netIdentity;
        var all = Object.FindObjectsOfType<ServerController>();
        bool enable = !ServerController.connectionsEnabled;   // état global, partagé par toutes les baies
        foreach (var s in all) s.RpcSetConnectionEnabled(me, enable);
        return $"Serveurs {(enable ? "rétablis" : "coupés")} ({all.Length})";
    }

    internal static string Snapshot()
    {
        var cam = Camera.main;
        if (cam == null || !Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 8f)) return "Rien de visé";
        var rack = hit.collider.GetComponentInParent<ServerController>();
        if (rack == null) return $"Visé : {hit.collider.name} (pas une baie)";

        var now = new Dictionary<string, string>();
        Collect(rack.transform, rack.transform, now);
        var log = Plugin.L;
        log.LogInfo($"[SRV] instantané '{rack.name}' connexions={ServerController.connectionsEnabled} : {now.Count} éléments");
        if (last == null)
            foreach (var kv in now) log.LogInfo($"[SRV]   {kv.Key} = {kv.Value}");
        else
        {
            int n = 0;
            foreach (var kv in now)
                if (!last.TryGetValue(kv.Key, out var before) || before != kv.Value) { log.LogInfo($"[SRV] CHANGÉ {kv.Key} : {before} -> {kv.Value}"); n++; }
            log.LogInfo($"[SRV] {n} différence(s) avec l'instantané précédent");
        }
        last = now;
        return $"Instantané '{rack.name}' (voir log)";
    }

    static void Collect(Transform root, Transform t, Dictionary<string, string> acc)
    {
        var path = Path(root, t);
        acc[path + " [actif]"] = t.gameObject.activeSelf.ToString();
        foreach (var l in t.GetComponents<Light>())
            acc[path + " [Light]"] = $"on={l.enabled} couleur={l.color} intensité={l.intensity:0.00}";
        foreach (var r in t.GetComponents<Renderer>())
        {
            var sb = new StringBuilder($"on={r.enabled}");
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                sb.Append($" | {m.name}");
                if (m.HasProperty("_BaseColor")) sb.Append($" base={m.GetColor("_BaseColor")}");
                if (m.HasProperty("_EmissionColor")) sb.Append($" emission={m.GetColor("_EmissionColor")}");
            }
            acc[path + " [Renderer]"] = sb.ToString();
        }
        for (int i = 0; i < t.childCount; i++) Collect(root, t.GetChild(i), acc);
    }

    static string Path(Transform root, Transform t) => t == root ? t.name : Path(root, t.parent) + "/" + t.name;
}
