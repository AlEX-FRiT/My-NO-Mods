using HarmonyLib;
using UnityEngine;

namespace MouseAimMod;

[HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]
public static class PilotPlayerStatePatch
{
    internal static PID PitchPID;
    internal static PID RollPID;
    internal static PID YawPID;
    private static bool pidsInitialized;

    [HarmonyPatch("PlayerControls")]
    [HarmonyPostfix]
    public static void PlayerControlsPostfix(PilotPlayerState __instance, Pilot ___pilot)
    {
        if (!CameraAimState.MouseAimEnabled)
            return;

        if (PlayerSettings.virtualJoystickEnabled)
            return;

        if (!FlightHudStatePatch.CanvasEnabled)
            return;

        var pilot = ___pilot;
        if (pilot == null || pilot.aircraft == null)
            return;

        if (!pilot.aircraft.GetAircraftParameters().verticalLanding)
            return;

        var aircraft = pilot.aircraft;

        float pitch = Mathf.Asin(-aircraft.transform.forward.y) * Mathf.Rad2Deg;
        float roll  = Mathf.Asin( aircraft.transform.right.y)  * Mathf.Rad2Deg;

        if (Mathf.Abs(pitch) > Plugin.HoverExitAngle.Value || Mathf.Abs(roll) > Plugin.HoverExitAngle.Value)
        {
            if (HoverThrottleController.HoverActive)
            {
                HoverThrottleController.Disable(__instance);
            }
        }
        else if (GameManager.playerInput.GetButtonTimedPressUp("Brake", 0f, PlayerSettings.clickDelay))
        {
            if (!HoverThrottleController.HoverActive)
            {
                HoverThrottleController.Enable(__instance);
            }
            else
            {
                HoverThrottleController.Disable(__instance);
            }
        }
    }

    [HarmonyPostfix]
    public static void PlayerAxisControlsPostfix(
        PilotPlayerState __instance,
        Pilot ___pilot,
        ControlInputs ___controlInputs)
    {
        if (!CameraAimState.MouseAimEnabled)
            return;

        if (PlayerSettings.virtualJoystickEnabled)
            return;

        if (!FlightHudStatePatch.CanvasEnabled)
            return;

        bool freeLook = GameManager.playerInput.GetButton("Free Look");

        var pilot = ___pilot;
        if (pilot == null || pilot.aircraft == null)
            return;

        var aircraft = pilot.aircraft;
        if (aircraft.rb == null)
            return;

        var controlInputs = ___controlInputs;
        if (controlInputs == null)
            return;

        bool canHover = aircraft.GetAircraftParameters().verticalLanding;

        if (HoverThrottleController.HoverActive && canHover)
        {
            HoverThrottleController.ApplyHoverThrottle(controlInputs, aircraft);
        }

        bool aimActive = Plugin.InvertFreeLook.Value ? freeLook : !freeLook;
        if (!aimActive)
        {
            pidsInitialized = false;
            return;
        }

        if (Camera.main == null)
            return;

        Vector3 viewDirection = Camera.main.transform.forward;
        Vector3 localTarget = Quaternion.Inverse(aircraft.transform.rotation) * viewDirection;

        // Error signals are sin(angular deviation) — already in [-1,1] range
        float horizontalDeviation = localTarget.x;
        float verticalDeviation   = localTarget.y;

        Vector3 av = aircraft.transform.InverseTransformDirection(aircraft.rb.angularVelocity);
        float pitchD = -Mathf.Sin(av.x);
        float rollD  =  Mathf.Sin(av.z);
        float yawD   = -Mathf.Sin(av.y);

        if (localTarget.z < 0f)
        {
            verticalDeviation = Mathf.Sign(verticalDeviation);
        }

        float exp = Plugin.ErrorExp.Value;
        horizontalDeviation = Mathf.Sign(horizontalDeviation) * Mathf.Pow(Mathf.Abs(horizontalDeviation), exp);
        verticalDeviation   = Mathf.Sign(verticalDeviation)   * Mathf.Pow(Mathf.Abs(verticalDeviation),   exp);

        float pitchError = -verticalDeviation;
        float yawError = horizontalDeviation;

        float rollAngle = aircraft.transform.eulerAngles.z;
        if (rollAngle > 180f) rollAngle -= 360f;

        float rollError = horizontalDeviation;
        if (Plugin.RollCentering.Value)
        {
            float rollAngleSin = Mathf.Sin(rollAngle * Mathf.Deg2Rad);
            float centeringFactor = 1f - Mathf.Clamp01(Mathf.Abs(horizontalDeviation) / Plugin.CenteringRange.Value);
            rollError = horizontalDeviation + rollAngleSin * centeringFactor * Plugin.CenteringGain.Value;
        }

        if (!pidsInitialized)
        {
            PitchPID.Reseti();
            RollPID.Reseti();
            YawPID.Reseti();
            pidsInitialized = true;
        }

        float pitchOutput = PitchPID.GetOutput(pitchError, pitchD, Plugin.PitchIThreshold.Value, Time.fixedDeltaTime);
        float rollOutput = RollPID.GetOutput(rollError, rollD, Plugin.RollIThreshold.Value, Time.fixedDeltaTime);
        float yawOutput = YawPID.GetOutput(yawError, yawD, Plugin.YawIThreshold.Value, Time.fixedDeltaTime);

        pitchOutput = ClampWithBackCalc(Traverse.Create((object)PitchPID), pitchOutput, Plugin.PitchTrackingTc.Value, Time.fixedDeltaTime);
        rollOutput  = ClampWithBackCalc(Traverse.Create((object)RollPID),  rollOutput,  Plugin.RollTrackingTc.Value,  Time.fixedDeltaTime);
        yawOutput   = ClampWithBackCalc(Traverse.Create((object)YawPID),   yawOutput,   Plugin.YawTrackingTc.Value,   Time.fixedDeltaTime);

        float yawScale = 1f;
        if (Plugin.RollYawBalance.Value)
        {
            float totalDev = Vector3.Angle(aircraft.transform.forward, Camera.main.transform.forward);
            yawScale = 1f - Mathf.Clamp01((totalDev - Plugin.YawAttenStart.Value) / (Plugin.YawAttenEnd.Value - Plugin.YawAttenStart.Value));
        }
        yawOutput *= yawScale;

        float pitchScaled = pitchOutput * Plugin.PitchScale.Value;
        float rollScaled = rollOutput * Plugin.RollScale.Value;
        float yawScaled = yawOutput * Plugin.YawScale.Value;
        Plugin.PushDebugData(pitchError, rollError, yawError, pitchScaled, rollScaled, yawScaled);

        float pitchInput = pitchScaled;
        float rollInput = rollScaled;
        float yawInput = yawScaled;

        if (controlInputs.pitch == 0f)
            controlInputs.pitch = pitchInput;
        if (controlInputs.roll == 0f)
            controlInputs.roll = rollInput;
        if (controlInputs.yaw == 0f)
            controlInputs.yaw = yawInput;
    }

    private static float ClampWithBackCalc(Traverse pid, float rawOutput, float trackingTc, float dt)
    {
        float clamped = Mathf.Clamp(rawOutput, -1f, 1f);
        float cut = rawOutput - clamped;
        if (Mathf.Approximately(cut, 0f))
            return clamped;

        float backCalcGain = Mathf.Min(1f, dt / trackingTc);
        float i = pid.Field("i").GetValue<float>();
        i -= cut * backCalcGain;
        pid.Field("i").SetValue(i);
        return clamped;
    }
}
