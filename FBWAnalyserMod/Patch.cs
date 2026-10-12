using HarmonyLib;
using UnityEngine;

namespace FBWAnalyserMod
{
    // Fixed-wing seam: postfix on ControlsFilter.Filter. Runs after the FBW has computed this tick,
    // so pitch getter is fresh; `rawInputs` (method arg) still holds the yaw/roll command (the
    // RelaxedStabilityController only rewrites pitch before the FBW).
    [HarmonyPatch(typeof(ControlsFilter), "Filter")]
    internal static class FixedWingFilterPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ControlsFilter __instance, Vector3 rawInputs, Rigidbody rb)
        {
            if (!Plugin.Enabled.Value) return;
            if (__instance is HeloControlsFilter) return; // handled by HeloFilterPatch

            var ac = FbwParams.GetAircraft(__instance);
            var fbw = FbwParams.GetFlyByWire(__instance);
            if (ac == null || fbw == null || !fbw.Enabled || rb == null || ac.rb == null) return;

            // Mirror the FBW's own early-out (decompile 65759).
            if (ac.speed < FbwParams.GetMinSpeed(__instance) || ac.radarAlt < FbwParams.GetMinAlt(__instance)) return;

            Vector3 omega = rb.transform.InverseTransformDirection(rb.angularVelocity);
            Vector3 cmd = FbwParams.ResolveCommand(fbw, rawInputs);
            float[] p = fbw.GetParameters().Item2;
            float maxRollAV = FbwParams.MaxRollAngularVel(fbw);
            Vector3 tgt = ForwardModel.FixedWing(fbw, ac, cmd, maxRollAV, FbwParams.MaxRollSpeed(fbw), p[2]);
            Vector3 ceil = ForwardModel.FixedWingCeiling(fbw, ac, maxRollAV, p[2]);

            LagEstimator.NoteAircraft(ac.GetInstanceID());
            LagEstimator.Sample(tgt, omega, ceil);
            ChartBridge.Push(tgt, omega, ceil, ForwardModel.QRatio(ac, p[2]));
        }
    }

    // Rotorcraft seam: HeloControlsFilter overrides Filter, so it needs its own patch.
    [HarmonyPatch(typeof(HeloControlsFilter), "Filter")]
    internal static class HeloFilterPatch
    {
        [HarmonyPostfix]
        private static void Postfix(HeloControlsFilter __instance, Vector3 rawInputs, Rigidbody rb)
        {
            if (!Plugin.Enabled.Value) return;

            var ac = FbwParams.GetAircraft(__instance);
            var hfbw = FbwParams.GetHeloFlyByWire(__instance);
            if (ac == null || hfbw == null || rb == null) return;
            if (!FbwParams.GetBool(hfbw, "Enabled", true)) return;

            Vector3 omega = rb.transform.InverseTransformDirection(rb.angularVelocity);
            Vector3 tgt = ForwardModel.Helo(hfbw, ac, rawInputs);
            Vector3 ceil = ForwardModel.HeloCeiling(hfbw, ac);

            LagEstimator.NoteAircraft(ac.GetInstanceID());
            LagEstimator.Sample(tgt, omega, ceil);
            ChartBridge.Push(tgt, omega, ceil, 0f);
        }
    }
}
