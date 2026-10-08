using HarmonyLib;
using UnityEngine;

namespace MouseAimMod;

[HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]
public static class PilotPlayerStatePatch
{
    private static bool _stabilityKilled;
    private static bool _stabilityOrig;

    [HarmonyPatch("PlayerControls")]
    [HarmonyPostfix]
    public static void PlayerControlsPostfix(PilotPlayerState __instance, Pilot ___pilot)
    {
        if (!Plugin.MouseAimEnabled.Value || !Plugin.StabilizerEnabled()) return;
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
        if (!Plugin.MouseAimEnabled.Value || !Plugin.StabilizerEnabled()) return;
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
        if (!aimActive) return;
        if (Camera.main == null) return;

        // 目标输入：视线方向在机体坐标系下的目标方位向量
        Vector3 viewDirection = Camera.main.transform.forward;
        Vector3 localTarget = Quaternion.Inverse(aircraft.transform.rotation) * viewDirection;
        Vector3 localVelocityDir = aircraft.transform.InverseTransformDirection(aircraft.rb.velocity.normalized);

        Vector3 control = ProportionalControl.ComputeControlFromTarget(localTarget, localVelocityDir);
        float pitchOut = control.x;
        float yawOut   = control.y;
        float rollOut  = control.z;

        Plugin.PushDebugData(-localTarget.y, localTarget.x, localTarget.x, pitchOut, rollOut, yawOut);
        Plugin.PushDebugCoord(yawOut, pitchOut, rollOut, -1f);

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
}

/// <summary>
/// 简易比例映射：将机体坐标系下的目标方位转化为控制量（输入目标，输出控制量）。
/// </summary>
public static class ProportionalControl
{
    /// <param name="localVelocityDir">归一化后的速度矢量在机体坐标系下的表示（各分量与 localTarget 含义一致）。</param>
    /// <returns>控制量 (pitch, yaw, roll)，各分量已夹取到 [-1, 1]。</returns>
    public static Vector3 ComputeControlFromTarget(Vector3 localTarget, Vector3 localVelocityDir)
    {
        float horizDev = localTarget.x;
        float vertDev  = localTarget.y;
        // 目标位于机身后方时，垂直偏差方向取反
        if (localTarget.z < 0f) vertDev = Mathf.Sign(vertDev);

        // u = Kp * e
        float pitch = Mathf.Clamp(-vertDev * Plugin.PropGainPitch.Value, -1f, 1f);
        float yaw   = Mathf.Clamp( horizDev * Plugin.PropGainYaw.Value,   -1f, 1f);

        // roll = X·wX - Y·wY，X/Y 为目标/速度在水平面内偏离机头轴线的归一化分量（右为正）
        float t = Mathf.Max(Plugin.RollBlend.Value, 0.0001f);
        float X = Mathf.Clamp(horizDev, -1f, 1f);
        float Y = Mathf.Clamp(localVelocityDir.x, -1f, 1f);
        float ax = Mathf.Abs(X), ay = Mathf.Abs(Y);
        float wX = ax >= t ? 1f : ax / t;
        float wY = ay >= t ? 0f : 1f - ay / t;
        float roll = Mathf.Clamp(X * wX - Y * wY, -1f, 1f);

        return new Vector3(pitch, yaw, roll);
    }
}
