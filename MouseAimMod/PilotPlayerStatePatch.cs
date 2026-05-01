using HarmonyLib;
using UnityEngine;

namespace MouseAimMod;

[HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]
public static class PilotPlayerStatePatch
{
    internal static PID PitchPID = new PID(0.025f, 0.01f, 0.005f);
    internal static PID RollPID = new PID(0.03f, 0.005f, 0.002f);
    internal static PID YawPID = new PID(0.1f, 0.1f, 0.05f);
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

        if (Mathf.Abs(pitch) > 15f || Mathf.Abs(roll) > 15f)
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

        var controlInputs = ___controlInputs;
        if (controlInputs == null)
            return;

        bool canHover = aircraft.GetAircraftParameters().verticalLanding;

        if (HoverThrottleController.HoverActive && canHover)
        {
            HoverThrottleController.ApplyHoverThrottle(controlInputs, aircraft);
        }

        if (freeLook)
        {
            pidsInitialized = false;
            return;
        }

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
        float centeringFactor = 1f - Mathf.Clamp01(Mathf.Abs(horizontalDeviation) / 7.5f);
        float rollError = horizontalDeviation + rollAngle * centeringFactor;

        if (!pidsInitialized)
        {
            PitchPID.Reseti();
            RollPID.Reseti();
            YawPID.Reseti();
            pidsInitialized = true;
        }

        float pitchOutput = PitchPID.GetOutput(pitchError, pitchRate, 2f, Time.fixedDeltaTime);
        float rollOutput = RollPID.GetOutput(rollError, -rollRate, 2f, Time.fixedDeltaTime);
        float yawOutput = YawPID.GetOutput(yawError, -yawRate, 2f, Time.fixedDeltaTime);

        float pitchInput = Mathf.Clamp(pitchOutput, -1f, 1f);
        float rollInput = Mathf.Clamp(rollOutput, -1f, 1f);
        float yawInput = Mathf.Clamp(yawOutput, -1f, 1f);

        if (controlInputs.pitch == 0f)
            controlInputs.pitch = pitchInput;
        if (controlInputs.roll == 0f)
            controlInputs.roll = rollInput;
        if (controlInputs.yaw == 0f)
            controlInputs.yaw = yawInput;
    }
}
