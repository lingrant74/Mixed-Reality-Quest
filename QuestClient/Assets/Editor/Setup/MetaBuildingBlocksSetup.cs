using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the mixed reality scene out of Meta's Building Blocks instead of hand-rolled rig
/// and interaction code. The Building Blocks installer API is internal to
/// Meta.XR.BuildingBlocks.Editor, so it is driven here through reflection.
/// </summary>
public static class MetaBuildingBlocksSetup
{
    const string ScenePath = "Assets/Scenes/MrHologram.unity";
    const string MaterialPath = "Assets/Materials/Hologram.mat";

    // Meta XR Core SDK block ids.
    const string PassthroughBlock = "f0540b20-dfd6-420e-b20d-c270f88dc77e";
    const string HandTrackingBlock = "8b26b298-7bf4-490e-b245-a039c0184303";
    const string ControllerTrackingBlock = "5817f7c0-f2a5-45f9-a5ca-64264e0166e8";
    const string SpatialAnchorCoreBlock = "a383f5ea-3856-4c23-a11c-7fdbb9408035";

    // Meta XR Interaction SDK block ids.
    const string HandInteractionsBlock = "0393ca30-f2a9-4865-a40f-f9a68d01c3a9";
    const string ControllerInteractionsBlock = "f10154e0-16b2-492f-97d0-6639f69e7df6";
    const string GrabInteractionBlock = "f8766c17-aaf8-4b55-9431-de8deea83842";
    const string RealHandsBlock = "f547fe18-d477-46ec-bdf5-7208df19cb98";

    [MenuItem("Tools/Quest Client/Build MR Scene (Building Blocks)")]
    public static async void BuildScene()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Passthrough pulls in the Camera Rig block as a dependency.
            await Install(PassthroughBlock);
            await Install(HandTrackingBlock);
            await Install(ControllerTrackingBlock);

            // Hand/controller interactions pull in the Interactions Rig, which supplies the
            // grab interactors that make a Grabbable respond to a pinch.
            await Install(HandInteractionsBlock);
            await Install(ControllerInteractionsBlock);
            await Install(RealHandsBlock);
            await Install(SpatialAnchorCoreBlock);

            var hologram = CreateHologram();

            // Grab Interaction is applied to a selected object rather than instantiated.
            await Install(GrabInteractionBlock, hologram);

            hologram.AddComponent<AnchorOnRelease>();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[BuildingBlocks] Built {ScenePath}. Open it and press Play.");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[BuildingBlocks] Scene build failed: {exception}");
        }
    }

    // --- block installation ---------------------------------------------

    static async Task Install(string blockId, GameObject target = null)
    {
        var blockData = GetBlockData(blockId);
        if (blockData == null)
        {
            Debug.LogWarning($"[BuildingBlocks] Block {blockId} not found in the registry; skipping.");
            return;
        }

        var addToProject = blockData.GetType().GetMethod(
            "AddToProject",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (addToProject == null)
        {
            Debug.LogWarning($"[BuildingBlocks] No AddToProject on {blockData.GetType().Name}; skipping {blockId}.");
            return;
        }

        var name = BlockName(blockData) ?? blockId;
        if (target != null)
            Selection.activeGameObject = target;

        var task = addToProject.Invoke(blockData, new object[] { target, null }) as Task;
        if (task != null)
            await task;

        Debug.Log($"[BuildingBlocks] Installed '{name}'.");
    }

    static object GetBlockData(string blockId)
    {
        var utilsType = FindEditorType("Meta.XR.BuildingBlocks.Editor.Utils");
        var method = utilsType?.GetMethod(
            "GetBlockData",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(string) },
            null);
        if (method != null)
            return method.Invoke(null, new object[] { blockId });

        // Fall back to scanning the loaded block assets directly.
        var baseType = FindEditorType("Meta.XR.BuildingBlocks.Editor.BlockBaseData");
        if (baseType == null)
            return null;

        foreach (var asset in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Packages" })
                     .Select(AssetDatabase.GUIDToAssetPath)
                     .Select(AssetDatabase.LoadMainAssetAtPath))
        {
            if (asset == null || !baseType.IsInstanceOfType(asset))
                continue;
            var idProperty = asset.GetType().GetProperty("Id");
            if (idProperty != null && (string)idProperty.GetValue(asset) == blockId)
                return asset;
        }

        return null;
    }

    static string BlockName(object blockData)
    {
        var property = blockData.GetType().GetProperty("BlockName");
        var overridable = property?.GetValue(blockData);
        var value = overridable?.GetType().GetProperty("Value")?.GetValue(overridable);
        return value as string;
    }

    static Type FindEditorType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullName, false);
            if (type != null)
                return type;
        }
        return null;
    }

    // --- scene content ---------------------------------------------------

    static GameObject CreateHologram()
    {
        var hologram = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hologram.name = "Hologram";
        hologram.transform.localScale = Vector3.one * 0.2f;
        hologram.transform.position = new Vector3(0f, 1.2f, 0.5f);

        var shader = Shader.Find("AssemblyGuide/PassthroughUnlit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            var color = new Color(0.22f, 0.55f, 1f, 1f);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            hologram.GetComponent<Renderer>().sharedMaterial = material;
        }

        return hologram;
    }

    static void AddSceneToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Any(s => s.path == path))
            return;
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
