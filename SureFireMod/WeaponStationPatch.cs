using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace SureFireMod;

[HarmonyPatch(typeof(WeaponStation), "LaunchMount")]
public static class WeaponStation_LaunchMount_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(
        WeaponStation __instance,
        Unit owner,
        Unit target,
        GlobalPosition aimpoint)
    {
        if (!Plugin.Enabled.Value) return true;
        if (target == null)
            return true;

        if (!__instance.WeaponInfo.laserGuided)
            return true;

        FactionHQ hq = owner.NetworkHQ;
        if (Plugin.RequireLased.Value && hq != null && !hq.IsTargetLased(target))
        {
            Plugin.Logger.LogInfo(
                $"[SureFire] Blocked {owner.unitName} → {target.unitName}: target not lased");
            return false;
        }

        if (Plugin.RequireArc.Value)
        {
            var req = __instance.WeaponInfo.targetRequirements;
            var ownerTr = ((Component)owner).transform;
            var targetTr = ((Component)target).transform;
            float dist = Vector3.Distance(ownerTr.position, targetTr.position);
            float arc = Mathf.Min(req.minAlignment, Mathf.Max(dist, req.minRange) * 0.002f);
            float angle = Vector3.Angle(ownerTr.forward, targetTr.position - ownerTr.position);
            if (angle > arc)
            {
                Plugin.Logger.LogInfo(
                    $"[SureFire] Blocked {owner.unitName} → {target.unitName}: out of arc ({angle:F1}° > {arc:F1}°)");
                return false;
            }
        }

        if (Plugin.SortByAngle.Value && hq != null)
        {
            var aircraft = owner as Aircraft;
            var laser = aircraft?.GetLaserDesignator();
            if (laser != null)
            {
                var trv = Traverse.Create(laser);
                var targetList = trv.Field("targetList").GetValue<List<Unit>>();
                var lasedTargets = trv.Field("lasedTargets").GetValue<List<Unit>>();
                int maxTargets = trv.Field("maxTargets").GetValue<int>();
                float range = trv.Field("range").GetValue<float>();
                var xform = trv.Field("xform").GetValue<Transform>();

                foreach (var u in lasedTargets)
                    hq.UpdateLasedState(u, false);
                lasedTargets.Clear();

                var viable = new List<Unit>();
                foreach (var u in targetList)
                {
                    if (u == null || !hq.IsTargetPositionAccurate(u, 100f)
                        || !FastMath.InRange(u.GlobalPosition(), xform.GlobalPosition(), range)
                        || !u.LineOfSight(xform.position, 1000f))
                        continue;
                    viable.Add(u);
                }

                var nose = ((Component)owner).transform.forward;
                var xformPos = xform.position;
                viable.Sort((a, b) =>
                    Vector3.Angle(nose, a.transform.position - xformPos)
                   .CompareTo(Vector3.Angle(nose, b.transform.position - xformPos)));

                for (int i = 0; i < maxTargets && i < viable.Count; i++)
                {
                    hq.UpdateLasedState(viable[i], true);
                    lasedTargets.Add(viable[i]);
                }
            }
        }

        return true;
    }
}
