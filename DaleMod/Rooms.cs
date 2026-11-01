using System.Collections.Generic;
using Room;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Pièce (Room.RoomController) qui contient une position : d'après les volumes (colliders) de chaque pièce.
// Le détecteur de pièce du jeu (RoomTrigger) n'est pas sur le personnage (caméra…) : on calcule nous-mêmes, pour
// n'importe quel joueur ou objet. Si plusieurs pièces contiennent le point, la plus petite (sous-pièce) l'emporte.
static class Rooms
{
    static readonly List<(RoomController room, Bounds[] parts, float volume)> cache = new();
    static float builtAt = -1f;

    static void Build()
    {
        if (cache.Count > 0 && Time.time - builtAt < 30f) return;   // la carte ne bouge pas ; rafraîchi de temps en temps
        cache.Clear();
        builtAt = Time.time;
        foreach (var room in Object.FindObjectsOfType<RoomController>())
        {
            var cols = room.GetComponentsInChildren<Collider>(true);
            if (cols.Length == 0) continue;
            var parts = new Bounds[cols.Length];
            float vol = 0f;
            for (int i = 0; i < cols.Length; i++) { parts[i] = cols[i].bounds; vol += parts[i].size.x * parts[i].size.y * parts[i].size.z; }
            cache.Add((room, parts, vol));
        }
    }

    internal static RoomController At(Vector3 p)
    {
        Build();
        RoomController best = null;
        float bestVol = float.MaxValue;
        foreach (var (room, parts, vol) in cache)
        {
            if (room == null || vol >= bestVol) continue;
            foreach (var b in parts)
                if (b.Contains(p)) { best = room; bestVol = vol; break; }
        }
        return best;
    }

    // Pièce d'un joueur ou d'un objet (on prend un point un peu au-dessus du sol : les pieds sont sur la limite)
    // (repli sur la position exacte : un objet fixé au mur, comme le tableau blanc, peut être en limite de pièce)
    internal static RoomController Of(Component c) =>
        c == null ? null : At(c.transform.position + Vector3.up * 0.5f) ?? At(c.transform.position);
}
