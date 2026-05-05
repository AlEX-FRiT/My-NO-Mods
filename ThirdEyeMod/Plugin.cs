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
    internal static ConfigEntry<float> CameraAngle;

    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("Third Eye Mod is loaded!");

        Enabled = Config.Bind("General", "Enabled", true, "Enable third-person camera mod");

        CameraDistance = Config.Bind("Camera", "Distance", 30f,
            new ConfigDescription("Camera distance from aircraft",
                new AcceptableValueRange<float>(5f, 200f)));
        CameraAngle = Config.Bind("Camera", "Angle", 15f,
            new ConfigDescription("Camera pitch angle below horizontal",
                new AcceptableValueRange<float>(0f, 90f)));

        _harmony = new Harmony("nuclearoption.thirdeyemod");
        _harmony.PatchAll();
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
