using HarmonyLib;
using UnityEngine;

namespace ThirdEyeMod;

[HarmonyPatch(typeof(GameplayUI), "ResumeGame")]
internal class ResumeHudPatch
{
    [HarmonyPostfix]
    private static void Postfix(CameraStateManager ___cameraStateManager)
    {
        if (SceneSingleton<CombatHUD>.i.aircraft != null
            && CameraStateManager.cameraMode == CameraMode.orbit)
        {
            FlightHud.EnableCanvas(true);
            DynamicMap.EnableCanvas(true);
        }
    }
}
