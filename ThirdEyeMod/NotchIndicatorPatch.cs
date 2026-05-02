using HarmonyLib;
using UnityEngine;

namespace ThirdEyeMod;

[HarmonyPatch(typeof(ThreatItem), "AlignNotchIndicator")]
internal class NotchIndicatorPatch
{
    [HarmonyPostfix]
    private static void Postfix(ThreatItem __instance)
    {
        if (CameraStateManager.cameraMode != CameraMode.orbit)
            return;

        var trv = Traverse.Create(__instance);
        var notchIndicator = trv.Field("notchIndicator").GetValue<GameObject>();
        if (notchIndicator == null) return;

        var aircraft = SceneSingleton<CombatHUD>.i.aircraft;
        if (aircraft == null) return;

        var cam = SceneSingleton<CameraStateManager>.i;
        var old = notchIndicator.transform.eulerAngles;
        notchIndicator.transform.eulerAngles = new Vector3(0, 0,
            old.z + aircraft.transform.eulerAngles.z - cam.mainCamera.transform.eulerAngles.z);
    }
}
