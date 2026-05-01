using HarmonyLib;
using UnityEngine;

namespace ThirdEyeMod;

[HarmonyPatch(typeof(DynamicMap), "Maximize")]
internal class MinimapMaxHudPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (SceneSingleton<CombatHUD>.i.aircraft != null
            && (CameraStateManager.cameraMode == CameraMode.cockpit
                || CameraStateManager.cameraMode == CameraMode.orbit))
        {
            FlightHud.EnableCanvas(true);
            DynamicMap.EnableCanvas(true);
        }
    }
}

[HarmonyPatch(typeof(DynamicMap), "Minimize")]
internal class MinimapMinHudPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (SceneSingleton<CombatHUD>.i.aircraft != null
            && (CameraStateManager.cameraMode == CameraMode.cockpit
                || CameraStateManager.cameraMode == CameraMode.orbit))
        {
            FlightHud.EnableCanvas(true);
            DynamicMap.EnableCanvas(true);
        }
    }
}
