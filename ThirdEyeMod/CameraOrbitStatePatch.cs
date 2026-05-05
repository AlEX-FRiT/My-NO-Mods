using HarmonyLib;
using UnityEngine;

namespace ThirdEyeMod;

[HarmonyPatch(typeof(CameraOrbitState))]
public static class CameraOrbitStatePatch
{
    [HarmonyPatch("CameraMotion")]
    [HarmonyPrefix]
    public static bool CameraMotionPrefix(
        CameraOrbitState __instance,
        CameraStateManager cam,
        ref float ___panView,
        ref float ___tiltView)
    {
        if (!Plugin.Enabled.Value)
            return true;

        // Clamp
        if (___panView >  180f) ___panView -= 360f;
        if (___panView < -180f) ___panView += 360f;
        if (___tiltView >  89f) ___tiltView =  89f;
        if (___tiltView < -89f) ___tiltView = -89f;

        // Rotation: mouse-driven
        cam.transform.rotation = Quaternion.Euler(___tiltView, ___panView, 0f);

        // Position: behind and above aircraft, offset by yaw and pitch
        float cameraPitch = cam.transform.eulerAngles.x;
        if (cameraPitch > 180f) cameraPitch -= 360f;
        float lookdownAngle = cameraPitch + Plugin.CameraAngle.Value;
        float cameraDistance = Plugin.CameraDistance.Value;

        Quaternion horizontalRot = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
        Quaternion elevationRot = Quaternion.Euler(lookdownAngle, 0f, 0f);
        Vector3 behindAndAbove = elevationRot * Vector3.back;

        cam.transform.position = cam.cameraPivot.position
            + horizontalRot * behindAndAbove * cameraDistance;

        return false;
    }
}
