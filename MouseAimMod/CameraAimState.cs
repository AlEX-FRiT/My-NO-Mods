using UnityEngine;

namespace MouseAimMod;

public static class CameraAimState
{
    public static Quaternion WorldRotation = Quaternion.identity;

    public static float SensitivityMultiplier = 120f;

    public static bool MouseAimEnabled = true;

    public static void Reset(Quaternion targetRotation)
    {
        WorldRotation = targetRotation;
    }

    public static void ResetToForward()
    {
        WorldRotation = Quaternion.identity;
    }
}