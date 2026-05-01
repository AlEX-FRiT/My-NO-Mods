using HarmonyLib;
using NuclearOption.Networking;

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
        if (hq != null && !hq.IsTargetLased(target))
        {
            Plugin.Logger.LogInfo(
                $"[SureFire] Blocked {owner.unitName} → {target.unitName}: target not lased");
            return false;
        }

        return true;
    }
}
