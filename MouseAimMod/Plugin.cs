using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace MouseAimMod;

public enum PresetSlot { Slot1, Slot2, Slot3, Slot4, Slot5 }

[BepInPlugin("nuclearoption.mouseaimmod", "Mouse Aim Mod", "1.0.0")]
[BepInProcess("NuclearOption.exe")]
[BepInDependency("nuclearoption.debuggraphmod", BepInDependency.DependencyFlags.SoftDependency)]
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
    internal static ConfigEntry<float> PitchIThreshold;
    internal static ConfigEntry<float> RollIThreshold;
    internal static ConfigEntry<float> YawIThreshold;
    internal static ConfigEntry<float> PitchTrackingTc;
    internal static ConfigEntry<float> RollTrackingTc;
    internal static ConfigEntry<float> YawTrackingTc;
    internal static ConfigEntry<float> ErrorExp;

    internal static ConfigEntry<float> YawAttenStart;
    internal static ConfigEntry<float> YawAttenEnd;
    internal static ConfigEntry<bool> RollYawBalance;
    internal static ConfigEntry<bool> SchedEnabled;
    internal static ConfigEntry<float> SchedRefQ;
    internal static ConfigEntry<float> SchedExp;
    internal static ConfigEntry<float> SchedClampMin;
    internal static ConfigEntry<float> SchedClampMax;

    internal static ConfigEntry<PresetSlot> ActivePreset;
    internal static ConfigEntry<bool> SavePreset;
    internal static ConfigEntry<bool> LoadPreset;

    private static bool _debugAvailable;
    private static object _pitchErrStream, _pitchOutStream;
    private static object _rollErrStream, _rollOutStream;
    private static object _yawErrStream, _yawOutStream;
    private static MethodInfo _debugPushMethod;

    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("Mouse Aim Mod is loaded!");

        PitchP = Config.Bind("PID - Pitch", "P", 3.25f, "Pitch PID proportional gain (sin-unit input)");
        PitchI = Config.Bind("PID - Pitch", "I", 0.5f, "Pitch PID integral gain (sin-unit input)");
        PitchD = Config.Bind("PID - Pitch", "D", 1f, "Pitch PID derivative gain (sin-unit input)");
        PitchIThreshold = Config.Bind("Pitch - Modify", "IThr", 0.3f,
            new ConfigDescription("Pitch error above which integral is zeroed. Default > max sin, effectively disabled",
                new AcceptableValueRange<float>(0f, 1f)));
        PitchScale = Config.Bind("Pitch - Modify", "Scale", 1f,
            new ConfigDescription("Pitch output scale",
                new AcceptableValueRange<float>(0f, 1f)));
        PitchTrackingTc = Config.Bind("Pitch - Modify", "TrackingTc", 0.1f,
            new ConfigDescription("Pitch anti-windup tracking time constant (s). Smaller = faster unwind",
                new AcceptableValueRange<float>(0.01f, 10f)));

        RollP = Config.Bind("Roll - PID", "P", 2f,  "Roll PID proportional gain (sin-unit input)");
        RollI = Config.Bind("Roll - PID", "I", 0f, "Roll PID integral gain (sin-unit input)");
        RollD = Config.Bind("Roll - PID", "D", 1f, "Roll PID derivative gain (sin-unit input)");
        RollIThreshold = Config.Bind("Roll - Modify", "IThr", 0.3f,
            new ConfigDescription("Roll error above which integral is zeroed. Default > max sin, effectively disabled",
                new AcceptableValueRange<float>(0f, 1f)));
        RollScale = Config.Bind("Roll - Modify", "Scale", 1f,
            new ConfigDescription("Roll output scale",
                new AcceptableValueRange<float>(0f, 1f)));
        RollTrackingTc = Config.Bind("Roll - Modify", "TrackingTc", 0.1f,
            new ConfigDescription("Roll anti-windup tracking time constant (s). Smaller = faster unwind",
                new AcceptableValueRange<float>(0.01f, 10f)));

        YawP = Config.Bind("Yaw - PID", "P", 2f,  "Yaw PID proportional gain (sin-unit input)");
        YawI = Config.Bind("Yaw - PID", "I", 0.4f, "Yaw PID integral gain (sin-unit input)");
        YawD = Config.Bind("Yaw - PID", "D", 1f, "Yaw PID derivative gain (sin-unit input)");
        YawIThreshold = Config.Bind("Yaw - Modify", "IThr", 0.3f,
            new ConfigDescription("Yaw error above which integral is zeroed. Default > max sin, effectively disabled",
                new AcceptableValueRange<float>(0f, 1f)));
        YawScale = Config.Bind("Yaw - Modify", "Scale", 1f,
            new ConfigDescription("Yaw output scale",
                new AcceptableValueRange<float>(0f, 1f)));
        YawTrackingTc = Config.Bind("Yaw - Modify", "TrackingTc", 0.1f,
            new ConfigDescription("Yaw anti-windup tracking time constant (s). Smaller = faster unwind",
                new AcceptableValueRange<float>(0.01f, 10f)));
        ErrorExp = Config.Bind("General", "ErrorExp", 0.9f,
            new ConfigDescription("Error power exponent. 1=linear, >1=suppress small errors",
                new AcceptableValueRange<float>(0.5f, 2f)));

        RollYawBalance = Config.Bind("Roll/Yaw Balance", "Enable", false, "Enable roll/yaw balance attenuation");
        YawAttenStart = Config.Bind("Roll/Yaw Balance", "AttenStart", 30f,
            new ConfigDescription("Total view deviation (deg) where yaw reduction begins",
                new AcceptableValueRange<float>(0f, 180f)));
        YawAttenEnd = Config.Bind("Roll/Yaw Balance", "AttenEnd", 60f,
            new ConfigDescription("Total view deviation (deg) where yaw reaches zero",
                new AcceptableValueRange<float>(0f, 180f)));

        SchedEnabled = Config.Bind("Gain Schedule", "Enabled", false,
            new ConfigDescription("Scale PID gains by dynamic pressure. Disabled = flat gains for all altitudes"));
        SchedRefQ = Config.Bind("Gain Schedule", "RefQ", 18750f,
            new ConfigDescription("Reference dynamic pressure (Pa) where base gains were tuned. ~175 m/s at sea level",
                new AcceptableValueRange<float>(1000f, 100000f)));
        SchedExp = Config.Bind("Gain Schedule", "Exp", 0.3f,
            new ConfigDescription("Power-law exponent. 0 = no scaling, 0.3 = mild, 1.0 = linear compensation",
                new AcceptableValueRange<float>(0f, 2f)));
        SchedClampMin = Config.Bind("Gain Schedule", "ClampMin", 0.1f,
            new ConfigDescription("Minimum gain multiplier (safety floor)",
                new AcceptableValueRange<float>(0.01f, 1f)));
        SchedClampMax = Config.Bind("Gain Schedule", "ClampMax", 5f,
            new ConfigDescription("Maximum gain multiplier (safety ceiling)",
                new AcceptableValueRange<float>(1f, 20f)));

        MouseAimEnabled = Config.Bind("General", "MouseAimEnabled", true, "Enable mouse aim");
        InvertFreeLook = Config.Bind("General", "InvertFreeLook", false, "When true, mouse aim is active only while Free Look is held");
        RollCentering = Config.Bind("Roll Centering", "RollCentering", false, "Auto roll back to level when camera is near center");
        CenteringRange = Config.Bind("Roll Centering", "CenteringRange", 0.259f,
            new ConfigDescription("Horizontal deviation range for roll centering (sin units, ~15°)",
                new AcceptableValueRange<float>(0.01f, 0.35f)));
        CenteringGain = Config.Bind("Roll Centering", "CenteringGain", 0.3f,
            new ConfigDescription("Roll centering strength gain",
                new AcceptableValueRange<float>(0f, 1f)));
        HoverExitAngle = Config.Bind("Hover", "ExitAngle", 20f,
            new ConfigDescription("Pitch or roll angle exceeding this disables hover throttle",
                new AcceptableValueRange<float>(5f, 60f)));
        ActivePreset = Config.Bind("Presets", "ActivePreset", PresetSlot.Slot1, "Select preset slot for save/load.");
        SavePreset = Config.Bind("Presets", "SavePreset", false, "Toggle ON to save current config to active preset.");
        LoadPreset = Config.Bind("Presets", "LoadPreset", false, "Toggle ON to load config from active preset.");

        SavePreset.SettingChanged += (_, _) => { if (SavePreset.Value) { SaveToPreset(ActivePreset.Value); SavePreset.Value = false; } };
        LoadPreset.SettingChanged += (_, _) => { if (LoadPreset.Value) { LoadFromPreset(ActivePreset.Value); LoadPreset.Value = false; } };

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

    private void Start()
    {
        InitDebugCharts();
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

    private static string PresetPath(PresetSlot slot)
    {
        string dir = Path.Combine(Paths.ConfigPath, "nuclearoption.mouseaimmod_presets");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"preset{(int)slot + 1}.cfg");
    }

    private void InitDebugCharts()
    {
        try
        {
            var regType = Type.GetType("DebugGraphMod.GraphRegistry, DebugGraphMod");
            if (regType == null) return;

            var chartTypeEnum = Type.GetType("DebugGraphMod.ChartType, DebugGraphMod");
            var flowVal = Enum.Parse(chartTypeEnum, "Flow");
            var createChart = regType.GetMethod("CreateChart");

            var errChart = createChart.Invoke(null, new[] { flowVal, "Pitch", 480f, 200f, -1f, 1f, 600, null, null });
            var outChart = createChart.Invoke(null, new[] { flowVal, "Roll", 480f, 200f, -1f, 1f, 600, null, null });
            var yawChart  = createChart.Invoke(null, new[] { flowVal, "Yaw", 480f, 200f, -1f, 1f, 600, null, null });

            var addStream = errChart.GetType().GetMethod("AddStream");
            _pitchErrStream = addStream.Invoke(errChart, new object[] { "Err", Color.green });
            _pitchOutStream = addStream.Invoke(errChart, new object[] { "Out", Color.yellow });

            _rollErrStream = addStream.Invoke(outChart, new object[] { "Err", Color.green });
            _rollOutStream = addStream.Invoke(outChart, new object[] { "Out", Color.yellow });

            _yawErrStream = addStream.Invoke(yawChart, new object[] { "Err", Color.green });
            _yawOutStream = addStream.Invoke(yawChart, new object[] { "Out", Color.yellow });

            _debugAvailable = true;
            Logger.LogInfo("DebugGraphMod charts registered");
        }
        catch (Exception ex)
        {
            Logger.LogInfo($"DebugGraphMod not available: {ex.Message}");
        }
    }

    internal static void PushDebugData(float pitchErr, float rollErr, float yawErr, float pitchOut, float rollOut, float yawOut)
    {
        if (!_debugAvailable) return;
        try
        {
            if (_debugPushMethod == null)
                _debugPushMethod = _pitchErrStream.GetType().GetMethod("Push");
            _debugPushMethod.Invoke(_pitchErrStream, new object[] { pitchErr });
            _debugPushMethod.Invoke(_pitchOutStream, new object[] { pitchOut });
            _debugPushMethod.Invoke(_rollErrStream, new object[] { rollErr });
            _debugPushMethod.Invoke(_rollOutStream, new object[] { rollOut });
            _debugPushMethod.Invoke(_yawErrStream, new object[] { yawErr });
            _debugPushMethod.Invoke(_yawOutStream, new object[] { yawOut });
        }
        catch
        {
            _debugAvailable = false;
        }
    }

    private static void SaveToPreset(PresetSlot slot)
    {
        var fields = typeof(Plugin).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => typeof(ConfigEntryBase).IsAssignableFrom(f.FieldType));

        var sections = new Dictionary<string, List<(string key, string val)>>();
        foreach (var f in fields)
        {
            if (f.Name is "ActivePreset" or "SavePreset" or "LoadPreset") continue;
            var entry = (ConfigEntryBase)f.GetValue(null);
            if (entry == null) continue;
            var sec = entry.Definition.Section;
            if (!sections.ContainsKey(sec)) sections[sec] = new();
            sections[sec].Add((entry.Definition.Key, entry.GetSerializedValue() ?? ""));
        }
        using var sw = new StreamWriter(PresetPath(slot));
        foreach (var sec in sections.OrderBy(s => s.Key))
        {
            sw.WriteLine($"[{sec.Key}]");
            foreach (var (k, v) in sec.Value)
                sw.WriteLine($"{k} = {v}");
            sw.WriteLine();
        }
    }

    private static void LoadFromPreset(PresetSlot slot)
    {
        string path = PresetPath(slot);
        if (!File.Exists(path)) return;

        var values = new Dictionary<(string, string), string>();
        string sec = "";
        foreach (var line in File.ReadAllLines(path))
        {
            var t = line.Trim();
            if (t.StartsWith("[") && t.EndsWith("]")) { sec = t.Trim('[', ']'); continue; }
            int eq = t.IndexOf('=');
            if (eq < 0) continue;
            values[(sec, t.Substring(0, eq).Trim())] = t.Substring(eq + 1).Trim();
        }

        var fields = typeof(Plugin).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => typeof(ConfigEntryBase).IsAssignableFrom(f.FieldType));

        foreach (var f in fields)
        {
            if (f.Name is "ActivePreset" or "SavePreset" or "LoadPreset") continue;
            var entry = (ConfigEntryBase)f.GetValue(null);
            if (entry == null) continue;
            if (values.TryGetValue((entry.Definition.Section, entry.Definition.Key), out var val))
            {
                var boxed = TomlTypeConverter.ConvertToValue(val, entry.SettingType);
                if (boxed != null) entry.BoxedValue = boxed;
            }
        }
    }
}
