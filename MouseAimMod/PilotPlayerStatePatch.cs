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

        float kpMul = 1f;
        if (Plugin.SchedEnabled.Value)
        {
            float speed = aircraft.rb.velocity.magnitude;
            float altitude = aircraft.transform.position.y;
            float rho = IsaDensity(altitude);
            float q = 0.5f * rho * speed * speed;
            float ratio = Plugin.SchedRefQ.Value / Mathf.Max(q, 1f);
            kpMul = Mathf.Clamp(Mathf.Pow(ratio, Plugin.SchedExp.Value),
                Plugin.SchedClampMin.Value, Plugin.SchedClampMax.Value);
            pitchError *= kpMul;
            rollError  *= kpMul;
            yawError   *= kpMul;
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

    private static float IsaDensity(float altitudeM)
    {
        const float rho0 = 1.225f;
        const float t0 = 288.15f;
        const float lapse = 0.0065f;
        const float g = 9.80665f;
        const float r = 287.05f;
        const float hTrop = 11000f;

        float alt = Mathf.Max(altitudeM, 0f);
        if (alt <= hTrop)
        {
            float t = t0 - lapse * alt;
            return rho0 * Mathf.Pow(t / t0, (g / (r * lapse)) - 1f);
        }
        else
        {
            float tTrop = t0 - lapse * hTrop;
            float rhoTrop = rho0 * Mathf.Pow(tTrop / t0, (g / (r * lapse)) - 1f);
            return rhoTrop * Mathf.Exp(-g * (alt - hTrop) / (r * tTrop));
        }
    }
}
