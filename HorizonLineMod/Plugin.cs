using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace HorizonLineMod;

[BepInPlugin("nuclearoption.horizonlinemod", "Horizon Line Mod", "1.0.0")]
[BepInProcess("NuclearOption.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static ConfigEntry<bool> Enabled;
    private Harmony _harmony;

    private void Awake()
    {
        Enabled = Config.Bind("General", "Enabled", true, "Enable horizon line HUD overlay");

        Enabled.SettingChanged += (_, _) =>
        {
            if (!Enabled.Value)
                HorizonLineManager.DestroyCurrentLine();
            else
                HorizonLineManager.RecreateIfEnabled();
        };

        _harmony = new Harmony("nuclearoption.horizonlinemod");
        _harmony.PatchAll();

        Logger.LogInfo("HorizonLineMod 1.0.0 loaded");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}