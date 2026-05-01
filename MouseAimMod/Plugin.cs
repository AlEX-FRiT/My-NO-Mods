using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace MouseAimMod;

[BepInPlugin("nuclearoption.mouseaimmod", "Mouse Aim Mod", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    internal static ConfigEntry<float> PitchP;
    internal static ConfigEntry<float> PitchI;
    internal static ConfigEntry<float> PitchD;
    internal static ConfigEntry<float> RollP;
    internal static ConfigEntry<float> RollI;
    internal static ConfigEntry<float> RollD;
    internal static ConfigEntry<float> YawP;
    internal static ConfigEntry<float> YawI;
    internal static ConfigEntry<float> YawD;
    internal static ConfigEntry<bool> MouseAimEnabled;

    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("Mouse Aim Mod is loaded!");

        PitchP = Config.Bind("PID - Pitch", "P", 0.025f, "Pitch PID proportional gain");
        PitchI = Config.Bind("PID - Pitch", "I", 0.01f,  "Pitch PID integral gain");
        PitchD = Config.Bind("PID - Pitch", "D", 0.005f, "Pitch PID derivative gain");

        RollP = Config.Bind("PID - Roll", "P", 0.03f,  "Roll PID proportional gain");
        RollI = Config.Bind("PID - Roll", "I", 0.005f, "Roll PID integral gain");
        RollD = Config.Bind("PID - Roll", "D", 0.002f, "Roll PID derivative gain");

        YawP = Config.Bind("PID - Yaw", "P", 0.1f,  "Yaw PID proportional gain");
        YawI = Config.Bind("PID - Yaw", "I", 0.1f,  "Yaw PID integral gain");
        YawD = Config.Bind("PID - Yaw", "D", 0.05f, "Yaw PID derivative gain");

        MouseAimEnabled = Config.Bind("General", "MouseAimEnabled", true, "Enable mouse aim");

        _ = PilotPlayerStatePatch.PitchPID;

        ApplyPidConfig();

        PitchP.SettingChanged += (_, _) => ApplyPidConfig();
        PitchI.SettingChanged += (_, _) => ApplyPidConfig();
        PitchD.SettingChanged += (_, _) => ApplyPidConfig();
        RollP.SettingChanged  += (_, _) => ApplyPidConfig();
        RollI.SettingChanged  += (_, _) => ApplyPidConfig();
        RollD.SettingChanged  += (_, _) => ApplyPidConfig();
        YawP.SettingChanged   += (_, _) => ApplyPidConfig();
        YawI.SettingChanged   += (_, _) => ApplyPidConfig();
        YawD.SettingChanged   += (_, _) => ApplyPidConfig();
        MouseAimEnabled.SettingChanged += (_, _) => ApplyPidConfig();

        _harmony = new Harmony("nuclearoption.mouseaimmod");
        _harmony.PatchAll();
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    internal static void ApplyPidConfig()
    {
        if (PitchP == null) return;

        PilotPlayerStatePatch.PitchPID.pFactor = PitchP.Value;
        PilotPlayerStatePatch.PitchPID.iFactor = PitchI.Value;
        PilotPlayerStatePatch.PitchPID.dFactor = PitchD.Value;

        PilotPlayerStatePatch.RollPID.pFactor = RollP.Value;
        PilotPlayerStatePatch.RollPID.iFactor = RollI.Value;
        PilotPlayerStatePatch.RollPID.dFactor = RollD.Value;

        PilotPlayerStatePatch.YawPID.pFactor = YawP.Value;
        PilotPlayerStatePatch.YawPID.iFactor = YawI.Value;
        PilotPlayerStatePatch.YawPID.dFactor = YawD.Value;

        CameraAimState.MouseAimEnabled = MouseAimEnabled.Value;
    }
}
