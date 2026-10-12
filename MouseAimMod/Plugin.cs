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
public enum AimMode { Mixed, A, B }

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
    internal static ConfigEntry<float> PropGainRoll;

    internal static ConfigEntry<float> FeedbackGain;
    internal static ConfigEntry<bool> Adaptive;
    internal static ConfigEntry<float> AdaptiveWc;
    internal static ConfigEntry<float> AdaptiveKpMin;
    internal static ConfigEntry<float> AdaptiveKpMax;
    internal static ConfigEntry<float> AdaptiveTauMin;
    internal static ConfigEntry<float> AdaptiveTauMax;
    internal static ConfigEntry<float> AdaptiveThetaMin;
    internal static ConfigEntry<float> TargetFilterTau;
    internal static ConfigEntry<int> LagIntervalFrames;
    internal static ConfigEntry<int> LagRingSize;
    internal static ConfigEntry<int> LagPredHorizon;
    internal static ConfigEntry<int> LagSmoothWindow;
    internal static ConfigEntry<bool> LagAdaptiveSmooth;
    internal static ConfigEntry<float> LagAdaptiveK;
    internal static ConfigEntry<float> ModeBlendX;
    internal static ConfigEntry<float> ModeBlendY;
    internal static ConfigEntry<float> ModePitchN;
    internal static ConfigEntry<AimMode> ModeForce;
    internal static ConfigEntry<bool> AimPitch;
    internal static ConfigEntry<bool> AimRoll;
    internal static ConfigEntry<bool> AimYaw;

    internal static ConfigEntry<PresetSlot> ActivePreset;
    internal static ConfigEntry<bool> SavePreset;
    internal static ConfigEntry<bool> LoadPreset;

    private static bool _debugAvailable;
    private static object _pitchOutStream, _rollOutStream, _yawOutStream;
    private static object _pitchErrStream, _rollErrStream, _yawErrStream;
    private static object _pitchTauStream, _rollTauStream, _yawTauStream;
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
        PropGainRoll = Config.Bind("Proportional", "GainRoll", 1f,
            new ConfigDescription("Proportional gain for roll: roll = Kp * horizontal deviation",
                new AcceptableValueRange<float>(0f, 20f)));

        FeedbackGain = Config.Bind("Feedback", "Gain", 1f,
            new ConfigDescription("Master scale on the rate feedback: u -= Gain*Kp*tau*omega.",
                new AcceptableValueRange<float>(0f, 3f)));
        Adaptive = Config.Bind("Adaptive", "Enabled", true,
            "Derive Kp/Kd online from the identified (K,tau,theta) instead of the fixed Proportional gains.");
        AdaptiveWc = Config.Bind("Adaptive", "Wc", 0.4f,
            new ConfigDescription("Kp = Wc / (K * max(theta, ThetaMin)).",
                new AcceptableValueRange<float>(0.05f, 2f)));
        AdaptiveKpMin = Config.Bind("Adaptive", "KpMin", 0.2f,
            new ConfigDescription("Kp clamp min.", new AcceptableValueRange<float>(0.01f, 20f)));
        AdaptiveKpMax = Config.Bind("Adaptive", "KpMax", 20f,
            new ConfigDescription("Kp clamp max.", new AcceptableValueRange<float>(0.01f, 100f)));
        AdaptiveTauMin = Config.Bind("Adaptive", "TauMin", 0.05f,
            new ConfigDescription("tau clamp min for Kd = Kp*tau.", new AcceptableValueRange<float>(0.01f, 1f)));
        AdaptiveTauMax = Config.Bind("Adaptive", "TauMax", 1f,
            new ConfigDescription("tau clamp max.", new AcceptableValueRange<float>(0.01f, 3f)));
        AdaptiveThetaMin = Config.Bind("Adaptive", "ThetaMin", 0.02f,
            new ConfigDescription("theta floor (stops Kp blow-up as theta -> 0).", new AcceptableValueRange<float>(0.005f, 0.5f)));
        TargetFilterTau = Config.Bind("Filter", "TargetTau", 0.05f,
            new ConfigDescription("Low-pass time constant on the input target direction (0 = off).",
                new AcceptableValueRange<float>(0f, 1f)));
        LagIntervalFrames = Config.Bind("Lag", "IntervalFrames", 30,
            new ConfigDescription("Re-estimate the lag every N physics frames.",
                new AcceptableValueRange<int>(1, 300)));
        LagRingSize = Config.Bind("Lag", "RingSize", 256,
            new ConfigDescription("Identification window length, samples per axis (takes effect on reload).",
                new AcceptableValueRange<int>(32, 4096)));
        LagPredHorizon = Config.Bind("Lag", "PredHorizon", 20,
            new ConfigDescription("H-step simulation horizon (frames) for the quality gate.",
                new AcceptableValueRange<int>(2, 200)));
        LagSmoothWindow = Config.Bind("Lag", "SmoothWindow", 5,
            new ConfigDescription("Moving-average window on u,y before identification (odd; removes noise bias).",
                new AcceptableValueRange<int>(1, 15)));
        LagAdaptiveSmooth = Config.Bind("Lag", "AdaptiveSmooth", true,
            "Pick the smoothing window per axis from the last tau (fast axes -> smaller window).");
        LagAdaptiveK = Config.Bind("Lag", "AdaptiveK", 0.3f,
            new ConfigDescription("Adaptive smoothing: window ~= AdaptiveK * tau / dt (clamped 3..13).",
                new AcceptableValueRange<float>(0.05f, 1f)));
        ModeBlendX = Config.Bind("Mode", "BlendX", 0.3f,
            new ConfigDescription("z <= BlendX -> pure A (yaw+pitch).",
                new AcceptableValueRange<float>(0f, 1f)));
        ModeBlendY = Config.Bind("Mode", "BlendY", 0.7f,
            new ConfigDescription("z >= BlendY -> pure B (roll+pitch).",
                new AcceptableValueRange<float>(0f, 1f)));
        ModePitchN = Config.Bind("Mode", "PitchWeightN", 2f,
            new ConfigDescription("Mode B pitch weight (1-|rollErr|)^n exponent.",
                new AcceptableValueRange<float>(0.1f, 10f)));
        ModeForce = Config.Bind("Mode", "ForceMode", AimMode.Mixed, "Force a single mode (Mixed/A/B).");
        AimPitch = Config.Bind("Control", "AimPitch", true, "Enable the mouse-aim output on the pitch axis (off = suppress assist pitch).");
        AimRoll = Config.Bind("Control", "AimRoll", true, "Enable the mouse-aim output on the roll axis (off = suppress assist roll).");
        AimYaw = Config.Bind("Control", "AimYaw", true, "Enable the mouse-aim output on the yaw axis (off = suppress assist yaw).");

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
            var errChart = createChart.Invoke(null, new[] { flowVal, "Pitch", 480f, 200f, -1f, 1f, 1800, null, null });
            var outChart = createChart.Invoke(null, new[] { flowVal, "Roll", 480f, 200f, -1f, 1f, 1800, null, null });
            var yawChart = createChart.Invoke(null, new[] { flowVal, "Yaw", 480f, 200f, -1f, 1f, 1800, null, null });
            var addStream = errChart.GetType().GetMethod("AddStream");
            _pitchErrStream = addStream.Invoke(errChart, new object[] { "Err", Color.green });
            _rollErrStream = addStream.Invoke(outChart, new object[] { "Err", Color.green });
            _yawErrStream = addStream.Invoke(yawChart, new object[] { "Err", Color.green });
            _pitchOutStream = addStream.Invoke(errChart, new object[] { "Out", Color.yellow });
            _rollOutStream = addStream.Invoke(outChart, new object[] { "Out", Color.yellow });
            _yawOutStream = addStream.Invoke(yawChart, new object[] { "Out", Color.yellow });
            _pitchTauStream = addStream.Invoke(errChart, new object[] { "Tau", Color.cyan });
            _rollTauStream = addStream.Invoke(outChart, new object[] { "Tau", Color.cyan });
            _yawTauStream = addStream.Invoke(yawChart, new object[] { "Tau", Color.cyan });

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

    internal static void PushDebugData(float pO, float rO, float yO)
    {
        if (!_debugAvailable) return;
        try
        {
            if (_debugPushMethod == null) _debugPushMethod = _pitchOutStream.GetType().GetMethod("Push");
            _debugPushMethod.Invoke(_pitchOutStream, new object[] { pO });
            _debugPushMethod.Invoke(_rollOutStream, new object[] { rO });
            _debugPushMethod.Invoke(_yawOutStream, new object[] { yO });
        }
        catch { _debugAvailable = false; }
    }

    internal static void PushDebugErr(float pE, float rE, float yE)
    {
        if (!_debugAvailable) return;
        try
        {
            if (_debugPushMethod == null) _debugPushMethod = _pitchOutStream.GetType().GetMethod("Push");
            _debugPushMethod.Invoke(_pitchErrStream, new object[] { pE });
            _debugPushMethod.Invoke(_rollErrStream, new object[] { rE });
            _debugPushMethod.Invoke(_yawErrStream, new object[] { yE });
        }
        catch { _debugAvailable = false; }
    }

    internal static void PushDebugTau(float pTau, float rTau, float yTau)
    {
        if (!_debugAvailable) return;
        try
        {
            if (_debugPushMethod == null) _debugPushMethod = _pitchOutStream.GetType().GetMethod("Push");
            _debugPushMethod.Invoke(_pitchTauStream, new object[] { pTau });
            _debugPushMethod.Invoke(_rollTauStream, new object[] { rTau });
            _debugPushMethod.Invoke(_yawTauStream, new object[] { yTau });
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
