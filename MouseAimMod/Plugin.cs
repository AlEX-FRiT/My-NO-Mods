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

    internal static ConfigEntry<bool> MouseAimEnabled;
    internal static ConfigEntry<bool> InvertFreeLook;
    internal static ConfigEntry<float> HoverExitAngle;
    internal static ConfigEntry<bool> StabilityKbEnabled;

    internal static ConfigEntry<bool> RollCentering;
    internal static ConfigEntry<float> CenteringRange;
    internal static ConfigEntry<float> CenteringGain;

    internal static ConfigEntry<float> ErrorExp;

    internal static ConfigEntry<float> MpcK;
    internal static ConfigEntry<float> MpcPenalty;
    internal static ConfigEntry<int> MpcHorizon;
    internal static ConfigEntry<int> MpcIter;
    internal static ConfigEntry<float> MpcScale;
    internal static ConfigEntry<int> SaturationHold;

    internal static ConfigEntry<float> YawAttenStart;
    internal static ConfigEntry<float> YawAttenEnd;
    internal static ConfigEntry<bool> RollYawBalance;

    internal static ConfigEntry<PresetSlot> ActivePreset;
    internal static ConfigEntry<bool> SavePreset;
    internal static ConfigEntry<bool> LoadPreset;

    private static bool _debugAvailable;
    private static object _pitchErrStream, _pitchOutStream;
    private static object _rollErrStream, _rollOutStream;
    private static object _yawErrStream, _yawOutStream;
    private static object _fbwInStream, _fbwOutStream;
    private static object _paStream;
    private static MethodInfo _debugPushMethod;

    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("Mouse Aim Mod is loaded!");

        MouseAimEnabled = Config.Bind("General", "MouseAimEnabled", true, "Enable mouse aim");
        InvertFreeLook = Config.Bind("General", "InvertFreeLook", false, "When true, mouse aim is active only while Free Look is held");
        StabilityKbEnabled = Config.Bind("General", "StabilityKill", false, "Temporarily disable flight assist while keyboard pitch is active");
        ErrorExp = Config.Bind("MPC", "ErrorExp", 0.9f,
            new ConfigDescription("Error power exponent. 1=linear, >1=suppress small errors",
                new AcceptableValueRange<float>(0.5f, 2f)));

        MpcK = Config.Bind("MPC", "K", 50f,
            new ConfigDescription("K=100→87%/frame, K=50→63%/frame, K=10→18%/frame, K=1→2%/frame toward target angular rate",
                new AcceptableValueRange<float>(0f, 100f)));
        MpcPenalty = Config.Bind("MPC", "Penalty", 3f,
            new ConfigDescription("Overshoot penalty multiplier",
                new AcceptableValueRange<float>(0f, 10f)));
        MpcHorizon = Config.Bind("MPC", "Horizon", 50,
            new ConfigDescription("Look-ahead frames. K × Horizon × 0.02 should be ≥ 3 to avoid myopic oscillation",
                new AcceptableValueRange<int>(0, 100)));
        MpcIter = Config.Bind("MPC", "Iterations", 10,
            new ConfigDescription("Golden-section search iterations",
                new AcceptableValueRange<int>(3, 50)));
        MpcScale = Config.Bind("MPC", "Scale", 1f,
            new ConfigDescription("Output scale applied before clamp to FBW",
                new AcceptableValueRange<float>(0f, 2f)));
        SaturationHold = Config.Bind("MPC", "SaturationHold", 25,
            new ConfigDescription("Pause MPC output when FBW is saturated, for this many frames",
                new AcceptableValueRange<int>(0, 200)));

        RollYawBalance = Config.Bind("Roll/Yaw Balance", "Enable", false, "Enable roll/yaw balance attenuation");
        YawAttenStart = Config.Bind("Roll/Yaw Balance", "AttenStart", 30f,
            new ConfigDescription("Total view deviation (deg) where yaw reduction begins",
                new AcceptableValueRange<float>(0f, 180f)));
        YawAttenEnd = Config.Bind("Roll/Yaw Balance", "AttenEnd", 60f,
            new ConfigDescription("Total view deviation (deg) where yaw reaches zero",
                new AcceptableValueRange<float>(0f, 180f)));

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

        MouseAimEnabled.SettingChanged += (_, _) => { CameraAimState.MouseAimEnabled = MouseAimEnabled.Value; };

        _harmony = new Harmony("nuclearoption.mouseaimmod");
        _harmony.PatchAll();
    }

    private void Start() { InitDebugCharts(); }
    private void OnDestroy() { _harmony?.UnpatchSelf(); }

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
            var yawChart = createChart.Invoke(null, new[] { flowVal, "Yaw", 480f, 200f, -1f, 1f, 600, null, null });
            var addStream = errChart.GetType().GetMethod("AddStream");
            _pitchErrStream = addStream.Invoke(errChart, new object[] { "Err", Color.green });
            _pitchOutStream = addStream.Invoke(errChart, new object[] { "Out", Color.yellow });
            _rollErrStream = addStream.Invoke(outChart, new object[] { "Err", Color.green });
            _rollOutStream = addStream.Invoke(outChart, new object[] { "Out", Color.yellow });
            _yawErrStream = addStream.Invoke(yawChart, new object[] { "Err", Color.green });
            _yawOutStream = addStream.Invoke(yawChart, new object[] { "Out", Color.yellow });

            var fbwChart = createChart.Invoke(null, new[] { flowVal, "FBW Pitch", 480f, 200f, -1f, 1f, 600, null, null });
            var addF = fbwChart.GetType().GetMethod("AddStream");
            _fbwInStream  = addF.Invoke(fbwChart, new object[] { "In", Color.green });
            _fbwOutStream = addF.Invoke(fbwChart, new object[] { "Out", Color.yellow });

            var paChart = createChart.Invoke(null, new[] { flowVal, "PA", 480f, 200f, -2f, 2f, 600, null, null });
            _paStream = paChart.GetType().GetMethod("AddStream").Invoke(paChart, new object[] { "PA", new Color(1f, 0.4f, 0.4f) });
            _debugAvailable = true;
            Logger.LogInfo("DebugGraphMod charts registered");
        }
        catch (Exception ex) { Logger.LogInfo($"DebugGraphMod not available: {ex.Message}"); }
    }

    internal static void PushDebugData(float pE, float rE, float yE, float pO, float rO, float yO)
    {
        if (!_debugAvailable) return;
        try
        {
            if (_debugPushMethod == null) _debugPushMethod = _pitchErrStream.GetType().GetMethod("Push");
            _debugPushMethod.Invoke(_pitchErrStream, new object[] { pE });
            _debugPushMethod.Invoke(_pitchOutStream, new object[] { pO });
            _debugPushMethod.Invoke(_rollErrStream, new object[] { rE });
            _debugPushMethod.Invoke(_rollOutStream, new object[] { rO });
            _debugPushMethod.Invoke(_yawErrStream, new object[] { yE });
            _debugPushMethod.Invoke(_yawOutStream, new object[] { yO });
        }
        catch { _debugAvailable = false; }
    }

    internal static void PushDebugFbw(float inP, float outP, float pa)
    {
        if (!_debugAvailable) return;
        try
        {
            if (_debugPushMethod == null) _debugPushMethod = _pitchErrStream.GetType().GetMethod("Push");
            _debugPushMethod.Invoke(_fbwInStream, new object[] { inP });
            _debugPushMethod.Invoke(_fbwOutStream, new object[] { outP });
            _debugPushMethod.Invoke(_paStream, new object[] { pa });
        }
        catch { }
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
            foreach (var (k, v) in sec.Value) sw.WriteLine($"{k} = {v}");
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
            int eq = t.IndexOf('='); if (eq < 0) continue;
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
