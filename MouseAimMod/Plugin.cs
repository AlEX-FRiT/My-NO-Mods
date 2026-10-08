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

[BepInPlugin("nuclearoption.mouseaimmod", "Mouse Aim Mod", "2.0.0")]
[BepInProcess("NuclearOption.exe")]
[BepInDependency("nuclearoption.debuggraphmod", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("nuclearoption.camerastabilizermod", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private static PropertyInfo _stabilizerEnabledProp;

    internal static bool StabilizerEnabled()
    {
        if (_stabilizerEnabledProp == null)
        {
            var t = Type.GetType("CameraStabilizerMod.Plugin, CameraStabilizerMod");
            _stabilizerEnabledProp = t?.GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Static);
        }
        if (_stabilizerEnabledProp == null) return false;
        try { return (bool)_stabilizerEnabledProp.GetValue(null); }
        catch { return false; }
    }

    internal static ConfigEntry<bool> MouseAimEnabled;
    internal static ConfigEntry<bool> InvertFreeLook;
    internal static ConfigEntry<float> HoverExitAngle;
    internal static ConfigEntry<bool> StabilityKbEnabled;

    // 简易比例映射增益：u = Kp * e（e 为目标方位在机体坐标系下的分量）
    internal static ConfigEntry<float> PropGainPitch;
    internal static ConfigEntry<float> PropGainYaw;
    internal static ConfigEntry<float> RollBlend;

    internal static ConfigEntry<PresetSlot> ActivePreset;
    internal static ConfigEntry<bool> SavePreset;
    internal static ConfigEntry<bool> LoadPreset;

    private static bool _debugAvailable;
    private static object _pitchErrStream, _pitchOutStream;
    private static object _rollErrStream, _rollOutStream;
    private static object _yawErrStream, _yawOutStream;
    private static object _coordPrStream, _coordYwStream;
    private static MethodInfo _debugPushMethod;
    private static MethodInfo _debugPushXyMethod;

    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("Mouse Aim Mod is loaded!");

        MouseAimEnabled = Config.Bind("General", "MouseAimEnabled", true, "Enable mouse aim");
        InvertFreeLook = Config.Bind("General", "InvertFreeLook", false, "When true, mouse aim is active only while Free Look is held");
        StabilityKbEnabled = Config.Bind("General", "StabilityKill", false, "Temporarily disable flight assist while keyboard pitch is active");
        HoverExitAngle = Config.Bind("Hover", "ExitAngle", 20f,
            new ConfigDescription("Pitch or roll angle exceeding this disables hover throttle",
                new AcceptableValueRange<float>(5f, 60f)));

        PropGainPitch = Config.Bind("Proportional", "GainPitch", 2f,
            new ConfigDescription("Proportional gain for pitch: pitch = -Kp * sin(vertical deviation)",
                new AcceptableValueRange<float>(0f, 20f)));
        PropGainYaw = Config.Bind("Proportional", "GainYaw", 2f,
            new ConfigDescription("Proportional gain for yaw: yaw = Kp * sin(horizontal deviation)",
                new AcceptableValueRange<float>(0f, 20f)));
        RollBlend = Config.Bind("Proportional", "RollBlend", 0.5f,
            new ConfigDescription("Roll target/velocity blend ratio; |X|,|Y| within this band mix proportionally",
                new AcceptableValueRange<float>(0.001f, 1f)));

        ActivePreset = Config.Bind("Presets", "ActivePreset", PresetSlot.Slot1, "Select preset slot for save/load.");
        SavePreset = Config.Bind("Presets", "SavePreset", false, "Toggle ON to save current config to active preset.");
        LoadPreset = Config.Bind("Presets", "LoadPreset", false, "Toggle ON to load config from active preset.");

        SavePreset.SettingChanged += (_, _) => { if (SavePreset.Value) { SaveToPreset(ActivePreset.Value); SavePreset.Value = false; } };
        LoadPreset.SettingChanged += (_, _) => { if (LoadPreset.Value) { LoadFromPreset(ActivePreset.Value); LoadPreset.Value = false; } };

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

            var coordVal = Enum.Parse(chartTypeEnum, "Coordinate");
            var xyChart = createChart.Invoke(null, new object[] { coordVal, "Stick XY", 480f, 200f, -1f, 1f, 5, -1f, 1f });
            var addC = xyChart.GetType().GetMethod("AddStream");
            _coordPrStream = addC.Invoke(xyChart, new object[] { "P/R", Color.cyan });
            _coordYwStream = addC.Invoke(xyChart, new object[] { "Yaw", Color.yellow });

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

    internal static void PushDebugCoord(float prX, float prY, float ywX, float ywY)
    {
        if (!_debugAvailable) return;
        try
        {
            if (_debugPushXyMethod == null)
                _debugPushXyMethod = _coordPrStream.GetType().GetMethod("PushXy");
            _debugPushXyMethod.Invoke(_coordPrStream, new object[] { prX, prY });
            _debugPushXyMethod.Invoke(_coordYwStream, new object[] { ywX, ywY });
        }
        catch { _debugAvailable = false; }
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
