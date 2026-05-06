using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace MouseAimMod;

[BepInPlugin("nuclearoption.mouseaimmod", "Mouse Aim Mod", "1.0.0")]
[BepInProcess("NuclearOption.exe")]
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
    internal static ConfigEntry<bool> InvertFreeLook;
    internal static ConfigEntry<float> HoverExitAngle;

    internal static ConfigEntry<bool> RollCentering;
    internal static ConfigEntry<float> CenteringRange;
    internal static ConfigEntry<float> CenteringGain;

    internal static ConfigEntry<float> PitchScale;
    internal static ConfigEntry<float> RollScale;
    internal static ConfigEntry<float> YawScale;
    internal static ConfigEntry<float> PitchILimit;
    internal static ConfigEntry<float> RollILimit;
    internal static ConfigEntry<float> YawILimit;
    internal static ConfigEntry<float> PitchIThreshold;
    internal static ConfigEntry<float> RollIThreshold;
    internal static ConfigEntry<float> YawIThreshold;

    internal static ConfigEntry<float> YawAttenStart;
    internal static ConfigEntry<float> YawAttenEnd;
    internal static ConfigEntry<bool> RollYawBalance;

    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("Mouse Aim Mod is loaded!");

        PitchP = Config.Bind("PID - Pitch", "P", 0.03f, "Pitch PID proportional gain");
        PitchI = Config.Bind("PID - Pitch", "I", 0.01f,  "Pitch PID integral gain");
        PitchD = Config.Bind("PID - Pitch", "D", 0.005f, "Pitch PID derivative gain");
        PitchILimit = Config.Bind("Pitch - Modify", "ILimit", 2f,
            new ConfigDescription("Pitch integral output clamp",
                new AcceptableValueRange<float>(0f, 2f)));
        PitchIThreshold = Config.Bind("Pitch - Modify", "IThr", 180f,
            new ConfigDescription("Pitch error (deg) above which integral is zeroed",
                new AcceptableValueRange<float>(0.5f, 180f)));
        PitchScale = Config.Bind("Pitch - Modify", "Scale", 0.4f,
            new ConfigDescription("Pitch output scale",
                new AcceptableValueRange<float>(0f, 1f)));

        RollP = Config.Bind("Roll - PID", "P", 0.03f,  "Roll PID proportional gain");
        RollI = Config.Bind("Roll - PID", "I", 0.005f, "Roll PID integral gain");
        RollD = Config.Bind("Roll - PID", "D", 0.002f, "Roll PID derivative gain");
        RollILimit = Config.Bind("Roll - Modify", "ILimit", 2f,
            new ConfigDescription("Roll integral output clamp",
                new AcceptableValueRange<float>(0f, 2f)));
        RollIThreshold = Config.Bind("Roll - Modify", "IThr", 180f,
            new ConfigDescription("Roll error (deg) above which integral is zeroed",
                new AcceptableValueRange<float>(0.5f, 180f)));
        RollScale = Config.Bind("Roll - Modify", "Scale", 0.4f,
            new ConfigDescription("Roll output scale",
                new AcceptableValueRange<float>(0f, 1f)));

        YawP = Config.Bind("Yaw - PID", "P", 0.1f,  "Yaw PID proportional gain");
        YawI = Config.Bind("Yaw - PID", "I", 0.1f,  "Yaw PID integral gain");
        YawD = Config.Bind("Yaw - PID", "D", 0.05f, "Yaw PID derivative gain");
        YawILimit = Config.Bind("Yaw - Modify", "ILimit", 2f,
            new ConfigDescription("Yaw integral output clamp",
                new AcceptableValueRange<float>(0f, 2f)));
        YawIThreshold = Config.Bind("Yaw - Modify", "IThr", 180f,
            new ConfigDescription("Yaw error (deg) above which integral is zeroed",
                new AcceptableValueRange<float>(0.5f, 180f)));
        YawScale = Config.Bind("Yaw - Modify", "Scale", 0.4f,
            new ConfigDescription("Yaw output scale",
                new AcceptableValueRange<float>(0f, 1f)));

        RollYawBalance = Config.Bind("Roll/Yaw Balance", "Enable", false, "Enable roll/yaw balance attenuation");
        YawAttenStart = Config.Bind("Roll/Yaw Balance", "AttenStart", 30f,
            new ConfigDescription("Total view deviation (deg) where yaw reduction begins",
                new AcceptableValueRange<float>(0f, 180f)));
        YawAttenEnd = Config.Bind("Roll/Yaw Balance", "AttenEnd", 60f,
            new ConfigDescription("Total view deviation (deg) where yaw reaches zero",
                new AcceptableValueRange<float>(0f, 180f)));

        MouseAimEnabled = Config.Bind("General", "MouseAimEnabled", true, "Enable mouse aim");
        InvertFreeLook = Config.Bind("General", "InvertFreeLook", false, "When true, mouse aim is active only while Free Look is held");
        RollCentering = Config.Bind("Roll Centering", "RollCentering", false, "Auto roll back to level when camera is near center");
        CenteringRange = Config.Bind("Roll Centering", "CenteringRange", 15f,
            new ConfigDescription("Horizontal deviation range for roll centering (degrees)",
                new AcceptableValueRange<float>(0.5f, 20f)));
        CenteringGain = Config.Bind("Roll Centering", "CenteringGain", 0.3f,
            new ConfigDescription("Roll centering strength gain",
                new AcceptableValueRange<float>(0f, 1f)));
        HoverExitAngle = Config.Bind("Hover", "ExitAngle", 20f,
            new ConfigDescription("Pitch or roll angle exceeding this disables hover throttle",
                new AcceptableValueRange<float>(5f, 60f)));

        PilotPlayerStatePatch.PitchPID = new PID(PitchP.Value, PitchI.Value, PitchD.Value);
        PilotPlayerStatePatch.RollPID  = new PID(RollP.Value, RollI.Value, RollD.Value);
        PilotPlayerStatePatch.YawPID   = new PID(YawP.Value, YawI.Value, YawD.Value);

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

        PilotPlayerStatePatch.PitchPID.Reseti();
        PilotPlayerStatePatch.RollPID.Reseti();
        PilotPlayerStatePatch.YawPID.Reseti();
    }
}
