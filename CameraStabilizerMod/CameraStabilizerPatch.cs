using HarmonyLib;
using UnityEngine;

namespace CameraStabilizerMod;

[HarmonyPatch(typeof(CameraCockpitState))]
public static class CameraStabilizerPatch
{
    [HarmonyPatch("EnterState")]
    [HarmonyPostfix]
    public static void EnterStatePostfix(CameraStateManager cam)
    {
        StabilizerState.Capture(cam);
    }

    [HarmonyPatch("UpdateState")]
    [HarmonyPrefix]
    public static void UpdateStatePrefix(CameraStateManager cam, Aircraft ___aircraft, ref float ___panView, ref float ___tiltView)
    {
        if (!StabilizerState.Active(cam, ___aircraft)) return;
        StabilizerState.Seed(cam, ref ___panView, ref ___tiltView);
    }

    [HarmonyPatch("UpdateState")]
    [HarmonyPostfix]
    public static void UpdateStatePostfix(CameraStateManager cam, Aircraft ___aircraft, float ___panView, float ___tiltView)
    {
        if (!StabilizerState.Active(cam, ___aircraft)) return;
        StabilizerState.Save(cam, ___panView, ___tiltView);
    }
}

internal static class StabilizerState
{
    private static Quaternion _targetWorld;
    private static bool _hasTarget;

    public static void Capture(CameraStateManager cam)
    {
        if (cam == null) { _hasTarget = false; return; }
        _targetWorld = cam.transform.rotation;
        _hasTarget = true;
    }

    public static bool Active(CameraStateManager cam, Aircraft aircraft)
    {
        if (!Plugin.IsEnabled) return false;
        if (aircraft == null) return false;
        if (cam.currentState != cam.cockpitState) return false;
        if (!GameManager.flightControlsEnabled) return false;
        if (PlayerSettings.useTrackIR && PlayerSettings.headTrackerType != HeadTrackerType.None) return false;
        return true;
    }

    public static void Seed(CameraStateManager cam, ref float panView, ref float tiltView)
    {
        if (!_hasTarget) { _targetWorld = cam.transform.rotation; _hasTarget = true; }
        Quaternion parentRot = cam.transform.parent.rotation;
        Vector3 e = (Quaternion.Inverse(parentRot) * _targetWorld).eulerAngles;
        panView = Mathf.DeltaAngle(0f, e.y);
        tiltView = Mathf.DeltaAngle(0f, e.x);
    }

    public static void Save(CameraStateManager cam, float panView, float tiltView)
    {
        _targetWorld = cam.transform.parent.rotation * Quaternion.Euler(tiltView, panView, 0f);
    }
}
