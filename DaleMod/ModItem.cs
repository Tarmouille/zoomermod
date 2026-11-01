using System;
using System.Collections.Generic;
using Mirror;
using Props.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaleMod;

// Objet réseau custom : copie d'un consommable du jeu (le soda : prise en main, consommation, poubelle),
// renommé, avec un modèle .obj d'Assets (un sous-maillage coloré par groupe). Même assetId chez tous les joueurs moddés.
class ModItem
{
    const string SourceName = "Soda";
    static readonly List<ModItem> all = new();

    public readonly string Name;
    readonly uint assetId;
    readonly string objFile;
    readonly Vector3 scale;
    readonly Dictionary<string, Color> colors;
    public Action<GameObject> OnBuilt;           // optionnel : réglages en plus sur le modèle
    public bool ResetChildRotation;              // le modèle du soda est tourné pour tenir debout ; nos .obj sont déjà dans le bon sens
    public Dictionary<string, string> Textures;   // optionnel : groupe -> image d'Assets (sinon couleur unie)
    public bool SitOnPivot;                      // fond du modèle sur le pivot (celui du soda est au fond de la canette) + collision ajustée
    public bool NeverDespawn;                    // reste par terre indéfiniment (le soda disparaît après un temps d'inactivité)
    GameObject template;

    public ModItem(string name, uint assetId, string objFile, Vector3 scale, Dictionary<string, Color> colors)
    {
        Name = name; this.assetId = assetId; this.objFile = objFile; this.scale = scale; this.colors = colors;
        all.Add(this);
    }

    public bool Is(Interactable it) => it != null && it.interactableName == Name;

    // Les prefabs enregistrés sont effacés quand Mirror s'arrête : on réenregistre à chaque session
    internal static void EnsureAllRegistered()
    {
        foreach (var i in all) i.EnsureRegistered();
    }

    void EnsureRegistered()
    {
        if (!NetworkClient.active && !NetworkServer.active) return;
        if (NetworkClient.prefabs.ContainsKey(assetId)) return;
        if (template == null && (template = BuildTemplate()) == null) return;
        NetworkClient.RegisterPrefab(template, assetId);
        Plugin.L.LogInfo($"[ITEM] {Name} : prefab enregistré");
    }

    static GameObject FindSource()
    {
        foreach (var p in NetworkManager.singleton.spawnPrefabs)
        {
            var c = p == null ? null : p.GetComponent<Consumable>();
            if (c != null && c.interactableName == SourceName) return p;
        }
        return null;
    }

    GameObject BuildTemplate()
    {
        var source = FindSource();
        if (source == null) { Plugin.L.LogWarning($"[ITEM] {Name} : prefab source introuvable"); return null; }

        // Copie inactive, pour ne pas déclencher Awake/OnEnable sur le modèle
        bool wasActive = source.activeSelf;
        source.SetActive(false);
        var go = Object.Instantiate(source);
        source.SetActive(wasActive);
        Object.DontDestroyOnLoad(go);
        go.name = "DaleMod_" + Name.Replace(" ", "");

        go.GetComponent<NetworkIdentity>()._assetId = assetId;
        var c = go.GetComponent<Consumable>();
        c.interactableName = Name;
        c.label = Name;
        if (NeverDespawn) c.despawnAfterIdle = false;
        ApplyModel(go);
        try { OnBuilt?.Invoke(go); }
        catch (Exception e) { Plugin.L.LogError($"[ITEM] {Name} : {e}"); }
        return go;
    }

    // Modèle .obj (cube unité, mis à l'échelle ici) ; repli sur un cube blanc
    void ApplyModel(GameObject go)
    {
        var mesh = ModAssets.LoadObj(objFile, out var groups);
        if (mesh == null)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(cube);
            groups = new List<string> { "default" };
        }

        bool first = true;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            var r = mf.GetComponent<MeshRenderer>();
            if (!first) { if (r != null) r.enabled = false; continue; }   // le modèle source peut avoir plusieurs pièces
            first = false;
            mf.sharedMesh = mesh;
            mf.transform.localScale = scale;
            Plugin.L.LogInfo($"[ITEM] {Name} : pièce '{mf.name}' rotation d'origine {mf.transform.localEulerAngles}");
            if (ResetChildRotation) mf.transform.localRotation = Quaternion.identity;
            if (SitOnPivot) FitToPivot(go, mf, mesh);
            if (r == null) continue;
            var baseMat = r.sharedMaterial;
            var mats = new Material[groups.Count];
            for (int i = 0; i < mats.Length; i++)
            {
                var m = new Material(baseMat);
                Texture2D tex = null;
                if (Textures != null && Textures.TryGetValue(groups[i], out var file))
                    try { tex = ModAssets.LoadTexture(file); }
                    catch (Exception e) { Plugin.L.LogError($"[ITEM] {Name} : texture {file} : {e}"); }
                var c = tex != null ? Color.white : colors.TryGetValue(groups[i], out var col) ? col : Color.white;
                m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                mats[i] = m;
            }
            r.sharedMaterials = mats;
        }
    }

    // Remonte la pièce pour que le fond du modèle soit au niveau du pivot, et cale une BoxCollider sur le modèle
    void FitToPivot(GameObject go, MeshFilter mf, Mesh mesh)
    {
        var b = mesh.bounds;
        var lift = -b.min.y * scale.y;
        mf.transform.localPosition = new Vector3(mf.transform.localPosition.x, lift, mf.transform.localPosition.z);
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
        {
            Plugin.L.LogInfo($"[ITEM] {Name} : collision {col.GetIl2CppType().Name} sur '{col.name}'");
            var box = col.TryCast<BoxCollider>();
            if (box == null || box.transform != go.transform) continue;
            box.center = new Vector3(0f, b.size.y * scale.y / 2f, 0f);
            box.size = Vector3.Scale(b.size, scale);
        }
        Plugin.L.LogInfo($"[ITEM] {Name} : modèle remonté de {lift * 100f:0.0} cm");
    }

    // --- Côté serveur ---
    public GameObject Spawn(Vector3 pos)
    {
        if (!NetworkServer.active) { Plugin.L.LogWarning($"[ITEM] {Name} : apparition réservée au serveur"); return null; }
        EnsureRegistered();
        if (template == null) return null;
        var go = Object.Instantiate(template, pos, Quaternion.identity);
        go.SetActive(true);
        var c = go.GetComponent<Consumable>();
        if (NeverDespawn) c.despawnAfterIdle = false;   // la disparition est décidée par le serveur
        c.NetworkinteractableName = Name;   // SyncVars : synchronisés vers les clients
        c.Networklabel = Name;
        NetworkServer.Spawn(go, assetId, null);
        return go;
    }

    // Comme le paquet de clopes pris au bureau : apparaît puis va directement dans la main du joueur
    public GameObject GiveTo(NetworkIdentity player)
    {
        var go = Spawn(player.transform.position + Vector3.up * 1.2f);
        if (go == null) return null;
        go.GetComponent<Consumable>().ServerUse(player);
        Plugin.L.LogInfo($"[ITEM] {Name} donné en main");
        return go;
    }

    // Fait disparaître l'exemplaire tenu par ce joueur ; false s'il n'en tient pas.
    // Passe par la consommation du jeu : détruire un objet tenu laisse la main "occupée" (plus rien ne se ramasse).
    public bool ServerRemoveHeldBy(NetworkIdentity player)
    {
        Consumable nearest = null;
        float best = 2.5f;
        foreach (var c in Object.FindObjectsOfType<Consumable>())
        {
            if (!Is(c)) continue;
            if (HeldBy(c, player)) { nearest = c; break; }
            // repli : le serveur ne voit pas toujours l'objet "tenu" ; on prend l'exemplaire le plus proche du joueur
            float d = Vector3.Distance(c.transform.position, player.transform.position);
            if (d < best) { best = d; nearest = c; }
        }
        Plugin.L.LogInfo($"[ITEM] {Name} : retrait demandé -> {(nearest == null ? "aucun trouvé" : $"tenu={HeldBy(nearest, player)}")}");
        if (nearest == null) return false;
        // Partie serveur de la consommation, au nom du joueur qui tient l'objet (comme s'il l'avait consommé lui-même :
        // main libérée proprement). Consume() est l'action côté joueur : appelée ici, elle partait au nom de l'hôte
        // et le jeu la rejetait pour un autre joueur.
        try { nearest.UserCode_CmdConsume__NetworkIdentity__NetworkConnectionToClient(player, player.connectionToClient); }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"[ITEM] {Name} : consommation serveur impossible ({e.Message}), destruction directe");
            nearest.ServerDestroyCollectible();
        }
        return true;
    }

    // --- Partout ---
    public static bool HeldBy(Collectible c, NetworkIdentity player) =>
        player != null && (c.usingPlayerController == player
                           || (c.currentHolder != null && c.currentHolder.GetComponentInParent<NetworkIdentity>() == player));

    // Nombre d'exemplaires présents dans la scène (par terre, en main, posés), quel que soit le joueur
    public static int CountInScene(params ModItem[] items)
    {
        int n = 0;
        foreach (var c in Object.FindObjectsOfType<Consumable>())
            foreach (var i in items)
                if (i.Is(c)) { n++; break; }
        return n;
    }

    // Plafond du jeu : celui du frigo / placard de cuisine (maxItemCount), le même que pour les mugs et les bières
    public static int GameLimit()
    {
        var k = Fridge();
        return k == null ? 3 : k.maxItemCount;
    }

    // Les KitchenCabinetController sont aussi les placards, casiers et blocs de post-it : le frigo s'appelle "Fridge"
    public const string FridgeName = "Fridge";
    public static KitchenCabinetController Fridge()
    {
        foreach (var k in Object.FindObjectsOfType<KitchenCabinetController>())
            if (k.interactableName == FridgeName) return k;
        return null;
    }

    // Diagnostic : plafonds des meubles et compteurs du jeu
    internal static string LogLimits()
    {
        var log = Plugin.L;
        foreach (var k in Object.FindObjectsOfType<KitchenCabinetController>())
            log.LogInfo($"[LIMITE] cuisine '{k.name}' ({k.interactableName}) max={k.maxItemCount} sortis={k.items?.Count}");
        foreach (var b in Object.FindObjectsOfType<StorageBox>())
            log.LogInfo($"[LIMITE] boîte '{b.name}' max={b.maxItemCount}");
        foreach (var v in Object.FindObjectsOfType<Props.VendingMachine.VendingMachine>())
            log.LogInfo($"[LIMITE] distributeur '{v.name}' max={v.maxItemCount}");
        var counts = GameManagement.GameManager.instance?.collectibleCounts;
        if (counts != null)
            foreach (var kv in counts) log.LogInfo($"[LIMITE] en jeu : {kv.Key} = {kv.Value}");
        foreach (var i in all) log.LogInfo($"[LIMITE] mod : {i.Name} = {CountInScene(i)} (plafond {GameLimit()})");
        return "Limites écrites dans le log";
    }

    public bool LocalHolds()
    {
        var me = NetworkClient.localPlayer;
        if (me == null) return false;
        foreach (var c in Object.FindObjectsOfType<Consumable>())
            if (Is(c) && HeldBy(c, me)) return true;
        return false;
    }
}
