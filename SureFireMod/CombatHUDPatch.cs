using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace SureFireMod;

[HarmonyPatch(typeof(CombatHUD), "ShowTargetInfo")]
public static class CombatHUD_ShowTargetInfo_Patch
{
    [HarmonyPostfix]
    public static void Postfix(CombatHUD __instance, bool __result)
    {
        if (!Plugin.Enabled.Value) return;
        if (!__result) return;
        if (__instance.aircraft == null) return;
        var hq = __instance.aircraft.NetworkHQ;
        if (hq == null) return;

        var targetList = Traverse.Create(__instance).Field("targetList").GetValue<List<Unit>>();
        if (targetList == null || targetList.Count == 0) return;

        var markerLookup = Traverse.Create(__instance).Field("markerLookup").GetValue<Dictionary<Unit, HUDUnitMarker>>();
        if (markerLookup == null || !markerLookup.TryGetValue(targetList[0], out var marker)) return;

        if (!marker.selected || marker.unit == null || marker.image == null || !marker.image.enabled) return;

        var unit = marker.unit;
        if (unit.HasRadarEmission() && unit.radar is Radar radar && radar.IsJammed()) return;

        if (hq.IsTargetLased(unit))
        {
            float pulse = (Mathf.Sin(Time.timeSinceLevelLoad * 16f) + 1f) * 0.5f;
            marker.image.color = Color.Lerp(Color.red, Color.green, pulse);
        }
    }
}
