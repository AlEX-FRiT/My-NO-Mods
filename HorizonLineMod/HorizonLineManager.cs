using UnityEngine;
using HarmonyLib;

namespace HorizonLineMod;

[HarmonyPatch(typeof(CombatHUD), "SetAircraft")]
public static class CombatHUDPatch
{
    [HarmonyPostfix]
    public static void SetAircraftPostfix(CombatHUD __instance, Aircraft aircraft)
    {
        HorizonLineManager.SetAircraft(aircraft);
    }
}

public static class HorizonLineManager
{
    private static HorizonLine currentLine;
    private static Aircraft currentAircraft;

    public static void SetAircraft(Aircraft aircraft)
    {
        currentAircraft = aircraft;

        if (!Plugin.Enabled.Value)
        {
            DestroyCurrentLine();
            return;
        }

        if (currentLine != null)
        {
            Object.Destroy(currentLine.gameObject);
            currentLine = null;
        }

        if (aircraft != null)
        {
            CreateLine(aircraft);
        }
    }

    public static void RecreateIfEnabled()
    {
        if (Plugin.Enabled.Value && currentAircraft != null && currentLine == null)
        {
            CreateLine(currentAircraft);
        }
    }

    public static void DestroyCurrentLine()
    {
        if (currentLine != null)
        {
            Object.Destroy(currentLine.gameObject);
            currentLine = null;
        }
    }

    private static void CreateLine(Aircraft aircraft)
    {
        if (FlightHud.i == null)
        {
            Debug.LogWarning("[HorizonLineMod] FlightHud.i is null, cannot create line");
            return;
        }

        Transform flightHudTransform = FlightHud.i.transform;
        if (flightHudTransform == null)
        {
            Debug.LogWarning("[HorizonLineMod] FlightHud.transform is null");
            return;
        }

        GameObject lineObj = new GameObject("HorizonLine");
        lineObj.transform.SetParent(flightHudTransform, false);
        lineObj.transform.localPosition = Vector3.zero;

        currentLine = lineObj.AddComponent<HorizonLine>();
        currentLine.Initialize(aircraft);
    }
}