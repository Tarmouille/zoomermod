using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// FindObjectsOfType parcourt toute la scène (très cher sous IL2CPP) : pour les boucles appelées à chaque image, la liste
// d'un type est gardée MaxAge secondes. Les objets détruits entre-temps sont sautés (test Unity "== null").
static class SceneCache
{
    const float MaxAge = 0.2f;
    static readonly Dictionary<Type, (float at, object list)> cache = new();

    internal static List<T> All<T>() where T : Object
    {
        float now = Time.unscaledTime;
        if (cache.TryGetValue(typeof(T), out var e) && now - e.at < MaxAge)
        {
            var l = (List<T>)e.list;
            l.RemoveAll(x => x == null);
            return l;
        }
        var fresh = new List<T>();
        foreach (var x in Object.FindObjectsOfType<T>()) if (x != null) fresh.Add(x);
        cache[typeof(T)] = (now, fresh);
        return fresh;
    }
}
