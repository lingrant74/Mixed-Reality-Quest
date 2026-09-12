using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Meta XR Core 205.0.0 CaptureTool fails to compile on Editors without Android Build Support
/// because AndroidExternalToolsSettings is missing. Re-apply a reflection-based lookup if
/// Package Manager restores the original file.
/// </summary>
[InitializeOnLoad]
static class PatchMetaXrCaptureTool
{
    const string Marker = "static string GetAndroidSdkRootPath()";

    static PatchMetaXrCaptureTool()
    {
        EditorApplication.delayCall += TryPatch;
    }

    [MenuItem("Tools/Quest Client/Patch Meta XR CaptureTool")]
    static void TryPatch()
    {
        var packagesRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "PackageCache"));
        if (!Directory.Exists(packagesRoot))
            return;

        foreach (var dir in Directory.GetDirectories(packagesRoot, "com.meta.xr.sdk.core@*"))
        {
            var path = Path.Combine(dir, "Editor", "RuntimeOptimizer", "PerformanceInsight", "CaptureTool.cs");
            if (!File.Exists(path))
                continue;

            var text = File.ReadAllText(path);
            if (text.Contains(Marker) || !text.Contains("AndroidExternalToolsSettings"))
                continue;

            text = text.Replace(
                "using Unity.Profiling;\nusing UnityEngine;\n#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX\nusing UnityEditor.Android;\n#endif\nusing System.Collections;",
                "using System.Reflection;\nusing Unity.Profiling;\nusing UnityEngine;\nusing System.Collections;");

            text = text.Replace(
                "static private OVRADBTool adbTool =\n#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX\n            new OVRADBTool(AndroidExternalToolsSettings.sdkRootPath);\n#else\n            new OVRADBTool(\"\");\n#endif",
                "static private OVRADBTool adbTool = new OVRADBTool(GetAndroidSdkRootPath());\n\n        static string GetAndroidSdkRootPath()\n        {\n            var settingsType = System.Type.GetType(\"UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions\");\n            var property = settingsType?.GetProperty(\"sdkRootPath\", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);\n            return property?.GetValue(null) as string ?? string.Empty;\n        }");

            text = text.Replace(
                "#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX\n                adbTool = new OVRADBTool(AndroidExternalToolsSettings.sdkRootPath);\n#else\n                adbTool = new OVRADBTool(\"\");\n#endif",
                "adbTool = new OVRADBTool(GetAndroidSdkRootPath());");

            if (!text.Contains("AndroidExternalToolsSettings"))
            {
                File.WriteAllText(path, text);
                AssetDatabase.Refresh();
                Debug.Log("Patched Meta XR CaptureTool so it compiles without Android Build Support.");
            }
        }
    }
}
