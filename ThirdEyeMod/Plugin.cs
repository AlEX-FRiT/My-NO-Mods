using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ThirdEyeMod;

[BepInPlugin("nuclearoption.thirdeyemod", "Third Eye Mod", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("Third Eye Mod is loaded!");

        _harmony = new Harmony("nuclearoption.thirdeyemod");
        _harmony.PatchAll();
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
