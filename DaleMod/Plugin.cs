using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace DaleMod;

[BepInPlugin("dalemod", "DaleMod", Version)]
public class Plugin : BasePlugin
{
    internal const string Name = "ZoomerMod", Version = "0.6.3";   // affichés sur le menu principal (DebugMenu.DrawBadge)
    internal static ManualLogSource L;

    public override void Load()
    {
        L = Log;
        Harmony.CreateAndPatchAll(typeof(SpyPatches));
        var h = Harmony.CreateAndPatchAll(typeof(CustomTaskPatches));
        CustomTaskPatches.PatchCarriers(h);
        Harmony.CreateAndPatchAll(typeof(CustomTaskPatches.LocalizePatch));
        Harmony.CreateAndPatchAll(typeof(DebugMenuPatches));
        Harmony.CreateAndPatchAll(typeof(CokeBagPatches));
        Harmony.CreateAndPatchAll(typeof(DeskBagPatches));
        Harmony.CreateAndPatchAll(typeof(IncidentPatches));
        Harmony.CreateAndPatchAll(typeof(TaskHookObjectsPatches));
        Harmony.CreateAndPatchAll(typeof(NauseaPatches));
        Harmony.CreateAndPatchAll(typeof(Caca));
        Harmony.CreateAndPatchAll(typeof(PcMod));
        Harmony.CreateAndPatchAll(typeof(NetSignal));
        Harmony.CreateAndPatchAll(typeof(BossMail));
        Harmony.CreateAndPatchAll(typeof(InkCoffee));
        InkCoffee.Register();
        Harmony.CreateAndPatchAll(typeof(Tupperware));
        Tupperware.Register();
        _ = CokeBag.Item;   // objets réseau custom : créés dès le chargement (enregistrés ensuite à chaque session)
        Caca.Register();
        ServerBlink.Register();
        Incident.Register();
        Harmony.CreateAndPatchAll(typeof(FireExPrank));
        FireExPrank.Register();
        Harmony.CreateAndPatchAll(typeof(Windmill));
        Harmony.CreateAndPatchAll(typeof(Zoomer));
        Zoomer.Register();
        Harmony.CreateAndPatchAll(typeof(ColdShower));
        ColdShower.Register();
        Harmony.CreateAndPatchAll(typeof(WaterDamage));
        WaterDamage.Register();
        Harmony.CreateAndPatchAll(typeof(PowerNap));
        PowerNap.Register();
        Harmony.CreateAndPatchAll(typeof(ChainMail));
        Harmony.CreateAndPatchAll(typeof(TaskPools));
        Harmony.CreateAndPatchAll(typeof(Autograph));
        Harmony.CreateAndPatchAll(typeof(FridgeCleanup));
        FridgeCleanup.Register();   // crée aussi les objets réseau des aliments périmés
        Harmony.CreateAndPatchAll(typeof(SafetyCheck));
        Harmony.CreateAndPatchAll(typeof(CyberAudit));
        PopupSpam.Register();
        Harmony.CreateAndPatchAll(typeof(PopupSpam));
        Harmony.CreateAndPatchAll(typeof(ServerBackup));
        ServerBackup.Register();
        Harmony.CreateAndPatchAll(typeof(UnionFlyer));
        UnionFlyer.Register();
        Harmony.CreateAndPatchAll(typeof(SupplyOrder));
        SupplyOrder.Register();
        Harmony.CreateAndPatchAll(typeof(ClientPresentation));
        Harmony.CreateAndPatchAll(typeof(ButtCopy));
        ButtCopy.Register();
        Harmony.CreateAndPatchAll(typeof(StaplerTrap));
        StaplerTrap.Register();
        Harmony.CreateAndPatchAll(typeof(Karaoke));
        Karaoke.Register();

        ClassInjector.RegisterTypeInIl2Cpp<DebugMenu>();
        AddComponent<DebugMenu>();
#if DEBUG
        L.LogInfo($"{Name} {Version} chargé — F7 : menu de debug");
#else
        L.LogInfo($"{Name} {Version} chargé");
#endif
    }
}
