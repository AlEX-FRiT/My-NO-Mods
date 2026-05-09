using HarmonyLib;
using UnityEngine;

namespace MouseAimMod;

[HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]
public static class PilotPlayerStatePatch
{
    private static bool _mpcInitialized;
    private static bool _stabilityKilled;
    private static bool _stabilityOrig;

    [HarmonyPatch("PlayerControls")]
    [HarmonyPostfix]
    public static void PlayerControlsPostfix(PilotPlayerState __instance, Pilot ___pilot)
    {
        if (!CameraAimState.MouseAimEnabled) return;
        if (PlayerSettings.virtualJoystickEnabled) return;
        if (!FlightHudStatePatch.CanvasEnabled) return;
        var pilot = ___pilot;
        if (pilot == null || pilot.aircraft == null) return;
        if (!pilot.aircraft.GetAircraftParameters().verticalLanding) return;
        var aircraft = pilot.aircraft;
        float pitch = Mathf.Asin(-aircraft.transform.forward.y) * Mathf.Rad2Deg;
        float roll  = Mathf.Asin( aircraft.transform.right.y)  * Mathf.Rad2Deg;
        if (Mathf.Abs(pitch) > Plugin.HoverExitAngle.Value || Mathf.Abs(roll) > Plugin.HoverExitAngle.Value)
        {
            if (HoverThrottleController.HoverActive) HoverThrottleController.Disable(__instance);
        }
        else if (GameManager.playerInput.GetButtonTimedPressUp("Brake", 0f, PlayerSettings.clickDelay))
        {
            if (!HoverThrottleController.HoverActive) HoverThrottleController.Enable(__instance);
            else HoverThrottleController.Disable(__instance);
        }
    }

    [HarmonyPostfix]
    public static void PlayerAxisControlsPostfix(PilotPlayerState __instance, Pilot ___pilot, ControlInputs ___controlInputs)
    {
        if (!CameraAimState.MouseAimEnabled) return;
        if (PlayerSettings.virtualJoystickEnabled) return;
        if (!FlightHudStatePatch.CanvasEnabled) return;

        bool freeLook = GameManager.playerInput.GetButton("Free Look");
        bool keyboardPitch = Mathf.Abs(___controlInputs.pitch) > 0.01f;

        var pilot = ___pilot;
        if (pilot == null || pilot.aircraft == null) return;
        var aircraft = pilot.aircraft;
        if (aircraft.rb == null) return;
        var controlInputs = ___controlInputs;
        if (controlInputs == null) return;

        bool canHover = aircraft.GetAircraftParameters().verticalLanding;
        if (HoverThrottleController.HoverActive && canHover)
            HoverThrottleController.ApplyHoverThrottle(controlInputs, aircraft);

        bool aimActive = Plugin.InvertFreeLook.Value ? freeLook : !freeLook;
        if (!aimActive) { _mpcInitialized = false; return; }
        if (Camera.main == null) return;

        Vector3 viewDirection = Camera.main.transform.forward;
        Vector3 localTarget = Quaternion.Inverse(aircraft.transform.rotation) * viewDirection;
        float horizDev = localTarget.x, vertDev = localTarget.y;
        Vector3 localAV = aircraft.transform.InverseTransformDirection(aircraft.rb.angularVelocity);

        if (localTarget.z < 0f) vertDev = Mathf.Sign(vertDev);
        float exp = Plugin.ErrorExp.Value;
        horizDev = Mathf.Sign(horizDev) * Mathf.Pow(Mathf.Abs(horizDev), exp);
        vertDev  = Mathf.Sign(vertDev)  * Mathf.Pow(Mathf.Abs(vertDev),  exp);

        float pitchError = -vertDev, yawError = horizDev;
        float rollAngle = aircraft.transform.eulerAngles.z;
        if (rollAngle > 180f) rollAngle -= 360f;
        float rollError = horizDev;
        if (Plugin.RollCentering.Value)
        {
            float s = Mathf.Sin(rollAngle * Mathf.Deg2Rad);
            float f = 1f - Mathf.Clamp01(Mathf.Abs(horizDev) / Plugin.CenteringRange.Value);
            rollError = horizDev + s * f * Plugin.CenteringGain.Value;
        }

        if (!_mpcInitialized) { _mpcInitialized = true; }

        float dt = Time.fixedDeltaTime;
        float pitchOut = Mpc(pitchError, localAV.x, dt, Plugin.MpcK.Value, Plugin.MpcHorizon.Value, Plugin.MpcIter.Value, Plugin.MpcPenalty.Value);
        float rollOut  = Mpc(rollError,  localAV.z, dt, Plugin.MpcK.Value, Plugin.MpcHorizon.Value, Plugin.MpcIter.Value, Plugin.MpcPenalty.Value);
        float yawOut   = Mpc(yawError,   localAV.y, dt, Plugin.MpcK.Value, Plugin.MpcHorizon.Value, Plugin.MpcIter.Value, Plugin.MpcPenalty.Value);

        pitchOut = ApplyPaCompensation(pitchOut, aircraft);
        pitchOut = Mathf.Clamp(pitchOut * Plugin.MpcScale.Value, -1f, 1f);
        rollOut  = Mathf.Clamp(rollOut  * Plugin.MpcScale.Value, -1f, 1f);
        yawOut   = Mathf.Clamp(yawOut   * Plugin.MpcScale.Value, -1f, 1f);

        float yawScale = 1f;
        if (Plugin.RollYawBalance.Value)
        {
            float d = Vector3.Angle(aircraft.transform.forward, Camera.main.transform.forward);
            yawScale = 1f - Mathf.Clamp01((d - Plugin.YawAttenStart.Value) / (Plugin.YawAttenEnd.Value - Plugin.YawAttenStart.Value));
        }
        yawOut *= yawScale;

        Plugin.PushDebugData(pitchError, rollError, yawError, pitchOut, rollOut, yawOut);

        if (controlInputs.pitch == 0f) controlInputs.pitch = pitchOut;
        if (controlInputs.roll  == 0f) controlInputs.roll  = rollOut;
        if (controlInputs.yaw   == 0f) controlInputs.yaw   = yawOut;

        if (keyboardPitch && Plugin.StabilityKbEnabled.Value && !_stabilityKilled)
        {
            _stabilityOrig = aircraft.flightAssist;
            aircraft.flightAssist = false;
            _stabilityKilled = true;
        }
    }

    [HarmonyPatch(typeof(Aircraft), "FilterInputs")]
    [HarmonyPostfix]
    private static void FilterInputsPostfix(Aircraft __instance)
    {
        if (_stabilityKilled) { __instance.flightAssist = _stabilityOrig; _stabilityKilled = false; }
    }

    static float EvalCost(float u, float omega, float errorRad, float k, int horizon, float dt, float penalty)
    {
        float w = omega, e = errorRad;
        float alpha = 1f - Mathf.Exp(-k * dt);
        float ak = k > 0.0001f ? alpha / k : dt;
        for (int f = 0; f < horizon; f++)
        {
            float w0 = w;
            w += alpha * (u - w);
            e -= u * dt + (w0 - u) * ak;
        }
        float cost = e * e + w * w * dt * dt;
        if (Mathf.Abs(errorRad) > 0.005f && Mathf.Sign(e) != Mathf.Sign(errorRad)) cost += e * e * penalty;
        return cost;
    }

    static float Mpc(float errorSin, float omega, float dt, float k, int horizon, int iters, float penalty)
    {
        float errorRad = Mathf.Asin(Mathf.Clamp(errorSin, -0.999f, 0.999f));
        float lo = -1f, hi = 1f;
        float phi = 0.618034f;
        float c = hi - phi * (hi - lo), d = lo + phi * (hi - lo);
        float fc = EvalCost(c, omega, errorRad, k, horizon, dt, penalty);
        float fd = EvalCost(d, omega, errorRad, k, horizon, dt, penalty);
        for (int i = 0; i < iters; i++)
        {
            if (fc < fd) { hi = d; d = c; fd = fc; c = hi - phi * (hi - lo); fc = EvalCost(c, omega, errorRad, k, horizon, dt, penalty); }
            else { lo = c; c = d; fc = fd; d = lo + phi * (hi - lo); fd = EvalCost(d, omega, errorRad, k, horizon, dt, penalty); }
        }
        return (lo + hi) * 0.5f;
    }

    static float ApplyPaCompensation(float mpcOut, Aircraft aircraft)
    {
        var fbw = aircraft.GetControlsFilter();
        if (fbw == null) return mpcOut;
        var fw = Traverse.Create(fbw).Field("flyByWire");
        float pa = fw.Field("pitchAdjuster").GetValue<float>();
        float df = fw.Field("directControlFactor").GetValue<float>();
        if (Mathf.Abs(pa) < 0.001f || df < 0.001f) return mpcOut;

        var (_, p) = fbw.GetFlyByWireParameters();
        float cornerSpeed = p[2];
        float speed = aircraft.speed, rho = aircraft.airDensity;
        float qRatio = Mathf.Clamp01(cornerSpeed * cornerSpeed * 1.225f / Mathf.Max(rho * speed * speed, 50f));
        float maxPitchAV = p[1];
        float denom = df * qRatio * maxPitchAV;
        if (denom < 0.001f) return mpcOut;

        return Mathf.Clamp(mpcOut - pa / denom, -1f, 1f);
    }
}
