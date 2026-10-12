using HarmonyLib;
using UnityEngine;

namespace MouseAimMod;

[HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]
public static class PilotPlayerStatePatch
{
    private static bool _stabilityKilled;
    private static bool _stabilityOrig;
    private static float _prevMixPitch;
    private static float _prevMixRoll;
    private static float _prevMixYaw;
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

        // 误差提取（符号与已验证的现行律一致）
        float horizDev = localTarget.x;
        float vertDev  = localTarget.y;
        if (localTarget.z < 0f) vertDev = Mathf.Sign(vertDev);
        float errPitchCur  = -vertDev;
        float errYawCur    = horizDev;
        float errRollCur   = horizDev;
        float errRollLevel = Mathf.Clamp(aircraft.transform.right.y, -1f, 1f);

        // A：yaw 管水平 / pitch 管垂直 / roll 改平 ; B：roll 管水平 / pitch 按 roll 权重压制 / yaw=0
        float wN = Mathf.Max(1f, Plugin.ModePitchN.Value);
        Vector3 errA = new Vector3(errPitchCur, errRollLevel, errYawCur);
        Vector3 errB = new Vector3(errPitchCur * Mathf.Pow(1f - Mathf.Abs(errRollCur), wN), errRollCur, 0f);

        Vector3 uA = new Vector3(
            ProportionalControl.Prop(errA.x, Plugin.PropGainPitch.Value),
            ProportionalControl.Prop(errA.y, Plugin.PropGainRoll.Value),
            ProportionalControl.Prop(errA.z, Plugin.PropGainYaw.Value));
        Vector3 uB = new Vector3(
            ProportionalControl.Prop(errB.x, Plugin.PropGainPitch.Value),
            ProportionalControl.Prop(errB.y, Plugin.PropGainRoll.Value),
            ProportionalControl.Prop(errB.z, Plugin.PropGainYaw.Value));

        float z = Mathf.Clamp01(1f - localTarget.z);
        float wB = Mathf.Clamp01((z - Plugin.ModeBlendX.Value) / Mathf.Max(1e-4f, Plugin.ModeBlendY.Value - Plugin.ModeBlendX.Value));
        float wA = 1f - wB;
        if (Plugin.ModeForce.Value == AimMode.A) { wA = 1f; wB = 0f; }
        else if (Plugin.ModeForce.Value == AimMode.B) { wA = 0f; wB = 1f; }
        Vector3 u = wA * uA + wB * uB;

        // lead 后级：每轴一次，只对"纯 u"求导（prev 存超前前的 u）
        float dt = Time.fixedDeltaTime > 1e-5f ? Time.fixedDeltaTime : 0.02f;
        float lead = Plugin.LeadEnabled.Value ? Plugin.LeadScale.Value : 0f;
        float pitchOut = Lead(u.x, lead, LagEstimator.Tau[0], dt, ref _prevMixPitch);
        float rollOut  = Lead(u.y, lead, LagEstimator.Tau[1], dt, ref _prevMixRoll);
        float yawOut   = Lead(u.z, lead, LagEstimator.Tau[2], dt, ref _prevMixYaw);

        if (Plugin.AimPitch.Value && controlInputs.pitch == 0f) controlInputs.pitch = pitchOut;
        if (Plugin.AimRoll.Value  && controlInputs.roll  == 0f) controlInputs.roll  = rollOut;
        if (Plugin.AimYaw.Value   && controlInputs.yaw   == 0f) controlInputs.yaw   = yawOut;

        Vector3 omega = aircraft.transform.InverseTransformDirection(aircraft.rb.angularVelocity);
        LagEstimator.NoteAircraft(aircraft.GetInstanceID());
        LagEstimator.Sample(0, controlInputs.pitch, omega.x);
        LagEstimator.Sample(1, controlInputs.roll, omega.z);
        LagEstimator.Sample(2, controlInputs.yaw, omega.y);

        Plugin.PushDebugData(controlInputs.pitch, controlInputs.roll, controlInputs.yaw);
        Plugin.PushDebugTau(LagEstimator.Tau[0], LagEstimator.Tau[1], LagEstimator.Tau[2]);
        Plugin.PushDebugCoord(controlInputs.yaw, controlInputs.pitch, controlInputs.roll, -1f);

        if (keyboardPitch && Plugin.StabilityKbEnabled.Value && !_stabilityKilled)
        {
            _stabilityOrig = aircraft.flightAssist;
            aircraft.flightAssist = false;
            _stabilityKilled = true;
        }
    }

    private static float Lead(float u, float scale, float tau, float dt, ref float prev)
    {
        float o = u;
        if (scale > 0f && tau > 0.02f) o = Mathf.Clamp(u + scale * tau * (u - prev) / dt, -1f, 1f);
        prev = u;
        return o;
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
