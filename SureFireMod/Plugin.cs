using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace SureFireMod;

[BepInPlugin("nuclearoption.surefiremod", "SureFire Mod", "1.0.0")]
[BepInProcess("NuclearOption.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> RequireLased;
    internal static ConfigEntry<bool> RequireArc;
    internal static ConfigEntry<bool> SortByAngle;
    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("SureFire Mod 1.0.0 loading...");

        Enabled = Config.Bind("General", "Enabled", true, "Enable laser-guidance confirm check");
        RequireLased = Config.Bind("General", "RequireLased", true, "Block launch if target is not lased");
        RequireArc = Config.Bind("General", "RequireArc", true, "Block launch if target is out of firing arc");
        SortByAngle = Config.Bind("General", "SortByAngle", true, "Re-sort laser targets by angle from nose on each fire");

        _harmony = new Harmony("nuclearoption.surefiremod");
        _harmony.PatchAll();

        Logger.LogInfo("SureFire Mod loaded - laser check enabled");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
