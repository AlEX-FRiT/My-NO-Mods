using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CameraStabilizerMod;

[BepInPlugin("nuclearoption.camerastabilizermod", "Camera Stabilizer Mod", "0.1.0")]
[BepInProcess("NuclearOption.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    internal static ConfigEntry<bool> Enabled;

    public static bool IsEnabled => Enabled != null && Enabled.Value;

    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("Camera Stabilizer Mod is loaded!");

        Enabled = Config.Bind("General", "Enabled", true,
            "When enabled in cockpit view, lock the view to a fixed world angle (horizontal + pitch).");

        _harmony = new Harmony("nuclearoption.camerastabilizermod");
        _harmony.PatchAll();
    }

    private void OnDestroy() { _harmony?.UnpatchSelf(); }
}
