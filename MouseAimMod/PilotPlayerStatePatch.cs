using HarmonyLib;
using UnityEngine;

namespace MouseAimMod;

[HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]
public static class PilotPlayerStatePatch
{
    private static bool _stabilityKilled;
    private static bool _stabilityOrig;
    private static Vector3 _viewFilt;
    private static bool _filtInit;

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

        // 目标输入：视线方向先低通滤波（世界系），再转机体坐标系
        Vector3 viewRaw = Camera.main.transform.forward;
        float fTau = Plugin.TargetFilterTau.Value;
        if (fTau > 0.001f)
        {
            float fdt = Time.fixedDeltaTime > 1e-5f ? Time.fixedDeltaTime : 0.02f;
            if (!_filtInit) { _viewFilt = viewRaw; _filtInit = true; }
            _viewFilt += (fdt / (fTau + fdt)) * (viewRaw - _viewFilt);
        }
        else { _viewFilt = viewRaw; _filtInit = false; }
        Vector3 viewDirection = _viewFilt.normalized;
        Vector3 localTarget = Quaternion.Inverse(aircraft.transform.rotation) * viewDirection;

        // 视线在右-上平面的投影 B=(x,y,0)。后半球(z<0)时沿 O->B 方向把 B 移到单位圆上得 B'(|OB'|=1,方向不变)，
        // 再代入同一套几何：
        //   rollErr = ∠OAB  = atan2(B'.x, B'.y+1-|OB'|)   (AB 与垂直轴夹角)
        //   yawErr  = |AO|·∠OAB = (1-|OB'|)·rollErr       (以 A 为心、AO 为半径的弧长)
        //   pitchErr = -B'.y
        float dx = localTarget.x;
        float dy = localTarget.y;
        float bLen = Mathf.Sqrt(dx * dx + dy * dy);
        if (localTarget.z < 0f && bLen > 0f)
        {
            dx /= bLen;
            dy /= bLen;
            bLen = 1f;
        }
        float rollErr = Mathf.Atan2(dx, dy + 1f - bLen);
        float yawErr = (1f - bLen) * rollErr;
        float pitchErr = -dy;

        float gp = Plugin.PropGainPitch.Value, gy = Plugin.PropGainYaw.Value, gr = Plugin.PropGainRoll.Value;
        if (Plugin.Adaptive.Value)
        {
            if (LagEstimator.Valid[0]) gp = KpFromFit(LagEstimator.Gain[0], LagEstimator.Tau[0], LagEstimator.Theta[0]);
            if (LagEstimator.Valid[1]) gr = KpFromFit(LagEstimator.Gain[1], LagEstimator.Tau[1], LagEstimator.Theta[1]);
            if (LagEstimator.Valid[2]) gy = KpFromFit(LagEstimator.Gain[2], LagEstimator.Tau[2], LagEstimator.Theta[2]);
        }
        float tP = Mathf.Clamp(LagEstimator.Tau[0], Plugin.AdaptiveTauMin.Value, Plugin.AdaptiveTauMax.Value);
        float tR = Mathf.Clamp(LagEstimator.Tau[1], Plugin.AdaptiveTauMin.Value, Plugin.AdaptiveTauMax.Value);
        float tY = Mathf.Clamp(LagEstimator.Tau[2], Plugin.AdaptiveTauMin.Value, Plugin.AdaptiveTauMax.Value);

        Vector3 u = new Vector3(
            ProportionalControl.Prop(pitchErr, gp),
            ProportionalControl.Prop(rollErr, gr),
            ProportionalControl.Prop(yawErr, gy));

        // 速率反馈：u -= Kd·ω_corrective, Kd = FeedbackGain·GainAxis·τ（替代旧指令超前 lead）
        // Kd 独立设上限：Kd 随 Kp·τ 增长会在带延迟的 roll/yaw 回路上激起极限环（日志 19:02 实测）
        Vector3 omega = aircraft.transform.InverseTransformDirection(aircraft.rb.angularVelocity);
        float fg = Plugin.FeedbackGain.Value;
        float kdMax = Plugin.AdaptiveKdMax.Value;
        float kdP = Mathf.Min(fg * gp * tP, kdMax);
        float kdR = Mathf.Min(fg * gr * tR, kdMax);
        float kdY = Mathf.Min(fg * gy * tY, kdMax);
        float pitchOut = Mathf.Clamp(u.x - kdP * omega.x,    -1f, 1f);
        float rollOut  = Mathf.Clamp(u.y - kdR * (-omega.z), -1f, 1f);
        float yawOut   = Mathf.Clamp(u.z - kdY * omega.y,    -1f, 1f);

        bool anyManualInput = controlInputs.pitch != 0f || controlInputs.roll != 0f || controlInputs.yaw != 0f;
        if (!anyManualInput)
        {
            if (Plugin.AimPitch.Value) controlInputs.pitch = pitchOut;
            if (Plugin.AimRoll.Value)  controlInputs.roll  = rollOut;
            if (Plugin.AimYaw.Value)   controlInputs.yaw   = yawOut;
        }

        LagEstimator.NoteAircraft(aircraft.GetInstanceID());
        LagEstimator.Sample(0, controlInputs.pitch, omega.x);
        LagEstimator.Sample(1, controlInputs.roll, omega.z);
        LagEstimator.Sample(2, controlInputs.yaw, omega.y);

        Plugin.PushDebugData(controlInputs.pitch, controlInputs.roll, controlInputs.yaw);
        Plugin.PushDebugErr(pitchErr, rollErr, yawErr);
        Plugin.PushDebugTau(LagEstimator.Tau[0], LagEstimator.Tau[1], LagEstimator.Tau[2]);
        Plugin.PushDebugCoord(controlInputs.yaw, controlInputs.pitch, controlInputs.roll, -1f);

        if (keyboardPitch && Plugin.StabilityKbEnabled.Value && !_stabilityKilled)
        {
            _stabilityOrig = aircraft.flightAssist;
            aircraft.flightAssist = false;
            _stabilityKilled = true;
        }
    }

    private static float KpFromFit(float K, float tau, float theta)
    {
        float th = Mathf.Max(theta, Plugin.AdaptiveThetaMin.Value);
        float kk = Mathf.Max(Mathf.Abs(K), 0.05f);
        return Mathf.Clamp(Plugin.AdaptiveWc.Value / (kk * th), Plugin.AdaptiveKpMin.Value, Plugin.AdaptiveKpMax.Value);
    }

    [HarmonyPatch(typeof(Aircraft), "FilterInputs")]
    [HarmonyPostfix]
    private static void FilterInputsPostfix(Aircraft __instance)
    {
        if (_stabilityKilled) { __instance.flightAssist = _stabilityOrig; _stabilityKilled = false; }
    }
}

/// <summary>
/// 共享的纯比例映射：u = clamp(err * gain, -1, 1)。三轴算法相同。
/// </summary>
public static class ProportionalControl
{
    public static float Prop(float err, float gain) => Mathf.Clamp(err * gain, -1f, 1f);
}
