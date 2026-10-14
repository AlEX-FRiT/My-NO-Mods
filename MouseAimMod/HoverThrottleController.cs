using HarmonyLib;
using UnityEngine;

namespace MouseAimMod;

public static class HoverThrottleController
{
    public static bool HoverActive { get; private set; }

    public static void ResetState()
    {
        HoverActive = false;
        needsInit = true;
    }

    private static float throttleOverride;
    private static float hoverBaseThrottle = 0.5f;
    private static float climbSensitivity = 8f;
    private static PIDFactors altitudePIDFactors;
    private static PID altitudePID;
    private static bool needsInit = true;

    public static void Enable(PilotPlayerState pilotPlayerState)
    {
        HoverActive = true;
        needsInit = true;
        float rawMid = PlayerSettings.throttleUseNegative ? 0f : 0.5f;
        Traverse.Create(pilotPlayerState).Field("simulatedThrottle").SetValue(rawMid);
        SceneSingleton<AircraftActionsReport>.i.ReportText("Hover Throttle <b>Enabled</b>", 3f);
    }

    public static void Disable(PilotPlayerState pilotPlayerState)
    {
        ResetState();
        float raw = PlayerSettings.throttleUseNegative ? (throttleOverride * 2f - 1f) : throttleOverride;
        Traverse.Create(pilotPlayerState).Field("simulatedThrottle").SetValue(raw);
        SceneSingleton<AircraftActionsReport>.i.ReportText("Hover Throttle <b>Disabled</b>", 3f);
    }

    public static void ApplyHoverThrottle(ControlInputs controlInputs, Aircraft aircraft)
    {
        if (needsInit)
        {
            var filter = aircraft.GetControlsFilter();
            if (filter != null)
            {
                var autoHover = Traverse.Create(filter).Field("autoHover").GetValue();
                if (autoHover != null)
                {
                    hoverBaseThrottle = Traverse.Create(autoHover).Field("hoverBaseThrottle").GetValue<float>();
                    climbSensitivity = Traverse.Create(autoHover).Field("climbSensitivity").GetValue<float>();
                    altitudePIDFactors = Traverse.Create(autoHover).Field("altitudePIDFactors").GetValue<PIDFactors>();
                }
            }
            throttleOverride = controlInputs.throttle;
            altitudePID = new PID(altitudePIDFactors);
            needsInit = false;
        }

        float targetVS = (controlInputs.throttle - hoverBaseThrottle) * climbSensitivity;
        if (Mathf.Abs(controlInputs.throttle - hoverBaseThrottle) < 0.1f)
            targetVS = 0f;

        float vs = Vector3.Dot(aircraft.rb?.velocity ?? Vector3.zero, Vector3.up);
        float error = vs - targetVS;
        float output = altitudePID.GetOutput(-error, 2f, Time.fixedDeltaTime,
            new Vector3(altitudePIDFactors.P, altitudePIDFactors.I, altitudePIDFactors.D));

        throttleOverride = Mathf.Clamp01(throttleOverride + output);

        // RPM 保护：旋翼转速低于标称(0.92*GetMaxRPM)时按差额拉回油门；涡环状态(VRS)反而加油门。
        // 系数对齐 AutopilotHelo.BoresightAim (15561-15563)，玩家 AutoHover 本身没有这个保护。
        float avgRpm = 0f, nominalRpm = 0f, vrs = 0f;
        int rotors = 0;
        foreach (var engine in aircraft.engines)
        {
            if (engine is RotorShaft rs)
            {
                avgRpm += rs.GetRPM();
                nominalRpm += rs.GetMaxRPM() * 0.92f;
                vrs += rs.GetVRSFactor();
                rotors++;
            }
        }
        if (rotors > 0)
        {
            float guard = Mathf.Min((avgRpm - nominalRpm) / rotors * Plugin.HeliRpmGuardGain.Value, 0f);
            guard += vrs / rotors * 2f;
            throttleOverride = Mathf.Clamp01(throttleOverride + guard);
        }

        controlInputs.throttle = throttleOverride;
    }
}
