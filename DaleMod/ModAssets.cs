using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DaleMod;

// Ressources chargées depuis BepInEx\plugins\DaleMod\Assets (pas d'AssetBundle : on lit .obj et .wav nous-mêmes).
static class ModAssets
{
    internal static string Dir => Path.Combine(Paths.PluginPath, "DaleMod", "Assets");

    // --- Audio compressé (MP3 / OGG) : chargé par le module audio du jeu (UnityWebRequest), en arrière-plan ---
    // 3 à 4 fois plus léger qu'un WAV. Le résultat arrive dans `done` (null en cas d'échec) ; TickLoads est appelé
    // à chaque frame par DebugMenu.Update.
    static readonly List<(UnityEngine.Networking.UnityWebRequest req, string file, Action<AudioClip> done)> loads = new();

    internal static void LoadAudio(string file, Action<AudioClip> done)
    {
        var path = Path.Combine(Dir, file);
        if (!File.Exists(path)) { Plugin.L.LogWarning($"[ASSETS] {path} introuvable"); done(null); return; }
        var type = file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : AudioType.MPEG;
        var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), type);
        req.SendWebRequest();
        loads.Add((req, file, done));
    }

    internal static void TickLoads()
    {
        for (int i = loads.Count - 1; i >= 0; i--)
        {
            var (req, file, done) = loads[i];
            if (!req.isDone) continue;
            loads.RemoveAt(i);
            AudioClip clip = null;
            try
            {
                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req);
                else Plugin.L.LogWarning($"[ASSETS] {file} : {req.error}");
            }
            catch (Exception e) { Plugin.L.LogError($"[ASSETS] {file} : {e.Message}"); }
            req.Dispose();
            if (clip != null) { clip.name = Path.GetFileNameWithoutExtension(file); clip.hideFlags = HideFlags.DontUnloadUnusedAsset; }
            Plugin.L.LogInfo($"[ASSETS] {file} : {(clip == null ? "échec" : $"{clip.length:0.0} s, {clip.channels} canal/canaux, {clip.frequency} Hz")}");
            try { done(clip); } catch (Exception e) { Plugin.L.LogError($"[ASSETS] {file} : {e}"); }
        }
    }

    // .obj simple (v / vt / g / f, polygones convexes) -> un sous-maillage par groupe, dans l'ordre d'apparition.
    // Faces émises des deux côtés et sommets non partagés : rendu à facettes, insensible au sens des faces.
    internal static Mesh LoadObj(string file, out List<string> groups)
    {
        groups = new List<string>();
        var path = Path.Combine(Dir, file);
        if (!File.Exists(path)) { Plugin.L.LogWarning($"[ASSETS] {path} introuvable"); return null; }

        var pos = new List<Vector3>();
        var tex = new List<Vector2>();
        var tris = new List<List<int>>();
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var p = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            switch (p[0])
            {
                case "v":
                    // .obj est en repère main droite, Unity en main gauche : on inverse x
                    pos.Add(new Vector3(-F(p[1]), F(p[2]), F(p[3])));
                    break;
                case "vt":
                    tex.Add(new Vector2(F(p[1]), F(p[2])));
                    break;
                case "g":
                case "o":
                case "usemtl":
                    if (p.Length > 1 && !groups.Contains(p[1])) { groups.Add(p[1]); tris.Add(new List<int>()); }
                    break;
                case "f":
                    if (tris.Count == 0) { groups.Add("default"); tris.Add(new List<int>()); }
                    var t = tris[tris.Count - 1];
                    var idx = new int[p.Length - 1];
                    var tdx = new int[p.Length - 1];
                    for (int i = 1; i < p.Length; i++)
                    {
                        var parts = p[i].Split('/');
                        int n = int.Parse(parts[0]);
                        idx[i - 1] = n < 0 ? pos.Count + n : n - 1;
                        int m = parts.Length > 1 && parts[1].Length > 0 ? int.Parse(parts[1]) : 0;
                        tdx[i - 1] = m == 0 ? -1 : m < 0 ? tex.Count + m : m - 1;
                    }
                    Vector2 UV(int k) => tdx[k] >= 0 ? tex[tdx[k]] : Vector2.zero;
                    for (int i = 1; i + 1 < idx.Length; i++)
                    {
                        AddTri(verts, t, pos[idx[0]], pos[idx[i]], pos[idx[i + 1]]);
                        uvs.Add(UV(0)); uvs.Add(UV(i)); uvs.Add(UV(i + 1));
                        AddTri(verts, t, pos[idx[0]], pos[idx[i + 1]], pos[idx[i]]);
                        uvs.Add(UV(0)); uvs.Add(UV(i + 1)); uvs.Add(UV(i));
                    }
                    break;
            }
        }

        var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(file) };
        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts.ToArray();
        if (tex.Count > 0) mesh.uv = uvs.ToArray();
        mesh.subMeshCount = tris.Count;
        for (int i = 0; i < tris.Count; i++) mesh.SetTriangles(tris[i].ToArray(), i);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Plugin.L.LogInfo($"[ASSETS] {file} : {verts.Count / 3} triangles, groupes {string.Join(", ", groups)}");
        return mesh;
    }

    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    static void AddTri(List<Vector3> verts, List<int> t, Vector3 a, Vector3 b, Vector3 c)
    {
        t.Add(verts.Count); verts.Add(a);
        t.Add(verts.Count); verts.Add(b);
        t.Add(verts.Count); verts.Add(c);
    }

    // .wav PCM 16 bits (mono ou stéréo)
    internal static AudioClip LoadWav(string file)
    {
        var path = Path.Combine(Dir, file);
        if (!File.Exists(path)) { Plugin.L.LogWarning($"[ASSETS] {path} introuvable"); return null; }
        var b = File.ReadAllBytes(path);
        int channels = 0, rate = 0, bits = 0, pos = 12;
        while (pos + 8 <= b.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
            int size = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(b, pos + 10);
                rate = BitConverter.ToInt32(b, pos + 12);
                bits = BitConverter.ToInt16(b, pos + 22);
            }
            else if (id == "data")
            {
                if (bits != 16) { Plugin.L.LogWarning($"[ASSETS] {file} : {bits} bits non géré (16 attendu)"); return null; }
                int count = Math.Min(size, b.Length - pos - 8) / 2;
                var data = new float[count];
                for (int i = 0; i < count; i++) data[i] = BitConverter.ToInt16(b, pos + 8 + i * 2) / 32768f;
                // SetData passe toujours par une surcharge ReadOnlySpan absente de l'interop : on fournit les
                // échantillons via les callbacks de Create, que Unity appelle par blocs (stream=false : dès la création).
                int cursor = 0;
                var read = DelegateSupport.ConvertDelegate<AudioClip.PCMReaderCallback>(new Action<Il2CppStructArray<float>>(buf =>
                {
                    int n = buf.Length;
                    for (int i = 0; i < n; i++) buf[i] = cursor + i < data.Length ? data[cursor + i] : 0f;
                    cursor += n;
                }));
                var seek = DelegateSupport.ConvertDelegate<AudioClip.PCMSetPositionCallback>(new Action<int>(p => cursor = p * channels));
                var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(file), count / channels, channels, rate, false, read, seek);
                clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Plugin.L.LogInfo($"[ASSETS] {file} : {channels} canal/canaux, {rate} Hz, {(float)count / channels / rate:0.0}s");
                return clip;
            }
            pos += 8 + size + (size & 1);
        }
        Plugin.L.LogWarning($"[ASSETS] {file} : WAV invalide");
        return null;
    }

    // .png / .jpg -> Sprite
    internal static Sprite LoadSprite(string file)
    {
        var tex = LoadTexture(file);
        if (tex == null) return null;
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return sprite;
    }

    static readonly Dictionary<string, Texture2D> textures = new();

    // .png / .jpg -> Texture2D (mise en cache). ImageConversion.LoadImage est appelée côté IL2CPP (l'interop passe par des Span absents).
    internal static unsafe Texture2D LoadTexture(string file)
    {
        if (textures.TryGetValue(file, out var cached) && cached != null) return cached;
        var path = Path.Combine(Dir, file);
        if (!File.Exists(path)) { Plugin.L.LogWarning($"[ASSETS] {path} introuvable"); return null; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontUnloadUnusedAsset };
        var data = new Il2CppStructArray<byte>(File.ReadAllBytes(path));

        var klass = IL2CPP.GetIl2CppClass("UnityEngine.ImageConversionModule.dll", "UnityEngine", "ImageConversion");
        var method = IL2CPP.il2cpp_class_get_method_from_name(klass, "LoadImage", 2);
        if (method == IntPtr.Zero) { Plugin.L.LogWarning("[ASSETS] ImageConversion.LoadImage introuvable"); return null; }
        void** args = stackalloc void*[2];
        args[0] = (void*)tex.Pointer;
        args[1] = (void*)data.Pointer;
        IntPtr exc = IntPtr.Zero;
        IL2CPP.il2cpp_runtime_invoke(method, IntPtr.Zero, args, ref exc);
        Il2CppException.RaiseExceptionIfNecessary(exc);

        tex.wrapMode = TextureWrapMode.Repeat;
        textures[file] = tex;
        Plugin.L.LogInfo($"[ASSETS] {file} : {tex.width}x{tex.height}");
        return tex;
    }
}
