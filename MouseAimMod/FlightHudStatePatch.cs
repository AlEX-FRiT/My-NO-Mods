using HarmonyLib;

namespace MouseAimMod;

[HarmonyPatch(typeof(FlightHud), "EnableCanvas")]
internal class FlightHudStatePatch
{
    internal static bool CanvasEnabled { get; private set; }

    [HarmonyPostfix]
    private static void Postfix(bool enable)
    {
        CanvasEnabled = enable;
    }
}
