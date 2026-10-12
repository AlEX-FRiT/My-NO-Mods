using System;
using System.Reflection;
using UnityEngine;

namespace FBWAnalyserMod
{
    // Registers Flow charts with DebugGraphMod via reflection (soft dependency):
    // per axis {Pitch,Yaw,Roll} "Rates"+"Lag", plus a dynamic-pressure-ratio chart.
    internal static class ChartBridge
    {
        private const int HistSize = 64;

        private static readonly string[] AxisName = { "Pitch", "Yaw", "Roll" };

        private static bool _ok;
        private static MethodInfo _push;
        private static readonly object[] _rateStream = new object[15];
        private static readonly object[] _lagStream = new object[15];
        private static object _qStream;
        private static readonly float[][] _tgtHist = new float[3][];
        private static int _histHead;

        public static void Init()
        {
            try
            {
                var regT = Type.GetType("DebugGraphMod.GraphRegistry, DebugGraphMod");
                if (regT == null) { _ok = false; return; }
                var ctT = Type.GetType("DebugGraphMod.ChartType, DebugGraphMod");
                var flow = Enum.Parse(ctT, "Flow");
                var create = regT.GetMethod("CreateChart");

                float w = Plugin.GraphW.Value, h = Plugin.GraphH.Value;
                int cap = Plugin.RingSize.Value;

                for (int a = 0; a < 3; a++)
                {
                    var rates = create.Invoke(null, new object[] { flow, AxisName[a] + " Rates", w, h, Plugin.YMinRate.Value, Plugin.YMaxRate.Value, cap, null, null });
                    var lag = create.Invoke(null, new object[] { flow, AxisName[a] + " Lag", w, h, 0f, Plugin.YMaxLag.Value, cap, null, null });

                    var add = rates.GetType().GetMethod("AddStream");
                    _rateStream[a * 5] = add.Invoke(rates, new object[] { "target", Color.yellow });
                    _rateStream[a * 5 + 1] = add.Invoke(rates, new object[] { "actual", Color.cyan });
                    _rateStream[a * 5 + 2] = add.Invoke(rates, new object[] { "cap+", new Color(1f, 0.5f, 0.3f) });
                    _rateStream[a * 5 + 3] = add.Invoke(rates, new object[] { "cap-", new Color(1f, 0.5f, 0.3f) });
                    _rateStream[a * 5 + 4] = add.Invoke(rates, new object[] { "tgt_adj", new Color(1f, 0.45f, 0.55f) });
                    _lagStream[a * 5] = add.Invoke(lag, new object[] { "tau", Color.green });
                    _lagStream[a * 5 + 1] = add.Invoke(lag, new object[] { "tau2", new Color(0.5f, 1f, 0.6f) });
                    _lagStream[a * 5 + 2] = add.Invoke(lag, new object[] { "theta", Color.magenta });
                    _lagStream[a * 5 + 3] = add.Invoke(lag, new object[] { "gate", Color.white });
                    _lagStream[a * 5 + 4] = add.Invoke(lag, new object[] { "R2", new Color(0.6f, 0.8f, 1f) });
                }

                var qChart = create.Invoke(null, new object[] { flow, "q (dyn press ratio)", w, h, 0f, Plugin.YMaxQ.Value, cap, null, null });
                var qAdd = qChart.GetType().GetMethod("AddStream");
                _qStream = qAdd.Invoke(qChart, new object[] { "q", Color.green });

                for (int a = 0; a < 3; a++) _tgtHist[a] = new float[HistSize];

                _push = _rateStream[0].GetType().GetMethod("Push");
                _ok = true;
                Plugin.Logger.LogInfo("FBWAnalyserMod: 7 charts registered with DebugGraphMod.");
            }
            catch (Exception e)
            {
                _ok = false;
                Plugin.Logger.LogInfo("FBWAnalyserMod: DebugGraphMod not available (" + e.Message + "). Running headless.");
            }
        }

        public static void Push(Vector3 tgt, Vector3 omega, Vector3 ceil, float qRatio)
        {
            if (!_ok) return;
            try
            {
                float[] tv = { tgt.x, tgt.y, tgt.z };
                float[] ov = { omega.x, omega.y, omega.z };
                float[] cv = { ceil.x, ceil.y, ceil.z };

                for (int a = 0; a < 3; a++) _tgtHist[a][_histHead] = tv[a];
                _histHead = (_histHead + 1) % HistSize;

                for (int a = 0; a < 3; a++)
                {
                    int d = Mathf.Clamp(LagEstimator.AdjLag[a], 0, HistSize - 1);
                    int di = ((_histHead - 1 - d) % HistSize + HistSize) % HistSize;
                    float adj = LagEstimator.AdjGain[a] * _tgtHist[a][di];

                    _push.Invoke(_rateStream[a * 5], new object[] { tv[a] });
                    _push.Invoke(_rateStream[a * 5 + 1], new object[] { ov[a] });
                    _push.Invoke(_rateStream[a * 5 + 2], new object[] { cv[a] });
                    _push.Invoke(_rateStream[a * 5 + 3], new object[] { -cv[a] });
                    _push.Invoke(_rateStream[a * 5 + 4], new object[] { adj });

                    _push.Invoke(_lagStream[a * 5], new object[] { LagEstimator.Tau[a] });
                    _push.Invoke(_lagStream[a * 5 + 1], new object[] { LagEstimator.Tau2[a] });
                    _push.Invoke(_lagStream[a * 5 + 2], new object[] { LagEstimator.Theta[a] });
                    _push.Invoke(_lagStream[a * 5 + 3], new object[] { LagEstimator.Gated[a] ? 0f : 1f });
                    _push.Invoke(_lagStream[a * 5 + 4], new object[] { LagEstimator.R2[a] });
                }
                _push.Invoke(_qStream, new object[] { qRatio });
            }
            catch { _ok = false; }
        }
    }
}
