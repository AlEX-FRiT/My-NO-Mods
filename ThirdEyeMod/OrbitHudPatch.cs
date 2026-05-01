using HarmonyLib;
using UnityEngine;

namespace ThirdEyeMod;

[HarmonyPatch(typeof(CameraOrbitState), "EnterState")]
internal class OrbitHudPatch
{
    [HarmonyPostfix]
    private static void Postfix(CameraOrbitState __instance, CameraStateManager cam)
    {
        if (SceneSingleton<CombatHUD>.i.aircraft != null
            && (CameraStateManager.cameraMode == CameraMode.cockpit
                || CameraStateManager.cameraMode == CameraMode.orbit))
        {
            FlightHud.EnableCanvas(true);
            DynamicMap.EnableCanvas(true);
        }

        if (CameraStateManager.cameraMode == CameraMode.orbit
            && cam.followingUnit != null)
        {
            var rot = cam.followingUnit.rb.transform.eulerAngles;
            float yaw = rot.y;
            float pitch = rot.x;
            if (pitch > 180f) pitch -= 360f;
            var trv = Traverse.Create(__instance);
            trv.Field("panView").SetValue(yaw);
            trv.Field("tiltView").SetValue(pitch);
        }
    }
}
