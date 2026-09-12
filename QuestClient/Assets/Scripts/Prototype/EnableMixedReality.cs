using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

/// <summary>
/// Sets up Quest passthrough through Unity OpenXR Meta: a transparent XR camera plus an
/// active AR camera manager. The runtime composites the room beneath the rendered frame,
/// so no passthrough layer component or background blit is used here.
/// </summary>
public class EnableMixedReality : MonoBehaviour
{
    [SerializeField] bool disableSimulatorWithHeadset = true;

    void Awake()
    {
        ConfigureCamera();
        HideControllerVisuals();
        if (disableSimulatorWithHeadset)
            DisableSimulatorIfHeadsetPresent();
    }

    void Start()
    {
        HideControllerVisuals();
        EnsureHandInput();
        EnsureHologram();
    }

    static void ConfigureCamera()
    {
        var camera = XrSpace.Camera;
        if (camera == null)
            return;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.nearClipPlane = 0.05f;

        // Meta composites passthrough itself; a background blit would cover it.
        var background = camera.GetComponent<ARCameraBackground>();
        if (background != null)
            background.enabled = false;

        if (camera.GetComponent<ARCameraManager>() == null)
            camera.gameObject.AddComponent<ARCameraManager>();

        var extra = camera.GetUniversalAdditionalCameraData();
        if (extra != null)
        {
            extra.renderType = CameraRenderType.Base;
            extra.allowHDROutput = false;
            extra.renderPostProcessing = false;
        }
    }

    static void HideControllerVisuals()
    {
        var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (var i = 0; i < transforms.Length; i++)
        {
            var name = transforms[i].name;
            if (name.Contains("Controller Visual") || name.Contains("Controller Model"))
                transforms[i].gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Hand input must exist before the hologram reads it, so it is created here in Start.
    /// </summary>
    static void EnsureHandInput()
    {
        if (HandInput.Instance != null)
            return;

        var host = GameObject.Find("Hand Tracking");
        if (host == null)
            host = new GameObject("Hand Tracking");
        host.AddComponent<HandInput>();
    }

    static void EnsureHologram()
    {
        var cube = GameObject.Find("DemoCube");
        if (cube == null)
            return;

        PassthroughAppearance.ApplyTo(cube.GetComponent<Renderer>(), new Color(0.22f, 0.55f, 1f, 1f));
        if (cube.GetComponent<HologramController>() == null)
            cube.AddComponent<HologramController>();
    }

    static void DisableSimulatorIfHeadsetPresent()
    {
        if (!IsHeadsetPresent())
            return;

        var simulator = GameObject.Find("XR Interaction Simulator");
        if (simulator != null)
            simulator.SetActive(false);
    }

    public static Camera FindXrCamera() => XrSpace.Camera;

    static bool IsHeadsetPresent()
    {
        if (XRSettings.isDeviceActive && XRSettings.loadedDeviceName != "None")
            return true;

        var loader = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager.activeLoader : null;
        return loader != null;
    }
}
