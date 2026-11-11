using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using Player.Lobby;
using Player.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Cosmétiques gratuits du mod, ajoutés À LA FIN des listes du jeu (les numéros des objets du jeu ne bougent pas) :
// - Cheveux : "Ponpons" (Ponpons.cs).
// - Barbe : "Moustache ronde" (guidon qui s'enroule + petit bouc), rigide, plaquée sur le visage, couleur des cheveux.
// - Nouvelle catégorie "Accessoire" (13e) : "Nez rouge", "Masque de Glucks" (FaceGear.cs).
// - Lunettes : lunettes 3D, monocle ; Yeux : UwU (FaceGear.cs). Manteau : peignoir, tabard, gilet jaune (Gilet.cs).
// - Chemise : "Chemise des tests" (motif) ; Chemise, Pantalon, Veste : "Léopard" (le peignoir = chemise + jupe léopard).
// Sauvegarde : le disque ne contient JAMAIS rien du mod. À l'écriture d'une apparence dans les préférences du jeu, on
// écrit une version nettoyée (objets du jeu seulement) et on garde la version complète dans notre fichier ; à la lecture,
// si la sauvegarde du jeu est toujours celle qu'on a écrite, on rend la version complète. Sans le mod : perso normal.
// Réseau : l'apparence complète passe par le JSON du jeu ; tous les joueurs ont le mod, donc les mêmes listes.
static class Cosmetics
{
    const string Marker = "DaleMod_Cosmetics";
    static string T(string fr, string en) => RoleKit.T(fr, en);

    // Dans une apparence, une catégorie est repérée par son identifiant (parameterIndex = CustomizationParameter.id, ex.
    // barbe = 1, sourcils = 3), le modèle et la couleur par leur rang dans la liste (rang 0 = "rien" quand la liste
    // commence par un vide). Comptes du jeu sans le mod, par identifiant (relevé du 2026-11-05 ; remplacés par les vrais
    // au premier personnage vu).
    const int AccessoryId = 12, CoatId = 13, SkullId = 14;
    static int vanillaParams = 12;
    static readonly Dictionary<int, (int items, int colors)> vanilla = new()
    {
        [0] = (16, 12), [3] = (4, 12), [11] = (6, 2), [1] = (9, 12), [8] = (1, 5), [10] = (6, 9),
        [2] = (6, 4), [4] = (6, 21), [5] = (3, 10), [6] = (5, 10), [7] = (5, 4), [9] = (2, 4),
    };
    static bool vanillaKnown;

    static int IdOf(CustomizationParameter p) => int.TryParse(p?.id, out var n) ? n : -1;

    // Nos objets, repérés par leurs composants (icônes de l'écran de personnalisation)
    static readonly Dictionary<IntPtr, string> itemIcons = new(), colorIcons = new(), itemNames = new();

    // ===================== Ajout aux listes d'un personnage =====================
    internal static void Ensure(PlayerCustomizer c)
    {
        if (c == null || c.customizationParameters == null) return;
        if (c.transform.Find(Marker) != null) return;
        var ps = c.customizationParameters;
        if (!vanillaKnown && ps.Count == 12)
        {
            vanillaKnown = true;
            vanillaParams = ps.Count;
            vanilla.Clear();
            for (int i = 0; i < ps.Count; i++)
                vanilla[IdOf(ps[i])] = (ps[i]?.customizationItems?.Count ?? 0, ps[i]?.colorItems?.Count ?? 0);
        }
        if (ps.Count != vanillaParams) { Plugin.L.LogWarning($"[COSMETIC] {ps.Count} catégories au lieu de {vanillaParams} : rien ajouté"); return; }

        var root = new GameObject(Marker);
        root.transform.SetParent(c.transform, false);
        root.SetActive(false);   // conteneur des couleurs (composants, jamais affichés)
        var head = Bone(c, "head");
        if (head == null) { Plugin.L.LogWarning("[COSMETIC] os de la tête introuvable"); return; }

        try
        {
            int beard = Find(ps, "Beard"), shirt = Find(ps, "Shirt"), pants = Find(ps, "Pants"), jacket = Find(ps, "Jacket"), hat = Find(ps, "Hat");

            // Barbe : moustache ronde
            if (beard >= 0)
            {
                var it = NewItem(head, "DaleMod_MoustacheRonde", ps[beard], ps[beard].customizationItems.Count);
                ps[beard].customizationItems.Add(it);
                itemIcons[it.Pointer] = "icon_moustache.png";
            }

            // Cheveux : ponpons (deux chignons)
            int hair = Ponpons.FindHair(ps);
            if (hair >= 0)
            {
                var pp = Ponpons.NewItem(ps[hair], ps[hair].customizationItems.Count);
                if (pp != null)
                {
                    ps[hair].customizationItems.Add(pp);
                    itemIcons[pp.Pointer] = "icon_ponpons.png";
                    itemNames[pp.Pointer] = T("Ponpons", "Space buns");
                }
            }

            // Couleurs : chemise des tests, léopard
            var hawaii = FindColor(ps, shirt, "Hawaii");
            if (shirt >= 0 && hawaii != null)
            {
                var jdg = NewColor(root, ps[shirt], ps[shirt].colorItems.Count, T("Chemise des tests", "Test shirt"), TexturedFrom(hawaii.material, "cosmetics/shirt_jdg.png"), hawaii.strictItems);
                colorIcons[jdg.Pointer] = "cosmetics/swatch_jdg.png";
                ps[shirt].colorItems.Add(jdg);
            }
            var leopardMat = hawaii != null ? TexturedFrom(hawaii.material, "cosmetics/leopard.png") : null;
            foreach (int p in new[] { shirt })
            {
                if (p < 0 || leopardMat == null) continue;
                var leo = NewColor(root, ps[p], ps[p].colorItems.Count, T("Léopard", "Leopard"), leopardMat, null);
                colorIcons[leo.Pointer] = "cosmetics/swatch_leopard.png";
                ps[p].colorItems.Add(leo);
            }

            // 13e catégorie : Accessoire (rien / nez rouge)
            var acc = new CustomizationParameter();
            acc.customizationType = hat >= 0 ? ps[hat].customizationType : CustomizationType.Hat;
            acc.id = AccessoryId.ToString();
            acc.parameterName = "Accessory";   // l'écran affiche la traduction de "customize.categories.accessory"
            acc.colorOnly = false;
            acc.playerRoles = new Il2CppSystem.Collections.Generic.List<PlayerRole>();
            foreach (var r in new[] { PlayerRole.Manager, PlayerRole.Specialist, PlayerRole.Slacker, PlayerRole.Janitor }) acc.playerRoles.Add(r);
            acc.customizationItems = new Il2CppSystem.Collections.Generic.List<CustomizationItem>();
            acc.customizationItems.Add(null);
            var nose = NewItem(head, "DaleMod_NezRouge", acc, 1);
            acc.customizationItems.Add(nose);
            itemIcons[nose.Pointer] = "icon_nose.png";
            acc.colorItems = new Il2CppSystem.Collections.Generic.List<ColorItem>();
            acc.colorItems.Add(NewColor(root, acc, 0, T("Rouge", "Red"), Plain(new Color(0.85f, 0.08f, 0.1f), 0.15f), null));
            acc.colorParameters = new Il2CppSystem.Collections.Generic.List<ColorParameter>();
            // 3e lot : lunettes 3D, monocle (Lunettes), yeux UwU (Yeux), masque de Glucks (Accessoire)
            FaceGear.AddItems(ps, acc, head, itemIcons, itemNames);
            ps.Add(acc);

            // 14e catégorie : Manteau (rien / peignoir), par-dessus la chemise
            var coat = new CustomizationParameter();
            coat.customizationType = hat >= 0 ? ps[hat].customizationType : CustomizationType.Hat;   // pas "Chemise" : le jeu la traite à part (cravate)
            coat.id = CoatId.ToString();
            coat.parameterName = "Coat";       // "customize.categories.coat"
            coat.colorOnly = false;
            coat.playerRoles = new Il2CppSystem.Collections.Generic.List<PlayerRole>();
            foreach (var r in new[] { PlayerRole.Manager, PlayerRole.Specialist, PlayerRole.Slacker }) coat.playerRoles.Add(r);   // pas le janitor (combinaison)
            coat.customizationItems = new Il2CppSystem.Collections.Generic.List<CustomizationItem>();
            coat.customizationItems.Add(null);
            var robe = Robe.NewItem(c, coat, 1, Plain(Color.white, 0.1f));
            if (robe != null) { coat.customizationItems.Add(robe); itemIcons[robe.Pointer] = "icon_robe.png"; }
            var tabard = Tabard.NewItem(c, coat, coat.customizationItems.Count, CosmeticExtras.Textured("cosmetics/tabard.png", 0.1f, 0f), CosmeticExtras.Lit(Color.white, 0f, 0f));
            if (tabard != null) { coat.customizationItems.Add(tabard); itemIcons[tabard.Pointer] = "icon_tabard.png"; itemNames[tabard.Pointer] = T("Tabard de Solaire", "Solaire's tabard"); }
            CustomizationItem teeItem = null;
            if (shirt >= 0) foreach (var x in ps[shirt].customizationItems) if (x != null && x.gameObject.name == Gilet.Source) teeItem = x;
            var gilet = teeItem != null ? Gilet.NewItem(c, coat, coat.customizationItems.Count, teeItem.colorIndexInRenderer, CosmeticExtras.Lit(Color.white, 0f, 0f)) : null;
            if (gilet != null) { coat.customizationItems.Add(gilet); itemIcons[gilet.Pointer] = "icon_gilet.png"; itemNames[gilet.Pointer] = T("Gilet jaune", "Hi-vis vest"); }
            coat.colorItems = new Il2CppSystem.Collections.Generic.List<ColorItem>();
            int ci = 0;
            if (leopardMat != null) { var x = NewColor(root, coat, ci++, T("Léopard", "Leopard"), leopardMat, null); colorIcons[x.Pointer] = "cosmetics/swatch_leopard.png"; coat.colorItems.Add(x); }
            coat.colorItems.Add(NewColor(root, coat, ci++, T("Blanc éponge", "Terry white"), Plain(new Color(0.93f, 0.92f, 0.88f), 0.05f), null));
            coat.colorItems.Add(NewColor(root, coat, ci++, T("Rose", "Pink"), Plain(new Color(0.95f, 0.6f, 0.75f), 0.1f), null));
            coat.colorItems.Add(NewColor(root, coat, ci++, T("Bordeaux", "Burgundy"), Plain(new Color(0.45f, 0.06f, 0.12f), 0.35f), null));
            // Cuir : texture à grain, un peu brillant (ajoutés à la fin : les numéros des couleurs existantes ne bougent pas)
            if (hawaii != null)
                foreach (var (tex, fr, en) in new[] { ("leather", "Cuir", "Leather"), ("leather_black", "Cuir noir", "Black leather") })
                {
                    var m = TexturedFrom(hawaii.material, $"cosmetics/{tex}.png");
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.55f);
                    var x = NewColor(root, coat, ci++, T(fr, en), m, null);
                    colorIcons[x.Pointer] = $"cosmetics/swatch_{tex}.png";
                    coat.colorItems.Add(x);
                }
            coat.colorParameters = new Il2CppSystem.Collections.Generic.List<ColorParameter>();
            if (robe != null) ps.Add(coat);

            // 2e lot : chapeaux (sombrero, viking, Solaire) et vêtements peints (t-shirt au logo, Solaire, hoodie)
            if (hat >= 0) CosmeticExtras.AddHats(c, ps[hat], head, itemIcons, itemNames);
            CosmeticExtras.AddClothes(c, ps, root, colorIcons, NewColor);

            // 15e catégorie : Crâne (rien / raboté)
            var skull = new CustomizationParameter();
            skull.customizationType = hat >= 0 ? ps[hat].customizationType : CustomizationType.Hat;
            skull.id = SkullId.ToString();
            skull.parameterName = "Skull";     // "customize.categories.skull"
            skull.colorOnly = false;
            skull.playerRoles = new Il2CppSystem.Collections.Generic.List<PlayerRole>();
            foreach (var r in new[] { PlayerRole.Manager, PlayerRole.Specialist, PlayerRole.Slacker, PlayerRole.Janitor }) skull.playerRoles.Add(r);
            skull.customizationItems = new Il2CppSystem.Collections.Generic.List<CustomizationItem>();
            skull.customizationItems.Add(null);
            var dummy = CosmeticExtras.Lit(Color.white, 0f, 0f);
            var cut = SkullCut.NewItem(head, skull, 1, dummy);
            skull.customizationItems.Add(cut);
            itemIcons[cut.Pointer] = "icon_rabot.png";
            itemNames[cut.Pointer] = T("Crâne raboté", "Planed skull");
            skull.colorItems = new Il2CppSystem.Collections.Generic.List<ColorItem>();
            skull.colorItems.Add(NewColor(root, skull, 0, " ", dummy, null));
            skull.colorParameters = new Il2CppSystem.Collections.Generic.List<ColorParameter>();
            ps.Add(skull);
            var cats = new System.Text.StringBuilder();
            foreach (var p in ps) cats.Append($" {p?.parameterName}={p?.id}({p?.customizationItems?.Count})");
            Plugin.L.LogInfo($"[COSMETIC] objets du mod ajoutés à {c.transform.parent?.name}/{c.name} ({ps.Count} catégories :{cats})");
        }
        catch (Exception e) { Plugin.L.LogError("[COSMETIC] ajout : " + e); }
    }

    static int Find(Il2CppSystem.Collections.Generic.List<CustomizationParameter> ps, string name)
    {
        for (int i = 0; i < ps.Count; i++) if (ps[i]?.parameterName == name) return i;
        return -1;
    }

    static ColorItem FindColor(Il2CppSystem.Collections.Generic.List<CustomizationParameter> ps, int p, string name)
    {
        if (p < 0) return null;
        foreach (var ci in ps[p].colorItems) if (ci != null && ci.itemName == name) return ci;
        return null;
    }

    // Vue à la première personne : le jeu passe la tête du joueur local sur une couche que sa caméra ne rend pas
    // (PlayerController.SetLayerRecursively, une seule fois). Nos objets de tête, créés après, restaient visibles :
    // ils reprennent la couche (et le mode d'ombre) des chapeaux / cheveux du jeu sur le même personnage.
    internal static void MatchLayers(PlayerCustomizer c)
    {
        var head = Bone(c, "head");
        if (head == null) return;
        Renderer refR = null;
        var hatsRoot = head.Find("Hats");
        if (hatsRoot != null)
            foreach (var r in hatsRoot.GetComponentsInChildren<Renderer>(true))
                if (!r.name.StartsWith("DaleMod_")) { refR = r; if (r.gameObject.activeInHierarchy) break; }
        if (refR == null) return;
        int layer = refR.gameObject.layer;
        foreach (var t in head.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("DaleMod_")) continue;
            t.gameObject.layer = layer;
            var r = t.GetComponent<Renderer>();
            if (r != null) r.shadowCastingMode = refR.shadowCastingMode;
        }
    }

    // Filet de sécurité pour le joueur local (le jeu change sa couche après son apparition)
    static float nextLayers;
    internal static void Tick()
    {
        if (Time.unscaledTime < nextLayers) return;
        nextLayers = Time.unscaledTime + 1f;
        var me = Mirror.NetworkClient.localPlayer;
        var c = me != null ? me.GetComponentInChildren<PlayerCustomizer>(true) : null;
        if (c != null) MatchLayers(c);
    }

    static Transform Bone(PlayerCustomizer c, string name)
    {
        foreach (var t in c.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    // Objet rigide sous l'os de la tête ; son maillage est construit au placement (Place)
    static CustomizationItem NewItem(Transform head, string name, CustomizationParameter p, int id)
    {
        var go = new GameObject(name);
        go.transform.SetParent(head, false);
        go.SetActive(false);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = new Mesh { name = name };   // remplacé au placement
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = Plain(Color.gray, 0.4f);
        var it = go.AddComponent<CustomizationItem>();
        CustomizationItem model = null;
        if (p.customizationItems != null) foreach (var x in p.customizationItems) if (x != null) { model = x; break; }
        it.customizationType = model != null ? model.customizationType : p.customizationType;
        it.itemName = name;
        it.id = id.ToString();
        it.price = 0;
        it.cutHair = false;
        it.modelRenderer = mr;
        it.meshFilter = mf;
        it.clonedMesh = mf.sharedMesh;   // le jeu déforme les objets rigides selon la corpulence à partir de cette copie
        it.colorIndexInRenderer = 0;
        return it;
    }

    static ColorItem NewColor(GameObject root, CustomizationParameter p, int id, string name, Material m, Il2CppSystem.Collections.Generic.List<CustomizationItem> strict)
    {
        var go = new GameObject("DaleMod_Color_" + id);
        go.transform.SetParent(root.transform, false);
        var ci = go.AddComponent<ColorItem>();
        ci.id = id.ToString();
        ci.customizationType = p.customizationType;
        ci.itemName = name;
        ci.material = m;
        ci.price = 0;
        ci.strictItems = new Il2CppSystem.Collections.Generic.List<CustomizationItem>();
        if (strict != null) foreach (var s in strict) ci.strictItems.Add(s);
        return ci;
    }

    static readonly Dictionary<string, Material> materials = new();

    static Material TexturedFrom(Material model, string texture)
    {
        if (materials.TryGetValue(texture, out var m) && m != null) return m;
        var tex = ModAssets.LoadTexture(texture);
        m = new Material(model) { name = "DaleMod_" + Path.GetFileNameWithoutExtension(texture), hideFlags = HideFlags.DontUnloadUnusedAsset };
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        return materials[texture] = m;
    }

    static Material Plain(Color c, float smooth)
    {
        string key = "plain_" + ColorUtility.ToHtmlStringRGB(c);
        if (materials.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "DaleMod_" + key, hideFlags = HideFlags.DontUnloadUnusedAsset };
        m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        return materials[key] = m;
    }

    // ===================== Placement sur le visage =====================
    // Repère "visage" : origine au bout du nez, x à droite, y en haut, z vers l'avant. Les maillages du mod sont écrits
    // pour une tête de 0,28 m de large ; leur z est une hauteur au-dessus de la peau (plaqués sur le visage réel).
    internal sealed class Face
    {
        public Transform head;
        public Vector3 nose, right, up, fwd;
        public float k;                       // échelle (largeur de tête / 0,28)
        public float eyeDX;                   // demi-écart des yeux (0 : inconnu)
        public float Radius => 0.12f * k;     // arrondi du visage (vu de dessus)
        public List<Vector3> pts = new();     // sommets de la tête, dans le repère visage
        public Vector3 World(Vector3 f) => nose + right * f.x + up * f.y + fwd * f.z;
        public float Surface(float x, float y)
        {
            float best = float.NegativeInfinity, r2 = (0.022f * k) * (0.022f * k), nearD = float.MaxValue, nearZ = 0f;
            foreach (var p in pts)
            {
                float d = (p.x - x) * (p.x - x) + (p.y - y) * (p.y - y);
                if (d < r2 && p.z > best) best = p.z;
                if (d < nearD) { nearD = d; nearZ = p.z; }
            }
            return float.IsNegativeInfinity(best) ? nearZ : best;
        }
    }

    internal static Face Measure(PlayerCustomizer c)
    {
        var head = Bone(c, "head");
        SkinnedMeshRenderer smr = null;
        foreach (var r in c.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            if (r.name.StartsWith("Head") && r.gameObject.activeInHierarchy) { smr = r; break; }
        if (head == null || smr == null) return null;
        var baked = new Mesh();
        smr.BakeMesh(baked);
        Vector3[] local = baked.vertices;
        // Selon la version, le maillage cuit est à l'échelle ou non : on garde la conversion qui retombe sur les bornes
        var tr = smr.transform;
        var a = new List<Vector3>(); var b = new List<Vector3>();
        foreach (var v in local) { a.Add(tr.TransformPoint(v)); b.Add(tr.position + tr.rotation * v); }
        var world = Closer(a, b, smr.bounds.center) ? a : b;
        Object.Destroy(baked);

        var f = new Face { head = head, fwd = head.forward, up = head.up };
        f.right = Vector3.Cross(f.up, f.fwd);
        Vector3 center = head.position;
        var eyePts = EyesPoints(c);
        Vector3? eyes = null;
        if (eyePts != null)
        {
            Vector3 sum = Vector3.zero;
            foreach (var x in eyePts) sum += x;
            eyes = sum / eyePts.Count;
            float dx = 0f;
            foreach (var x in eyePts) dx += Mathf.Abs(Vector3.Dot(x - eyes.Value, f.right));
            f.eyeDX = dx / eyePts.Count;
        }
        // largeur : étendue en x ; nez : point le plus en avant au centre
        float minX = float.MaxValue, maxX = float.MinValue;
        foreach (var w in world) { float x = Vector3.Dot(w - center, f.right); minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
        float width = maxX - minX;
        f.k = width / 0.28f;
        // Pas de nez modélisé : le "nez" est le point de la peau à NoseBelowEyes sous le centre des yeux
        foreach (var w in world) { var d = w - center; f.pts.Add(new Vector3(Vector3.Dot(d, f.right), Vector3.Dot(d, f.up), Vector3.Dot(d, f.fwd))); }
        float ey = eyes.HasValue ? Vector3.Dot(eyes.Value - center, f.up) : 0f;
        float ny = ey - NoseBelowEyes * f.k;
        var noseF = new Vector3(0f, ny, f.Surface(0f, ny));
        f.nose = center + f.right * noseF.x + f.up * noseF.y + f.fwd * noseF.z;
        for (int i = 0; i < f.pts.Count; i++) f.pts[i] -= noseF;
        Plugin.L.LogInfo($"[COSMETIC] yeux {(eyes.HasValue ? "trouvés" : "introuvables")} : {ey:0.000} m au-dessus de l'os de la tête, demi-écart {f.eyeDX / f.k:0.000} (tête de 0,28), nez à {ny:0.000}, avancée {noseF.z:0.000}");
        return f;
    }

    const float NoseBelowEyes = 0.05f;

    // Sommets des yeux (maillage animé cuit), en monde. Les yeux affichés d'abord ; sinon (yeux UwU du mod portés) ceux
    // du jeu, masqués mais toujours là.
    static List<Vector3> EyesPoints(PlayerCustomizer c)
    {
        SkinnedMeshRenderer pick = null;
        foreach (var r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!r.name.StartsWith("Eyes")) continue;
            if (r.gameObject.activeInHierarchy) { pick = r; break; }
            pick ??= r;
        }
        if (pick == null) return null;
        var baked = new Mesh();
        pick.BakeMesh(baked);
        Vector3[] v = baked.vertices;
        Object.Destroy(baked);
        if (v.Length == 0) return null;
        var a = new List<Vector3>(); var b = new List<Vector3>();
        foreach (var x in v) { a.Add(pick.transform.TransformPoint(x)); b.Add(pick.transform.position + pick.transform.rotation * x); }
        return Closer(a, b, pick.bounds.center) ? a : b;
    }

    static bool Closer(List<Vector3> a, List<Vector3> b, Vector3 target)
    {
        Vector3 ca = Vector3.zero, cb = Vector3.zero;
        foreach (var v in a) ca += v; foreach (var v in b) cb += v;
        ca /= Mathf.Max(1, a.Count); cb /= Mathf.Max(1, b.Count);
        return (ca - target).sqrMagnitude <= (cb - target).sqrMagnitude;
    }

    static readonly Dictionary<IntPtr, string> placedFor = new();   // personnage -> tête pour laquelle les maillages sont faits

    internal static void Place(PlayerCustomizer c)
    {
        var headBone = Bone(c, "head");
        Face shared = null;
        Face Once() => shared ??= Measure(c);   // une seule mesure du visage pour tous les objets
        if (headBone != null)
            try { CosmeticExtras.PlaceHats(c, headBone, Once); } catch (Exception e) { Plugin.L.LogError("[COSMETIC] chapeaux : " + e); }
        if (headBone != null)
            try { FaceGear.Place(c, headBone, Once); } catch (Exception e) { Plugin.L.LogError("[COSMETIC] visage : " + e); }
        try { Ponpons.Place(c, Once); } catch (Exception e) { Plugin.L.LogError("[COSMETIC] ponpons : " + e); }

        var head = Bone(c, "head");
        if (head == null) return;
        var moustache = head.Find("DaleMod_MoustacheRonde");
        var nose = head.Find("DaleMod_NezRouge");
        if (moustache == null && nose == null) return;
        if (!(moustache?.gameObject.activeSelf == true || nose?.gameObject.activeSelf == true)) return;   // rien de porté
        string key = HeadKey(c);
        if (placedFor.TryGetValue(c.Pointer, out var k0) && k0 == key) return;
        var f = Once();
        if (f == null) { Plugin.L.LogWarning("[COSMETIC] visage introuvable"); return; }
        placedFor[c.Pointer] = key;
        if (moustache != null) SetMesh(moustache, f, FaceMesh("moustache_ronde.obj"), true, 1.25f);
        if (nose != null) SetMesh(nose, f, NoseMesh(), false, 1f);
        Plugin.L.LogInfo($"[COSMETIC] placé sur {key} : échelle {f.k:0.000}, nez à {head.InverseTransformPoint(f.nose)} (repère tête), {f.pts.Count} sommets");
    }

    static Transform FindDeep(Transform t, string name)
    {
        foreach (var x in t.GetComponentsInChildren<Transform>(true)) if (x.name == name) return x;
        return null;
    }

    static string HeadKey(PlayerCustomizer c)
    {
        foreach (var r in c.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            if (r.name.StartsWith("Head") && r.gameObject.activeInHierarchy) return r.name + "@" + c.transform.lossyScale.x.ToString("0.000");
        return "?";
    }

    // Maillage du visage : sommets (x, y, hauteur au-dessus de la peau), un seul sous-maillage
    static readonly Dictionary<string, (Vector3[] v, int[] t)> faceMeshes = new();

    static (Vector3[] v, int[] t) FaceMesh(string file)
    {
        if (faceMeshes.TryGetValue(file, out var m)) return m;
        var path = Path.Combine(ModAssets.Dir, "cosmetics", file);
        var v = new List<Vector3>();
        var t = new List<int>();
        if (File.Exists(path))
            foreach (var raw in File.ReadAllLines(path))
            {
                var p = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 4) continue;
                if (p[0] == "v") v.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                else if (p[0] == "f")
                {
                    var idx = new int[p.Length - 1];
                    for (int i = 1; i < p.Length; i++) idx[i - 1] = int.Parse(p[i].Split('/')[0]) - 1;
                    for (int i = 1; i + 1 < idx.Length; i++) { t.Add(idx[0]); t.Add(idx[i]); t.Add(idx[i + 1]); }
                }
            }
        else Plugin.L.LogWarning("[COSMETIC] " + path + " introuvable");
        return faceMeshes[file] = TwoSided(v, t);
    }

    // Faces des deux côtés avec des sommets à part (des sommets partagés donneraient des normales nulles : rendu noir),
    // un sommet par triangle : rendu à facettes comme les objets du jeu
    static (Vector3[] v, int[] t) TwoSided(List<Vector3> v, List<int> t)
    {
        var ov = new List<Vector3>();
        var ot = new List<int>();
        for (int i = 0; i + 2 < t.Count; i += 3)
        {
            Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
            int n = ov.Count;
            ov.Add(a); ov.Add(b); ov.Add(c); ov.Add(a); ov.Add(c); ov.Add(b);
            ot.Add(n); ot.Add(n + 1); ot.Add(n + 2); ot.Add(n + 3); ot.Add(n + 4); ot.Add(n + 5);
        }
        return (ov.ToArray(), ot.ToArray());
    }

    static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    // Nez : sphère à facettes, posée devant le bout du nez
    static (Vector3[] v, int[] t) NoseMesh()
    {
        if (faceMeshes.TryGetValue("nose", out var m)) return m;
        const int rings = 8, segs = 12;
        const float r = 0.021f;
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i <= rings; i++)
        {
            float a = Mathf.PI * i / rings;
            for (int j = 0; j <= segs; j++)
            {
                float b = 2f * Mathf.PI * j / segs;
                v.Add(new Vector3(r * Mathf.Sin(a) * Mathf.Cos(b), r * Mathf.Cos(a) + 0.014f, r * Mathf.Sin(a) * Mathf.Sin(b) + 0.006f));   // un peu au-dessus de la moustache
            }
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < segs; j++)
            {
                int a = i * (segs + 1) + j, b = a + segs + 1;
                t.Add(a); t.Add(b); t.Add(a + 1);
                t.Add(a + 1); t.Add(b); t.Add(b + 1);
                t.Add(a); t.Add(a + 1); t.Add(b);
                t.Add(a + 1); t.Add(b + 1); t.Add(b);
            }
        return faceMeshes["nose"] = TwoSided(v, t);
    }

    static void SetMesh(Transform obj, Face f, (Vector3[] v, int[] t) src, bool onSkin, float size)
    {
        var verts = new Vector3[src.v.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            var s = src.v[i] * f.k * size;
            // Posé sur le visage sans suivre ses facettes : profondeur de la colonne centrale à cette hauteur, courbée
            // selon l'arrondi de la tête (une projection point par point tordait la moustache)
            var fp = onSkin ? new Vector3(s.x, s.y, f.Surface(0f, s.y > -0.04f * f.k ? -0.022f * f.k : s.y) - s.x * s.x / (2f * f.Radius) + s.z) : s;
            verts[i] = obj.InverseTransformPoint(f.World(fp));
        }
        var mesh = new Mesh { name = obj.name };
        mesh.vertices = verts;
        mesh.triangles = src.t;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        obj.GetComponent<MeshFilter>().sharedMesh = mesh;
        var it = obj.GetComponent<CustomizationItem>();
        if (it != null) it.clonedMesh = mesh;
    }

    // ===================== Patchs =====================
    [HarmonyPatch(typeof(PlayerCustomizer), nameof(PlayerCustomizer.Customize))]
    [HarmonyPrefix]
    static void BeforeCustomize(PlayerCustomizer __instance, PlayerCustomization playerCustomization)
    {
        long t0 = Perf.Start();
        try
        {
            Ensure(__instance);
            Pad(playerCustomization, __instance.customizationParameters);
        }
        catch (Exception e) { Plugin.L.LogError("[COSMETIC] avant Customize : " + e); }
        Perf.Stop("COSM-AVANT", t0);
    }

    [HarmonyPatch(typeof(PlayerCustomizer), nameof(PlayerCustomizer.Customize))]
    [HarmonyPostfix]
    static void AfterCustomize(PlayerCustomizer __instance)
    {
        long t0 = Perf.Start();
        try { placedFor.Remove(__instance.Pointer); Place(__instance); } catch (Exception e) { Plugin.L.LogError("[COSMETIC] placement : " + e); }
        try { HideJacketUnderRobe(__instance); } catch (Exception e) { Plugin.L.LogError("[COSMETIC] veste : " + e); }
        try { var hb = Bone(__instance, "head"); if (hb != null) CosmeticExtras.HideFaceUnderHelmet(__instance, hb); } catch (Exception e) { Plugin.L.LogError("[COSMETIC] casque : " + e); }
        try { var hb = Bone(__instance, "head"); if (hb != null) FaceGear.HideUnderMask(__instance, hb); } catch (Exception e) { Plugin.L.LogError("[COSMETIC] masque : " + e); }
        try { var hb = Bone(__instance, "head"); if (hb != null) SkullCut.Apply(__instance, hb); } catch (Exception e) { Plugin.L.LogError("[COSMETIC] crâne : " + e); }
        try { MatchLayers(__instance); } catch (Exception e) { Plugin.L.LogError("[COSMETIC] couches : " + e); }
        Perf.Stop("COSM-APRES", t0);
    }

    // Corpulence : le jeu déforme chaque objet d'un personnage. Les nôtres sont déjà taillés sur le personnage réel
    // (et n'ont pas les données internes attendues) : on les laisse tels quels.
    [HarmonyPatch(typeof(MeshDeform), nameof(MeshDeform.DeformMesh))]
    [HarmonyPrefix]
    static bool SkipDeform(MeshDeform __instance) => !IsOurs(__instance);

    [HarmonyPatch(typeof(MeshDeform), nameof(MeshDeform.DeformMeshSkinnedMeshRenderer))]
    [HarmonyPrefix]
    static bool SkipDeformSkinned(MeshDeform __instance) => !IsOurs(__instance);

    [HarmonyPatch(typeof(MeshDeform), nameof(MeshDeform.DeformMeshMeshRenderer))]
    [HarmonyPrefix]
    static bool SkipDeformRigid(MeshDeform __instance) => !IsOurs(__instance);

    [HarmonyPatch(typeof(MeshDeform), nameof(MeshDeform.ResetMesh))]
    [HarmonyPrefix]
    static bool SkipReset(MeshDeform __instance) => !IsOurs(__instance);

    // Peignoir ou gilet jaune porté : la veste du manager (catégorie Veste) est masquée, le manteau en tient lieu
    static void HideJacketUnderRobe(PlayerCustomizer c)
    {
        var robe = FindDeep(c.transform, Robe.Name);
        var gilet = FindDeep(c.transform, Gilet.Name);
        if (!(robe != null && robe.gameObject.activeSelf) && !(gilet != null && gilet.gameObject.activeSelf)) return;
        foreach (var r in c.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            if (r.name.StartsWith("Jacket") && r.gameObject.activeSelf) r.gameObject.SetActive(false);
    }

    static bool IsOurs(MeshDeform d) => d != null && d.gameObject.name.StartsWith("DaleMod_") && d.gameObject.name != Robe.Name && d.gameObject.name != Gilet.Name;

    // Vérification du jeu (arrivée en partie, réseau) : réglée sur ses 12 catégories, elle supprime les nôtres. On les
    // met de côté avant et on les remet après.
    static readonly Dictionary<IntPtr, List<(int p, int m, int c)>> keptByVerify = new();

    [HarmonyPatch(typeof(PlayerCustomization), nameof(PlayerCustomization.Verify))]
    [HarmonyPrefix]
    static void BeforeVerify(PlayerCustomization __instance)
    {
        var sel = __instance?.customizationParameterSelection;
        if (sel == null) return;
        var kept = new List<(int, int, int)>();
        foreach (var x in sel) if (x != null && x.parameterIndex >= AccessoryId && x.parameterIndex <= SkullId) kept.Add((x.parameterIndex, x.modelIndex, x.colorIndex));
        if (kept.Count > 0) keptByVerify[__instance.Pointer] = kept;
    }

    [HarmonyPatch(typeof(PlayerCustomization), nameof(PlayerCustomization.Verify))]
    [HarmonyPostfix]
    static void AfterVerify(PlayerCustomization __instance)
    {
        if (__instance == null || !keptByVerify.Remove(__instance.Pointer, out var kept)) return;
        var sel = __instance.customizationParameterSelection;
        foreach (var (pi, m, c) in kept)
        {
            bool there = false;
            foreach (var x in sel) if (x != null && x.parameterIndex == pi) { there = true; break; }
            if (there) continue;
            var s = new CustomizationParameterSelection();
            s.parameterIndex = pi; s.modelIndex = m; s.colorIndex = c;
            sel.Add(s);
        }
    }

    // Apparence venue d'un perso enregistré sans le mod ou d'un joueur : une sélection par catégorie
    static void Pad(PlayerCustomization pc, Il2CppSystem.Collections.Generic.List<CustomizationParameter> ps)
    {
        var sel = pc?.customizationParameterSelection;
        if (sel == null || ps == null) return;
        foreach (var p in ps)
        {
            int id = IdOf(p);
            if (id < 0) continue;
            bool found = false;
            foreach (var s0 in sel) if (s0 != null && s0.parameterIndex == id) { found = true; break; }
            if (found) continue;
            var s = new CustomizationParameterSelection();
            s.parameterIndex = id; s.modelIndex = 0; s.colorIndex = 0;
            sel.Add(s);
        }
    }

    // L'écran de personnalisation : liste de catégories du personnage d'aperçu (le même objet, ou une copie)
    [HarmonyPatch(typeof(NewCustomizeTab), nameof(NewCustomizeTab.UpdateTab))]
    [HarmonyPrefix]
    static void BeforeTab(NewCustomizeTab __instance) => EnsureTab(__instance);

    [HarmonyPatch(typeof(NewCustomizeTab), nameof(NewCustomizeTab.SelectCategory))]
    [HarmonyPrefix]
    static void BeforeCategory(NewCustomizeTab __instance) => EnsureTab(__instance);

    static void EnsureTab(NewCustomizeTab tab)
    {
        try
        {
            var c = tab.playerCustomizer;
            Ensure(c);
            var mine = c?.customizationParameters;
            if (tab.parameters != null && mine != null && tab.parameters.Pointer != mine.Pointer)
                for (int i = tab.parameters.Count; i < mine.Count; i++) tab.parameters.Add(mine[i]);
            if (tab.playerCustomization != null) Pad(tab.playerCustomization, mine);
        }
        catch (Exception e) { Plugin.L.LogError("[COSMETIC] écran : " + e); }
    }

    // Icônes de nos objets dans l'écran de personnalisation
    [HarmonyPatch(typeof(UI.Tabs.NewCustomizationTab.CustomizationModelItem), nameof(UI.Tabs.NewCustomizationTab.CustomizationModelItem.SetItem))]
    [HarmonyPostfix]
    static void AfterSetItem(UI.Tabs.NewCustomizationTab.CustomizationModelItem __instance, CustomizationItem customizationItem)
    {
        if (customizationItem == null || !itemIcons.TryGetValue(customizationItem.Pointer, out var icon)) return;
        var sp = ModAssets.LoadSprite(icon.StartsWith("cosmetics/") ? icon : "cosmetics/" + icon);
        if (__instance.image != null && sp != null) { __instance.image.sprite = sp; __instance.image.color = Color.white; }
        if (__instance.itemName != null && itemNames.TryGetValue(customizationItem.Pointer, out var label)) __instance.itemName.text = label;
        else if (__instance.itemName != null)
            __instance.itemName.text = customizationItem.itemName.StartsWith("DaleMod_Moustache") ? T("Moustache ronde", "Round moustache")
                                     : customizationItem.itemName == Robe.Name ? T("Peignoir", "Bathrobe") : T("Nez rouge", "Red nose");
        if (__instance.priceText != null) __instance.priceText.text = "";
    }

    [HarmonyPatch(typeof(CustomizationColorItem), nameof(CustomizationColorItem.SetItem))]
    [HarmonyPostfix]
    static void AfterSetColor(CustomizationColorItem __instance, ColorItem colorItem)
    {
        if (colorItem == null || !colorIcons.TryGetValue(colorItem.Pointer, out var icon)) return;
        var sp = ModAssets.LoadSprite(icon);
        if (__instance.colorMeshRenderer != null && sp != null) { __instance.colorMeshRenderer.sprite = sp; __instance.colorMeshRenderer.color = Color.white; }
        if (__instance.colorName != null) __instance.colorName.text = colorItem.itemName;
    }

    internal static string Localize(string key) => key switch
    {
        "customize.categories.accessory" => T("Accessoire", "Accessory"),
        "customize.categories.coat" => T("Manteau", "Coat"),
        "customize.categories.skull" => T("Crâne", "Skull"),
        _ => null,
    };

    // ===================== Sauvegarde : rien du mod sur le disque =====================
    static string StoreFile => Path.Combine(Paths.ConfigPath, "ZoomerMod.cosmetiques.txt");
    static Dictionary<string, string> store;   // apparence nettoyée (sur le disque) -> apparence complète

    static Dictionary<string, string> Store()
    {
        if (store != null) return store;
        store = new Dictionary<string, string>();
        try
        {
            if (File.Exists(StoreFile))
                foreach (var line in File.ReadAllLines(StoreFile))
                {
                    int tab = line.IndexOf('\t');
                    if (tab > 0) store[line.Substring(0, tab)] = line.Substring(tab + 1);
                }
        }
        catch (Exception e) { Plugin.L.LogWarning("[COSMETIC] lecture du fichier : " + e.Message); }
        return store;
    }

    static void SaveStore()
    {
        try
        {
            var lines = new List<string>();
            foreach (var kv in store) lines.Add(kv.Key + "\t" + kv.Value);
            if (lines.Count > 60) lines.RemoveRange(0, lines.Count - 60);
            File.WriteAllLines(StoreFile, lines);
        }
        catch (Exception e) { Plugin.L.LogWarning("[COSMETIC] écriture du fichier : " + e.Message); }
    }

    // Version "jeu seul" d'une apparence : catégories et numéros du mod retirés (null : rien du mod dedans)
    static string Sanitize(string json)
    {
        var pc = PlayerCustomization.FromJson(json);
        var sel = pc?.customizationParameterSelection;
        if (sel == null) return null;
        bool changed = false;
        for (int i = sel.Count - 1; i >= 0; i--)
        {
            var s = sel[i];
            if (s == null) continue;
            int p = s.parameterIndex;
            if (!vanilla.TryGetValue(p, out var counts))
            {
                if (s.modelIndex != 0 || s.colorIndex != 0) changed = true;
                sel.RemoveAt(i);
                continue;
            }
            if (s.modelIndex >= counts.items) { s.modelIndex = 0; changed = true; }
            if (s.colorIndex >= counts.colors) { s.colorIndex = 0; changed = true; }
        }
        return changed ? pc.ToJson() : null;
    }

    [HarmonyPatch(typeof(PlayerPrefs), nameof(PlayerPrefs.SetString))]
    [HarmonyPrefix]
    static void BeforeSetString(string key, ref string value)
    {
        if (key == null || !key.StartsWith("playerCustomizationJson") || string.IsNullOrEmpty(value)) return;
        try
        {
            var clean = Sanitize(value);
            if (clean == null) return;
            Store()[clean] = value;
            SaveStore();
            Plugin.L.LogInfo($"[COSMETIC] {key} : version sans le mod écrite sur le disque, objets du mod gardés à part");
            value = clean;
        }
        catch (Exception e) { Plugin.L.LogError("[COSMETIC] sauvegarde : " + e); }
    }

    [HarmonyPatch(typeof(PlayerPrefs), nameof(PlayerPrefs.GetString), typeof(string), typeof(string))]
    [HarmonyPostfix]
    static void AfterGetString2(string key, ref string __result) => Restore(key, ref __result);

    [HarmonyPatch(typeof(PlayerPrefs), nameof(PlayerPrefs.GetString), typeof(string))]
    [HarmonyPostfix]
    static void AfterGetString1(string key, ref string __result) => Restore(key, ref __result);

    static void Restore(string key, ref string result)
    {
        if (key == null || !key.StartsWith("playerCustomizationJson") || string.IsNullOrEmpty(result)) return;
        // La sauvegarde du jeu est encore celle qu'on a écrite : on rend la version complète. Modifiée sans le mod :
        // elle ne correspond plus, le choix fait sans le mod l'emporte.
        if (Store().TryGetValue(result, out var full)) result = full;
    }
}
