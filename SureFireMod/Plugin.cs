using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace SureFireMod;

[BepInPlugin("nuclearoption.surefiremod", "SureFire Mod", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    internal static ConfigEntry<bool> Enabled;
    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("SureFire Mod 1.0.0 loading...");

        Enabled = Config.Bind("General", "Enabled", true, "Enable laser-guidance confirm check");

        _harmony = new Harmony("nuclearoption.surefiremod");
        _harmony.PatchAll();

        Logger.LogInfo("SureFire Mod loaded - laser check enabled");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
