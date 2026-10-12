using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace FBWAnalyserMod
{
    [BepInPlugin("nuclearoption.fbwanalysermod", "FBW Analyser Mod", "0.1.0")]
    [BepInProcess("NuclearOption.exe")]
    [BepInDependency("nuclearoption.debuggraphmod", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> IntervalFrames;
        internal static ConfigEntry<int> RingSize;
        internal static ConfigEntry<float> GraphW;
        internal static ConfigEntry<float> GraphH;
        internal static ConfigEntry<float> YMinRate;
        internal static ConfigEntry<float> YMaxRate;
        internal static ConfigEntry<float> YMaxLag;
        internal static ConfigEntry<float> YMaxQ;
        internal static ConfigEntry<float> UExcMin;
        internal static ConfigEntry<int> MinSignFlips;
        internal static ConfigEntry<int> PredHorizon;
        internal static ConfigEntry<float> MinR2;
        internal static ConfigEntry<float> SatFracMax;
        internal static ConfigEntry<int> SmoothWindow;
        internal static ConfigEntry<float> MaxPole;
        internal static ConfigEntry<float> DcGainMin;
        internal static ConfigEntry<float> DcGainMax;
        internal static ConfigEntry<bool> Yaw2ndOrder;

        private Harmony _harmony;

        private void Awake()
        {
            Logger = base.Logger;

            Enabled = Config.Bind("General", "Enabled", true, "Analysis + charts on/off.");
            IntervalFrames = Config.Bind("Estimator", "IntervalFrames", 30, "Re-estimate lag every N physics frames.");
            RingSize = Config.Bind("Estimator", "RingSize", 256, "Samples per axis estimation window.");
            GraphW = Config.Bind("Graphs", "Width", 320f, "Chart width (px).");
            GraphH = Config.Bind("Graphs", "Height", 220f, "Chart height (px).");
            YMinRate = Config.Bind("Graphs", "YMinRate", -8f, "Rates chart Y minimum (rad/s).");
            YMaxRate = Config.Bind("Graphs", "YMaxRate", 8f, "Rates chart Y maximum (rad/s).");
            YMaxLag = Config.Bind("Graphs", "YMaxLag", 1.5f, "Lag chart Y maximum (s).");
            YMaxQ = Config.Bind("Graphs", "YMaxQ", 3f, "Dynamic-pressure-ratio chart Y maximum.");
            UExcMin = Config.Bind("Estimator", "UExcMin", 0.03f, "Min target-rate std (rad/s) to count as excited.");
            MinSignFlips = Config.Bind("Estimator", "MinSignFlips", 2, "Min sign changes of (u-mean) for a valid window.");
            PredHorizon = Config.Bind("Estimator", "PredHorizon", 20, "H-step-ahead simulation horizon (frames) for the quality gate.");
            MinR2 = Config.Bind("Estimator", "MinR2", 0.5f, "Min H-step simulation R^2 for a valid ARX fit.");
            SatFracMax = Config.Bind("Estimator", "SatFracMax", 0.15f, "Max saturated-sample fraction allowed in a window.");
            SmoothWindow = Config.Bind("Estimator", "SmoothWindow", 5, "Moving-average window on u,y before identification (odd; removes noise bias).");
            MaxPole = Config.Bind("Estimator", "MaxPole", 0.985f, "Reject ARX poles at or above this (near-integrator fits).");
            DcGainMin = Config.Bind("Estimator", "DcGainMin", 0.2f, "Min identified DC gain b/(1-a) for a valid fit.");
            DcGainMax = Config.Bind("Estimator", "DcGainMax", 4f, "Max identified DC gain b/(1-a) for a valid fit.");
            Yaw2ndOrder = Config.Bind("Estimator", "Yaw2ndOrder", true, "Identify the yaw axis with a 2nd-order model.");

            LagEstimator.Init();

            _harmony = new Harmony("nuclearoption.fbwanalysermod");
            _harmony.PatchAll();

            Logger.LogInfo("FBW Analyser Mod loaded — plotting FBW target vs actual rate + lag, 6 charts.");
        }

        private void Start() { ChartBridge.Init(); }

        private void OnDestroy() { _harmony?.UnpatchSelf(); }
    }
}
