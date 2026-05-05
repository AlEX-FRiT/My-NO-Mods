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

        float horizontalDeviation = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;
        float verticalDeviation = Mathf.Atan2(localTarget.y, localTarget.z) * Mathf.Rad2Deg;

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

        float ith = Plugin.IThreshold.Value;
        float pitchOutput = PitchPID.GetOutput(pitchError, pitchRate, ith, Time.fixedDeltaTime);
        float rollOutput = RollPID.GetOutput(rollError, -rollRate, ith, Time.fixedDeltaTime);
        float yawOutput = YawPID.GetOutput(yawError, -yawRate, ith, Time.fixedDeltaTime);

        float pitchInput = Mathf.Clamp(pitchOutput, -1f, 1f);
        float rollInput = Mathf.Clamp(rollOutput, -1f, 1f);
        float yawInput = Mathf.Clamp(yawOutput, -1f, 1f);

        float scale = Plugin.OutputScale.Value;
        pitchInput *= scale;
        rollInput *= scale;
        yawInput *= scale;

        if (controlInputs.pitch == 0f)
            controlInputs.pitch = pitchInput;
        if (controlInputs.roll == 0f)
            controlInputs.roll = rollInput;
        if (controlInputs.yaw == 0f)
            controlInputs.yaw = yawInput;
    }
}
