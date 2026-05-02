using HarmonyLib;
using UnityEngine;

namespace SureFireMod;

[HarmonyPatch(typeof(HUDUnitMarker), "UpdatePosition")]
public static class HUDUnitMarker_UpdatePosition_Patch
{
    [HarmonyPostfix]
    public static void Postfix(HUDUnitMarker __instance, FactionHQ hq)
    {
        if (!Plugin.Enabled.Value) return;
        if (!__instance.selected) return;
        if (__instance.unit == null) return;
        if (!__instance.image || !__instance.image.enabled) return;

        var unit = __instance.unit;

        if (unit.HasRadarEmission() && unit.radar is Radar radar && radar.IsJammed())
            return;

        if (hq.IsTargetLased(unit))
        {
            float pulse = (Mathf.Sin(Time.timeSinceLevelLoad * 16f) + 1f) * 0.5f;
            __instance.image.color = Color.Lerp(Color.red, Color.green, pulse);
        }
    }
}
