using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ThirdEyeMod;

[BepInPlugin("nuclearoption.thirdeyemod", "Third Eye Mod", "1.0.0")]
[BepInProcess("NuclearOption.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<float> CameraDistance;
    internal static ConfigEntry<float> VerticalCurve;
    internal static ConfigEntry<float> HorizontalCurve;

    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("Third Eye Mod is loaded!");

        Enabled = Config.Bind("General", "Enabled", true, "Enable third-person camera mod");

        CameraDistance = Config.Bind("Camera", "Distance", 30f,
            new ConfigDescription("Camera distance from aircraft",
                new AcceptableValueRange<float>(5f, 200f)));
        VerticalCurve = Config.Bind("Camera", "VerticalCurve", 1.8f,
            new ConfigDescription("Vertical height curve power: <1=bottom-flat, >1=top-flat",
                new AcceptableValueRange<float>(0.1f, 5f)));
        HorizontalCurve = Config.Bind("Camera", "HorizontalCurve", 0.6f,
            new ConfigDescription("Horizontal distance curve: <1=forward-bias, >1=rear-bias",
                new AcceptableValueRange<float>(0.1f, 5f)));

        _harmony = new Harmony("nuclearoption.thirdeyemod");
        _harmony.PatchAll();
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
