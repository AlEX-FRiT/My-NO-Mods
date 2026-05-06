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

        float horizontalDeviation = Mathf.Asin(Mathf.Clamp(localTarget.x, -1f, 1f)) * Mathf.Rad2Deg;
        float verticalDeviation   = Mathf.Asin(Mathf.Clamp(localTarget.y, -1f, 1f)) * Mathf.Rad2Deg;

        if (localTarget.z < 0f)
        {
            verticalDeviation = Mathf.Sign(verticalDeviation) * 90f;
        }

        Vector3 angularVel = aircraft.transform.InverseTransformDirection(aircraft.rb.angularVelocity) * Mathf.Rad2Deg;
        float pitchRate = angularVel.x;
        float yawRate = angularVel.y;
        float rollRate = angularVel.z;

        float pitchError = -verticalDeviation;
        float yawError = horizontalDeviation;

        float rollAngle = aircraft.transform.eulerAngles.z;
        if (rollAngle > 180f) rollAngle -= 360f;

        float rollError = horizontalDeviation;
        if (Plugin.RollCentering.Value)
        {
            float centeringFactor = 1f - Mathf.Clamp01(Mathf.Abs(horizontalDeviation) / Plugin.CenteringRange.Value);
            rollError = horizontalDeviation + rollAngle * centeringFactor * Plugin.CenteringGain.Value;
        }

        if (!pidsInitialized)
        {
            PitchPID.Reseti();
            RollPID.Reseti();
            YawPID.Reseti();
            pidsInitialized = true;
        }

        float pitchOutput = PitchPID.GetOutput(pitchError, pitchRate, Plugin.PitchIThreshold.Value, Time.fixedDeltaTime);
        float rollOutput = RollPID.GetOutput(rollError, -rollRate, Plugin.RollIThreshold.Value, Time.fixedDeltaTime);
        float yawOutput = YawPID.GetOutput(yawError, -yawRate, Plugin.YawIThreshold.Value, Time.fixedDeltaTime);

        ClampPI(Traverse.Create((object)PitchPID), Plugin.PitchILimit.Value);
        ClampPI(Traverse.Create((object)RollPID), Plugin.RollILimit.Value);
        ClampPI(Traverse.Create((object)YawPID), Plugin.YawILimit.Value);

        float yawScale = 1f;
        if (Plugin.RollYawBalance.Value)
        {
            float totalDev = Vector3.Angle(aircraft.transform.forward, Camera.main.transform.forward);
            yawScale = 1f - Mathf.Clamp01((totalDev - Plugin.YawAttenStart.Value) / (Plugin.YawAttenEnd.Value - Plugin.YawAttenStart.Value));
        }
        yawOutput *= yawScale;

        float pitchInput = Mathf.Clamp(pitchOutput * Plugin.PitchScale.Value, -1f, 1f);
        float rollInput = Mathf.Clamp(rollOutput * Plugin.RollScale.Value, -1f, 1f);
        float yawInput = Mathf.Clamp(yawOutput * Plugin.YawScale.Value, -1f, 1f);

        if (controlInputs.pitch == 0f)
            controlInputs.pitch = pitchInput;
        if (controlInputs.roll == 0f)
            controlInputs.roll = rollInput;
        if (controlInputs.yaw == 0f)
            controlInputs.yaw = yawInput;
    }

    private static void ClampPI(Traverse pid, float limit)
    {
        float i = pid.Field("i").GetValue<float>();
        i = Mathf.Clamp(i, -limit, limit);
        pid.Field("i").SetValue(i);
    }
}
