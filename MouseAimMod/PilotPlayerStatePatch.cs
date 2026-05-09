using HarmonyLib;
using UnityEngine;

namespace MouseAimMod;

[HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]
public static class PilotPlayerStatePatch
{
    private static bool pidsInitialized;
    private static float _mpcPrevUP, _mpcPrevUR, _mpcPrevUY;
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
        if (!aimActive) { pidsInitialized = false; return; }
        if (Camera.main == null) return;

        Vector3 viewDirection = Camera.main.transform.forward;
        Vector3 localTarget = Quaternion.Inverse(aircraft.transform.rotation) * viewDirection;
        float hd = localTarget.x, vd = localTarget.y;
        Vector3 av = aircraft.transform.InverseTransformDirection(aircraft.rb.angularVelocity);

        if (localTarget.z < 0f) vd = Mathf.Sign(vd);
        float exp = Plugin.ErrorExp.Value;
        hd = Mathf.Sign(hd) * Mathf.Pow(Mathf.Abs(hd), exp);
        vd = Mathf.Sign(vd) * Mathf.Pow(Mathf.Abs(vd), exp);

        float pitchError = -vd, yawError = hd;
        float rollAngle = aircraft.transform.eulerAngles.z;
        if (rollAngle > 180f) rollAngle -= 360f;
        float rollError = hd;
        if (Plugin.RollCentering.Value)
        {
            float s = Mathf.Sin(rollAngle * Mathf.Deg2Rad);
            float f = 1f - Mathf.Clamp01(Mathf.Abs(hd) / Plugin.CenteringRange.Value);
            rollError = hd + s * f * Plugin.CenteringGain.Value;
        }

        if (!pidsInitialized) { _mpcPrevUP = _mpcPrevUR = _mpcPrevUY = 0f; pidsInitialized = true; }

        float dt = Time.fixedDeltaTime;
        float pO = Mpc(pitchError, av.x, dt, ref _mpcPrevUP, Plugin.MpcK_P.Value, Plugin.MpcHorizon.Value, Plugin.MpcIter.Value, Plugin.MpcPenalty_P.Value);
        float rO = Mpc(rollError,  av.z, dt, ref _mpcPrevUR, Plugin.MpcK_R.Value, Plugin.MpcHorizon.Value, Plugin.MpcIter.Value, Plugin.MpcPenalty_R.Value);
        float yO = Mpc(yawError,  av.y, dt, ref _mpcPrevUY, Plugin.MpcK_Y.Value, Plugin.MpcHorizon.Value, Plugin.MpcIter.Value, Plugin.MpcPenalty_Y.Value);

        float ys = 1f;
        if (Plugin.RollYawBalance.Value)
        {
            float d = Vector3.Angle(aircraft.transform.forward, Camera.main.transform.forward);
            ys = 1f - Mathf.Clamp01((d - Plugin.YawAttenStart.Value) / (Plugin.YawAttenEnd.Value - Plugin.YawAttenStart.Value));
        }
        yO *= ys;

        Plugin.PushDebugData(pitchError, rollError, yawError, pO, rO, yO);

        if (controlInputs.pitch == 0f) controlInputs.pitch = pO;
        if (controlInputs.roll  == 0f) controlInputs.roll  = rO;
        if (controlInputs.yaw   == 0f) controlInputs.yaw   = yO;

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
        for (int f = 0; f < horizon; f++) { w += k * (u - w) * dt; e -= w * dt; }
        float cost = e * e + w * w * dt * dt;
        if (Mathf.Abs(errorRad) > 0.005f && Mathf.Sign(e) != Mathf.Sign(errorRad)) cost += e * e * penalty;
        return cost;
    }

    static float Mpc(float errorSin, float omega, float dt, ref float uPrev, float k, int horizon, int iters, float penalty)
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
        uPrev = (lo + hi) * 0.5f;
        return uPrev;
    }
}
