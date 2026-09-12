using System.IO;
using System.Linq;
using System.Reflection;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.CompositionLayers;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using UnityEngine.XR.OpenXR.Features.OculusQuestSupport;

public static class QuestXrBootstrap
{
    const string ScenePath = "Assets/Scenes/PassthroughCube.unity";
    const string MaterialPath = "Assets/Materials/DemoCube.mat";
    const string MetaFeatureSetId = "com.unity.openxr.featureset.meta";
    const string OpenXrLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

    [InitializeOnLoadMethod]
    static void AutoContinue()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (File.Exists(ScenePath))
                return;
            if (!PackagesReady())
                return;

            Debug.Log("Quest XR bootstrap: creating first passthrough scene.");
            Run();
        };
    }

    [MenuItem("Tools/Quest Client/Setup Passthrough Prototype")]
    public static void Run()
    {
        ConfigureProject();
        if (!ImportRequiredSamples())
            return;

        CreatePassthroughScene();
        ApplyToOpenScene();
        if (File.Exists(ScenePath))
            Debug.Log("Quest XR bootstrap complete. Open Assets/Scenes/PassthroughCube.unity and Build And Run to Quest 3S.");
    }

    [MenuItem("Tools/Quest Client/Apply Passthrough And Hands")]
    public static void ApplyToOpenScene()
    {
        ConfigureProject();

        var origin = GameObject.Find("XR Origin (XR Rig)");
        if (origin != null)
            ConfigurePassthroughCamera(origin);

        EnsureComponent<EnableMixedReality>("Mixed Reality");
        EnsureComponent<HandPinchVisuals>("Hand Tracking");
        EnsureComponent<HandInput>("Hand Tracking");

        var cube = GameObject.Find("DemoCube");
        if (cube != null)
        {
            if (cube.GetComponent<HologramController>() == null)
                cube.AddComponent<HologramController>();
            MakeCubeUnlit(cube);
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Applied passthrough camera, Meta passthrough, and hand tracking to the open scene.");
    }

    static bool PackagesReady()
    {
        return AssetDatabase.FindAssets("t:MonoScript XRGrabInteractable").Length > 0
               && AssetDatabase.FindAssets("t:MonoScript ARCameraManager").Length > 0;
    }

    static void ConfigureProject()
    {
        UseInputSystem();
        OptimizeUrp();
        ConfigureAndroidPlayer();
        ConfigureMetaProject();
        EnableOpenXr(BuildTargetGroup.Standalone);
        EnableOpenXr(BuildTargetGroup.Android);
        ConfigureOpenXrFeatures(BuildTargetGroup.Standalone);
        ConfigureOpenXrFeatures(BuildTargetGroup.Android);
        AssetDatabase.SaveAssets();
    }

    static void ConfigureMetaProject()
    {
        var config = AssetDatabase.LoadAssetAtPath<OVRProjectConfig>("Assets/Oculus/OculusProjectConfig.asset");
        if (config == null)
            return;

        var so = new SerializedObject(config);
        SetEnum(so, "handTrackingSupport", (int)OVRProjectConfig.HandTrackingSupport.ControllersAndHands);
        SetEnum(so, "_insightPassthroughSupport", (int)OVRProjectConfig.FeatureSupport.Required);
        SetEnum(so, "anchorSupport", (int)OVRProjectConfig.AnchorSupport.Enabled);
        var insightEnabled = so.FindProperty("insightPassthroughEnabled");
        if (insightEnabled != null)
            insightEnabled.boolValue = true;
        var cameraAccess = so.FindProperty("isPassthroughCameraAccessEnabled");
        if (cameraAccess != null)
            cameraAccess.boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(config);
    }

    static void SetEnum(SerializedObject so, string propertyName, int value)
    {
        var property = so.FindProperty(propertyName);
        if (property != null)
            property.intValue = value;
    }

    static void UseInputSystem()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (assets == null || assets.Length == 0)
            return;

        var so = new SerializedObject(assets[0]);
        var handler = so.FindProperty("activeInputHandler");
        if (handler != null && handler.intValue != 1 && handler.intValue != 2)
        {
            handler.intValue = 1;
            so.ApplyModifiedProperties();
        }
    }

    static void OptimizeUrp()
    {
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP.asset");
        if (pipeline != null)
        {
            pipeline.supportsHDR = false;
            var so = new SerializedObject(pipeline);
            var terrainHoles = so.FindProperty("m_SupportsTerrainHoles");
            if (terrainHoles != null)
                terrainHoles.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
        }

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP_Renderer.asset");
        if (renderer != null)
        {
            var so = new SerializedObject(renderer);
            var postProcess = so.FindProperty("m_PostProcessData");
            if (postProcess != null)
                postProcess.objectReferenceValue = null;
            var intermediate = so.FindProperty("m_IntermediateTextureMode");
            if (intermediate != null)
                intermediate.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
        }
    }

    static void ConfigureAndroidPlayer()
    {
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        if (string.IsNullOrEmpty(PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android)))
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.assemblyguide.questclient");
    }

    static void EnableOpenXr(BuildTargetGroup group)
    {
        var perTarget = GetOrCreateXrSettings();
        if (perTarget == null)
            return;
        if (!perTarget.HasSettingsForBuildTarget(group))
            perTarget.CreateDefaultSettingsForBuildTarget(group);
        if (!perTarget.HasManagerSettingsForBuildTarget(group))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

        var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
        if (settings == null || settings.Manager == null)
            return;

        if (!XRPackageMetadataStore.IsLoaderAssigned(OpenXrLoaderType, group))
            XRPackageMetadataStore.AssignLoader(settings.Manager, OpenXrLoaderType, group);

        settings.InitManagerOnStart = true;
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(settings.Manager);
        EditorUtility.SetDirty(perTarget);
    }

    static void ConfigureOpenXrFeatures(BuildTargetGroup group)
    {
        FeatureHelpers.RefreshFeatures(group);
        OpenXRFeatureSetManager.InitializeFeatureSets();

        var featureSet = OpenXRFeatureSetManager.GetFeatureSetWithId(group, MetaFeatureSetId);
        if (featureSet != null)
            featureSet.isEnabled = true;

        EnableFeature(group, OpenXRCompositionLayersFeature.FeatureId, true);
        EnableFeature(group, ARSessionFeature.featureId, true);
        EnableFeature(group, ARCameraFeature.featureId, true);
        EnableFeature(group, MetaQuestFeature.featureId, true);
#pragma warning disable CS0618
        EnableFeature(group, OculusQuestFeature.featureId, false);
#pragma warning restore CS0618
        EnableFeature(group, OculusTouchControllerProfile.featureId, true);
        EnableFeature(group, MetaQuestTouchPlusControllerProfile.featureId, true);
        EnableFeature(group, MetaQuestTouchProControllerProfile.featureId, true);
        EnableFeature(group, HandTracking.featureId2, true);
        EnableFeature(group, MetaHandTrackingAim.featureId, true);
        EnableFeature(group, "com.unity.openxr.feature.input.handtrackingdatasource", true);
        EnableFeature(group, HandCommonPosesInteraction.featureId, true);
        EnableFeature(group, HandInteractionProfile.featureId, true);

        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
        if (settings != null)
            EditorUtility.SetDirty(settings);
    }

    static void EnableFeature(BuildTargetGroup group, string featureId, bool enabled)
    {
        var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, featureId);
        if (feature == null)
            return;
        feature.enabled = enabled;
        EditorUtility.SetDirty(feature);
    }

    static bool ImportRequiredSamples()
    {
        var importedAny = false;
        importedAny |= ImportSample("com.unity.xr.interaction.toolkit", "Starter Assets");
        importedAny |= ImportSample("com.unity.xr.interaction.toolkit", "XR Interaction Simulator");

        if (importedAny)
        {
            AssetDatabase.Refresh();
            EditorApplication.delayCall += CreatePassthroughScene;
            Debug.Log("Quest XR bootstrap: imported XRI samples. Scene creation will continue after refresh.");
            return false;
        }

        return true;
    }

    static bool ImportSample(string packageId, string displayName)
    {
        var existingFolder = Path.Combine("Assets/Samples/XR Interaction Toolkit/3.6.0", displayName);
        if (Directory.Exists(existingFolder))
            return false;

        var version = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
            .FirstOrDefault(package => package.name == packageId)?.version;
        foreach (var sample in Sample.FindByPackage(packageId, version))
        {
            if (sample.displayName != displayName || sample.isImported)
                continue;
            sample.Import();
            return true;
        }

        return false;
    }

    static XRGeneralSettingsPerBuildTarget GetOrCreateXrSettings()
    {
        if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget existing)
            && existing != null)
            return existing;

        var method = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
            "GetOrCreate",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return method?.Invoke(null, null) as XRGeneralSettingsPerBuildTarget;
    }

    static void CreatePassthroughScene()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += CreatePassthroughScene;
            return;
        }

        if (File.Exists(ScenePath))
            return;

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        var originPrefab = FindAsset<GameObject>("XR Origin (XR Rig)", "Starter Assets");
        if (originPrefab == null)
        {
            Debug.LogWarning("Quest XR bootstrap: XR Origin prefab not imported yet. Will retry.");
            EditorApplication.delayCall += CreatePassthroughScene;
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "PassthroughCube";

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.75f, 0.75f, 0.75f);

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var arSession = new GameObject("AR Session");
        arSession.AddComponent<ARSession>();

        var origin = (GameObject)PrefabUtility.InstantiatePrefab(originPrefab);
        origin.name = "XR Origin (XR Rig)";
        ConfigurePassthroughCamera(origin);

        var simulatorPrefab = FindAsset<GameObject>("XR Interaction Simulator", "XR Interaction Simulator");
        if (simulatorPrefab != null)
        {
            var simulator = (GameObject)PrefabUtility.InstantiatePrefab(simulatorPrefab);
            simulator.name = "XR Interaction Simulator";
        }

        EnsureComponent<EnableMixedReality>("Mixed Reality");
        EnsureComponent<HandPinchVisuals>("Hand Tracking");
        EnsureComponent<HandInput>("Hand Tracking");
        CreateDemoCube();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Created " + ScenePath);
    }

    static void ConfigurePassthroughCamera(GameObject origin)
    {
        var camera = origin.GetComponentInChildren<Camera>(true);
        if (camera == null)
            return;

        if (camera.GetComponent<ARCameraManager>() == null)
            camera.gameObject.AddComponent<ARCameraManager>();
        // Meta composites passthrough in the runtime, so no ARCameraBackground blit here.
        var background = camera.GetComponent<ARCameraBackground>();
        if (background != null)
            Object.DestroyImmediate(background);

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);

        var extra = camera.GetUniversalAdditionalCameraData();
        if (extra != null)
        {
            extra.renderType = CameraRenderType.Base;
            extra.allowHDROutput = false;
            extra.renderPostProcessing = false;
        }

        var originComponent = origin.GetComponent<XROrigin>();
        if (originComponent != null && originComponent.Camera == null)
            originComponent.Camera = camera;
    }

    static void CreateDemoCube()
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "DemoCube";
        cube.transform.localScale = Vector3.one * 0.2f;
        cube.transform.position = new Vector3(0f, 1.2f, 1f);

        // HologramController is the only mover, so no Rigidbody or XRGrabInteractable here.
        cube.AddComponent<HologramController>();

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { color = new Color(0.22f, 0.55f, 1f, 1f) };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            cube.GetComponent<Renderer>().sharedMaterial = material;
            MakeCubeUnlit(cube);
        }
    }

    static void MakeCubeUnlit(GameObject cube)
    {
        var renderer = cube.GetComponent<Renderer>();
        if (renderer == null)
            return;

        var shader = Shader.Find("AssemblyGuide/PassthroughUnlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            return;

        var material = renderer.sharedMaterial;
        if (material == null || material.shader != shader)
        {
            material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
                material = new Material(shader);
            else
                material.shader = shader;
            material.color = new Color(0.22f, 0.55f, 1f, 1f);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", new Color(0.22f, 0.55f, 1f, 1f));
            if (!AssetDatabase.LoadAssetAtPath<Material>(MaterialPath))
                AssetDatabase.CreateAsset(material, MaterialPath);
            else
                EditorUtility.SetDirty(material);
            renderer.sharedMaterial = material;
        }
    }

    static void EnsureComponent<T>(string objectName) where T : Component
    {
        var existing = Object.FindFirstObjectByType<T>();
        if (existing != null)
            return;

        var go = GameObject.Find(objectName);
        if (go == null)
            go = new GameObject(objectName);
        go.AddComponent<T>();
    }

    static T FindAsset<T>(string filterName, string pathContains) where T : Object
    {
        var guids = AssetDatabase.FindAssets($"{filterName} t:{typeof(T).Name}");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(pathContains) || path.Contains(pathContains))
                return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        return guids.Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<T>)
            .FirstOrDefault();
    }

    static void AddSceneToBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == scenePath))
            return;

        scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}