using HarmonyLib;
using UnityEngine;
using System;

namespace MouseAimMod;

[HarmonyPatch(typeof(CameraCockpitState))]
public static class CameraCockpitStatePatch
{
    [HarmonyPatch("UpdateState")]
    [HarmonyPrefix]
    public static bool UpdateStatePrefix(
        CameraCockpitState __instance,
        CameraStateManager cam,
        Aircraft ___aircraft,
        float ___minFOV,
        float ___maxFOV,
        float ___FOVAdjustment,
        Vector3 ___camRelativePos,
        Vector3 ___camRelativeVel)
    {
        if (!CameraAimState.MouseAimEnabled)
        {
            return true;
        }

        if (PlayerSettings.virtualJoystickEnabled)
        {
            return true;
        }

        if (!FlightHudStatePatch.CanvasEnabled)
        {
            return true;
        }

        var aircraft = ___aircraft;
        if (aircraft == null)
        {
            return true;
        }

        float deltaTime = Time.unscaledDeltaTime;

        Quaternion planeRotation = aircraft.transform.rotation;

        var trv = Traverse.Create(__instance);
        if (aircraft.cockpit != null)
            trv.Field("targetRB").SetValue(aircraft.cockpit.rb);

        if (!GameManager.flightControlsEnabled)
        {
            return false;
        }

        if (GameManager.playerInput.GetButtonTimedPressUp("Switch View", 0f, PlayerSettings.clickDelay))
        {
            aircraft.onShake -= trv.Method("CockpitCam_OnShake").GetValue<Action<Aircraft.OnShake>>();
            cam.SwitchState(cam.orbitState);
            return false;
        }

        float minFOV = ___minFOV;
        float maxFOV = ___maxFOV;
        float FOVAdjustment = ___FOVAdjustment;

        if (!DynamicMap.mapMaximized)
        {
            FOVAdjustment -= 5f * GameManager.playerInput.GetAxis("Zoom View");
        }
        FOVAdjustment = Mathf.Clamp(FOVAdjustment, minFOV - cam.desiredFOV, maxFOV - cam.desiredFOV);
        float targetFOV = Mathf.Clamp(cam.desiredFOV + FOVAdjustment, minFOV, maxFOV);
        cam.mainCamera.fieldOfView = Mathf.Lerp(cam.mainCamera.fieldOfView, targetFOV, 0.2f);
        cam.cockpitCamRender.fieldOfView = cam.mainCamera.fieldOfView;

        trv.Field("FOVAdjustment").SetValue(FOVAdjustment);

        float pitchInvert = (!PlayerSettings.viewInvertPitch) ? 1f : -1f;

        Vector3 euler;
        float panView, tiltView;

        if (!Cursor.visible && !RadialMenuMain.IsInUse())
        {
            float mouseX = GameManager.playerInput.GetAxis("Pan View")
                         * PlayerSettings.viewSensitivity
                         * CameraAimState.SensitivityMultiplier
                         * deltaTime;

            float mouseY = GameManager.playerInput.GetAxis("Tilt View")
                         * PlayerSettings.viewSensitivity
                         * CameraAimState.SensitivityMultiplier
                         * deltaTime
                         * pitchInvert;

            Quaternion localRotation = Quaternion.Inverse(planeRotation) * CameraAimState.WorldRotation;
            euler = localRotation.eulerAngles;
            panView = Mathf.DeltaAngle(0, euler.y);
            tiltView = Mathf.DeltaAngle(0, euler.x);

            panView += mouseX;
            tiltView += mouseY;

            panView = Mathf.Clamp(panView, -165f, 165f);
            tiltView = Mathf.Clamp(tiltView, -65f, 65f);

            localRotation = Quaternion.Euler(tiltView, panView, 0f);
            CameraAimState.WorldRotation = planeRotation * localRotation;
        }

        if (GameManager.playerInput.GetButtonDown("Center"))
        {
            CameraAimState.Reset(planeRotation);
        }

        Quaternion relativeRotation = Quaternion.Inverse(planeRotation) * CameraAimState.WorldRotation;

        euler = relativeRotation.eulerAngles;
        panView = Mathf.DeltaAngle(0, euler.y);
        tiltView = Mathf.DeltaAngle(0, euler.x);

        float originalPan = panView;
        float originalTilt = tiltView;

        panView = Mathf.Clamp(panView, -165f, 165f);
        tiltView = Mathf.Clamp(tiltView, -65f, 65f);

        if (panView != originalPan || tiltView != originalTilt)
        {
            Quaternion correctedRelative = Quaternion.Euler(tiltView, panView, 0f);
            CameraAimState.WorldRotation = planeRotation * correctedRelative;
            relativeRotation = correctedRelative;
        }
        else
        {
            relativeRotation = Quaternion.Euler(tiltView, panView, 0f);
        }

        trv.Field("panView").SetValue(panView);
        trv.Field("tiltView").SetValue(tiltView);

        Vector3 shakeOffset = trv.Method("CameraShake").GetValue<Vector3>();

        Vector3 camRelativePos = ___camRelativePos;
        Vector3 camRelativeVel = ___camRelativeVel;

        if (aircraft.CockpitRB() != null)
        {
            cam.cameraVelocity = aircraft.CockpitRB().velocity;
            camRelativePos += camRelativeVel * Mathf.Min(Time.deltaTime, 0.01666667f);
            if (camRelativePos.magnitude > 0.15f)
            {
                camRelativeVel = Vector3.zero;
                camRelativePos = Vector3.ClampMagnitude(camRelativePos, 0.15f);
            }
            trv.Field("camRelativePos").SetValue(camRelativePos);
            trv.Field("camRelativeVel").SetValue(camRelativeVel);
        }

        cam.cameraPivot.position = cam.followingUnit.cockpitViewPoint.position
                                 + camRelativePos * PlayerSettings.cockpitCamInertia
                                 + shakeOffset;

        float lateralOffset = Mathf.Lerp(0f, 0.2f, Mathf.Abs(panView * 0.015f) - 0.5f) * Mathf.Sign(panView);

        if (PlayerSettings.useTrackIR)
        {
            Tuple<Vector3, Quaternion> trackIROffset = TrackIRComponent.i.GetTrackIROffset(
                Vector3.right * lateralOffset,
                relativeRotation
            );
            cam.transform.localPosition = new Vector3(
                Mathf.Clamp(trackIROffset.Item1.x, -0.25f, 0.25f),
                Mathf.Clamp(trackIROffset.Item1.y, -0.15f, 0.15f),
                Mathf.Clamp(trackIROffset.Item1.z, -0.1f, 0.45f)
            );
            cam.transform.localRotation = trackIROffset.Item2;
        }
        else
        {
            cam.transform.localPosition = Vector3.right * lateralOffset;
            cam.transform.localRotation = relativeRotation;
        }

        return false;
    }

    [HarmonyPatch("EnterState")]
    [HarmonyPostfix]
    public static void EnterStatePostfix(Aircraft ___aircraft)
    {
        HoverThrottleController.ResetState();

        if (___aircraft != null)
        {
            CameraAimState.Reset(___aircraft.transform.rotation);
        }
        else
        {
            CameraAimState.ResetToForward();
        }
    }
}
