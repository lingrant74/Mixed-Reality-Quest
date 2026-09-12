using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class UrpBootstrap
{
    const string SettingsFolder = "Assets/Settings";
    const string RendererPath = SettingsFolder + "/URP_Renderer.asset";
    const string PipelinePath = SettingsFolder + "/URP.asset";

    [MenuItem("Tools/Bootstrap URP")]
    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder(SettingsFolder))
            AssetDatabase.CreateFolder("Assets", "Settings");

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, RendererPath);
        }

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
        }

        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        for (var i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
            QualitySettings.renderPipeline = pipeline;
        }

        PlayerSettings.colorSpace = ColorSpace.Linear;
        GraphicsSettings.lightsUseLinearIntensity = true;
        GraphicsSettings.lightsUseColorTemperature = true;

        EditorUtility.SetDirty(renderer);
        EditorUtility.SetDirty(pipeline);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"URP bootstrap complete. Pipeline={pipeline.name}, ColorSpace=Linear");
    }
}
