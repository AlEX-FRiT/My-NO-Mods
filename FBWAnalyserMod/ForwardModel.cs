using UnityEngine;

namespace FBWAnalyserMod
{
    // Forward model: ControlInputs command -> FBW target angular velocity (rad/s, body frame,
    // x = pitch, y = yaw, z = roll). Kept line-for-line consistent with the decompiled FBW.
    internal static class ForwardModel
    {
        // Fixed-wing (ControlsFilter.FlyByWire.Filter, decompile lines 65023-65101).
        //   pitch target : read the game's own value (GetTargetPitchAngVel) - 100% consistent.
        //   yaw target   : == the command itself (FBW drives yaw rate to inputs.yaw).
        //   roll target  : -inputs.roll * maxRollAngularVel * clamp(num2 / maxRollSpeed, 0.5, 1).
        public static float QRatio(Aircraft ac, float cornerSpeed)
        {
            return ac.speed * ac.speed * ac.airDensity / (cornerSpeed * cornerSpeed * 1.225f);
        }

        public static Vector3 FixedWing(ControlsFilter.FlyByWire fbw, Aircraft ac, Vector3 cmd,
                                        float maxRollAngularVel, float maxRollSpeed, float cornerSpeed)
        {
            float tgtPitch;
            try { tgtPitch = fbw.GetTargetPitchAngVel(); }
            catch { tgtPitch = 0f; }

            float tgtYaw = cmd.y;

            float num2 = QRatio(ac, cornerSpeed);
            float tgtRoll = -cmd.z * maxRollAngularVel * Mathf.Clamp(num2 / maxRollSpeed, 0.5f, 1f);

            return new Vector3(tgtPitch, tgtYaw, tgtRoll);
        }

        // Helicopter (HeloControlsFilter.HeloFlyByWire.Filter, decompile lines 36019-36043).
        //   vector = ( clamp(cmd.x * maxAngularVel.x, +/-gLimit*9.81/max(speed,10)),
        //              cmd.y * maxAngularVel.y, -cmd.z * maxAngularVel.z )
        public static Vector3 Helo(object heloFlyByWire, Aircraft ac, Vector3 cmd)
        {
            Vector3 maxAngularVel = FbwParams.GetVec3(heloFlyByWire, "maxAngularVel", new Vector3(1f, 2f, 2f));
            float gLimit = FbwParams.GetFloat(heloFlyByWire, "gLimit", 3f);

            float cap = gLimit * 9.81f / Mathf.Max(ac.speed, 10f);
            return new Vector3(
                Mathf.Clamp(cmd.x * maxAngularVel.x, -cap, cap),
                cmd.y * maxAngularVel.y,
                -cmd.z * maxAngularVel.z);
        }

        // Per-axis achievable-rate ceilings (rad/s), used by the estimator to detect output saturation.
        public static Vector3 FixedWingCeiling(ControlsFilter.FlyByWire fbw, Aircraft ac, float maxRollAngularVel, float cornerSpeed)
        {
            float maxPitchAV = 1f;
            try { maxPitchAV = fbw.GetParameters().Item2[1]; } catch { maxPitchAV = 1f; }
            float gCap = FbwParams.GLimitPositive(fbw) * 9.81f / Mathf.Max(ac.speed, 0.75f * cornerSpeed);
            return new Vector3(Mathf.Max(0.1f, Mathf.Max(maxPitchAV, gCap)), 1f, Mathf.Max(0.1f, maxRollAngularVel));
        }

        public static Vector3 HeloCeiling(object heloFlyByWire, Aircraft ac)
        {
            Vector3 maxAngularVel = FbwParams.GetVec3(heloFlyByWire, "maxAngularVel", new Vector3(1f, 2f, 2f));
            float gLimit = FbwParams.GetFloat(heloFlyByWire, "gLimit", 3f);
            float pitchCap = Mathf.Max(0.1f, Mathf.Min(maxAngularVel.x, gLimit * 9.81f / Mathf.Max(ac.speed, 10f)));
            return new Vector3(pitchCap, Mathf.Max(0.1f, maxAngularVel.y), Mathf.Max(0.1f, maxAngularVel.z));
        }
    }
}
