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

[BepInPlugin("nuclearoption.mouseaimmod", "Mouse Aim Mod", "2.1.0")]
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
    internal static ConfigEntry<float> AdaptiveKdMax;
    internal static ConfigEntry<float> GeomOaObSum;
    internal static ConfigEntry<float> LevelDriveGain;
    internal static ConfigEntry<float> LevelDriveThreshold;
    internal static ConfigEntry<float> HeliRpmGuardGain;
    internal static ConfigEntry<int> LagIntervalFrames;
    internal static ConfigEntry<int> LagRingSize;
    internal static ConfigEntry<int> LagPredHorizon;
    internal static ConfigEntry<int> LagSmoothWindow;
    internal static ConfigEntry<bool> LagAdaptiveSmooth;
    internal static ConfigEntry<float> LagAdaptiveK;
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

    // BepInEx ConfigurationManager 覆写：Order 越大越靠上；IsAdvanced 折叠到 "Advanced"。
    private sealed class ConfigurationManagerAttributes
    {
        public int? Order;
        public bool? IsAdvanced;
    }

    private static ConfigDescription D(string desc, int order, bool advanced = false)
        => new ConfigDescription(desc, null,
            new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced ? true : (bool?)null });

    private static ConfigDescription D(string desc, AcceptableValueBase range, int order, bool advanced = false)
        => new ConfigDescription(desc, range,
            new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced ? true : (bool?)null });

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("Mouse Aim Mod is loaded!");

        // ConfigManager 的段落显示顺序 = 下面首个 Bind 的顺序（不按字母序），调整位置会改变分组显示顺序，勿随意重排。
        MouseAimEnabled = Config.Bind("General", "Enabled", true,
            D("Master switch for the mouse-aim assist.", 60));
        InvertFreeLook = Config.Bind("General", "FreeLookOnly", false,
            D("When true the assist runs ONLY while Free Look is held (instead of the opposite).", 50));
        StabilityKbEnabled = Config.Bind("General", "StabilityKill", false,
            D("Temporarily disable flight assist while the keyboard pitch axis is active.", 40));
        AimPitch = Config.Bind("General", "AimPitch", true,
            D("Allow the assist to drive the pitch axis (off = leave pitch to the pilot).", 30));
        AimRoll = Config.Bind("General", "AimRoll", true,
            D("Allow the assist to drive the roll axis.", 20));
        AimYaw = Config.Bind("General", "AimYaw", true,
            D("Allow the assist to drive the yaw axis.", 10));

        PropGainPitch = Config.Bind("Tuning", "PitchGain", 1f,
            D("Fixed proportional gain, pitch. Used when Adaptive is off or has no valid estimate.", new AcceptableValueRange<float>(0f, 20f), 60));
        PropGainYaw = Config.Bind("Tuning", "YawGain", 1f,
            D("Fixed proportional gain, yaw.", new AcceptableValueRange<float>(0f, 20f), 50));
        PropGainRoll = Config.Bind("Tuning", "RollGain", 1f,
            D("Fixed proportional gain, roll.", new AcceptableValueRange<float>(0f, 20f), 40));
        FeedbackGain = Config.Bind("Tuning", "RateFeedback", 0.2f,
            D("Rate-damping master scale: u -= RateFeedback*Kp*tau*omega (capped by Adaptive/KdMax).", new AcceptableValueRange<float>(0f, 3f), 30));
        GeomOaObSum = Config.Bind("Tuning", "ApexSum", 1f,
            D("Jet only (body-frame law): |OA|+|OB| total length; 1 = original, higher shifts the roll flip later.", new AcceptableValueRange<float>(1f, 2f), 10));
        LevelDriveGain = Config.Bind("Tuning", "LevelDriveGain", 0.1f,
            D("Body-frame law: wings-level drive added to the roll err when aimed; weight of (bank/pi).", new AcceptableValueRange<float>(0f, 2f), 9));
        LevelDriveThreshold = Config.Bind("Tuning", "LevelDriveThreshold", 0.05f,
            D("Body-frame law: |pitchErr| and |yawErr| must both be below this (5% of full scale) to apply the wings-level drive.", new AcceptableValueRange<float>(0f, 0.5f), 8));

        Adaptive = Config.Bind("Adaptive", "Enabled", true,
            D("Derive Kp/Kd online from the identified plant (K,tau,theta) instead of the fixed gains.", 80));
        AdaptiveWc = Config.Bind("Adaptive", "Bandwidth", 0.4f,
            D("Target bandwidth: Kp = Bandwidth / (|K| * max(theta, ThetaMin)).", new AcceptableValueRange<float>(0.05f, 2f), 70));
        AdaptiveKpMin = Config.Bind("Adaptive", "KpMin", 0.2f,
            D("Adaptive Kp lower clamp.", new AcceptableValueRange<float>(0.01f, 5f), 60));
        AdaptiveKpMax = Config.Bind("Adaptive", "KpMax", 4f,
            D("Adaptive Kp upper clamp.", new AcceptableValueRange<float>(0.01f, 20f), 50));
        AdaptiveKdMax = Config.Bind("Adaptive", "KdMax", 1f,
            D("Hard cap on the per-axis rate-feedback gain Kd = RateFeedback*Kp*tau (prevents roll/yaw limit cycles).", new AcceptableValueRange<float>(0f, 10f), 40));
        AdaptiveThetaMin = Config.Bind("Adaptive", "ThetaMin", 0.02f,
            D("Identified dead-time floor (stops Kp blowing up as theta -> 0).", new AcceptableValueRange<float>(0.005f, 0.5f), 30, true));
        AdaptiveTauMin = Config.Bind("Adaptive", "TauMin", 0.05f,
            D("Identified tau lower clamp (used for Kd).", new AcceptableValueRange<float>(0.01f, 1f), 20, true));
        AdaptiveTauMax = Config.Bind("Adaptive", "TauMax", 1f,
            D("Identified tau upper clamp.", new AcceptableValueRange<float>(0.01f, 3f), 10, true));

        HeliRpmGuardGain = Config.Bind("Heli", "RpmGuardGain", 1.5f,
            D("Rotor aircraft only. Hover auto-throttle guard: throttle -= (nominalRPM - avgRotorRPM)*gain while drooping.", new AcceptableValueRange<float>(0f, 6f), 10));

        LagIntervalFrames = Config.Bind("Lag", "IntervalFrames", 30,
            D("Re-identify every N physics frames.", new AcceptableValueRange<int>(1, 300), 60, true));
        LagRingSize = Config.Bind("Lag", "RingSize", 256,
            D("Identification window, samples per axis (applies after reload).", new AcceptableValueRange<int>(32, 4096), 50, true));
        LagPredHorizon = Config.Bind("Lag", "PredHorizon", 20,
            D("H-step simulation horizon (frames) for the identification quality gate.", new AcceptableValueRange<int>(2, 200), 40, true));
        LagSmoothWindow = Config.Bind("Lag", "SmoothWindow", 5,
            D("Fixed moving-average window on u,y before identification.", new AcceptableValueRange<int>(1, 15), 30, true));
        LagAdaptiveSmooth = Config.Bind("Lag", "AdaptiveSmooth", true,
            D("Pick the smoothing window per axis from the last tau (fast axes -> smaller window).", 20, true));
        LagAdaptiveK = Config.Bind("Lag", "AdaptiveK", 0.3f,
            D("Adaptive smoothing: window ~= AdaptiveK * tau / dt (clamped 3..13).", new AcceptableValueRange<float>(0.05f, 1f), 10, true));

        ActivePreset = Config.Bind("Presets", "ActivePreset", PresetSlot.Slot1,
            D("Slot used by SavePreset / LoadPreset.", 30));
        SavePreset = Config.Bind("Presets", "SavePreset", false,
            D("Toggle ON to save the current settings into the active slot.", 20));
        LoadPreset = Config.Bind("Presets", "LoadPreset", false,
            D("Toggle ON to load the active slot into the current settings.", 10));

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
