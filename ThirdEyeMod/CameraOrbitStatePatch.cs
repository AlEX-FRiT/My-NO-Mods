using HarmonyLib;
using UnityEngine;

namespace ThirdEyeMod;

[HarmonyPatch(typeof(CameraOrbitState))]
public static class CameraOrbitStatePatch
{
    [HarmonyPatch("CameraMotion")]
    [HarmonyPrefix]
    public static bool CameraMotionPrefix(CameraOrbitState __instance, CameraStateManager cam)
    {
        if (!Plugin.Enabled.Value)
            return true;

        var trv = Traverse.Create(__instance);
        float panView  = trv.Field("panView").GetValue<float>();
        float tiltView = trv.Field("tiltView").GetValue<float>();
        if (panView >  180f) panView -= 360f;
        if (panView < -180f) panView += 360f;
        if (tiltView >  89f) tiltView =  89f;
        if (tiltView < -89f) tiltView = -89f;
        trv.Field("panView").SetValue(panView);
        trv.Field("tiltView").SetValue(tiltView);

        cam.transform.rotation = Quaternion.Euler(tiltView, panView, 0f);
        cam.transform.position = cam.cameraPivot.position
            + CameraMove(cam.transform.eulerAngles.y, cam.transform.eulerAngles.x);

        return false;
    }

    private static Vector3 CameraMove(float yawAngle, float pitchAngle)
    {
        // pitch
        float pitch = pitchAngle;
        if (pitch > 180f) pitch -= 360f;
        float normalized = (-pitch + 90f) / 180f;
        var upward = new Vector3(0f, Mathf.Pow(1 - normalized, Plugin.VerticalCurve.Value), 0f);
        // yaw
        float rad = yawAngle * Mathf.Deg2Rad;
        var backward = new Vector3(Mathf.Sin(rad) * Mathf.Pow(normalized, Plugin.HorizontalCurve.Value), 0f, Mathf.Cos(rad) * Mathf.Pow(normalized, Plugin.HorizontalCurve.Value));
        return (-backward + upward) * Plugin.CameraDistance.Value;
    }
}
