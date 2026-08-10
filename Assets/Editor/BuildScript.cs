using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

public class BuildScript
{
    /// <summary>
    /// Unity CLI から呼び出し可能な VIVE Focus Vision (Android) 向け APK 自動ビルドメソッド
    /// </summary>
    public static void BuildAndroidAPK()
    {
        Debug.Log("[BuildScript] Starting VIVE Focus Vision Android APK Build...");

        string scenePath = "Assets/Scenes/CoreSystemMain.unity";
        
        // メインシーンが存在しない場合は自動構築を実行
        if (!File.Exists(scenePath))
        {
            Debug.Log("[BuildScript] CoreSystemMain.unity not found. Generating scene...");
            SceneSetupScript.GenerateCoreSystemScene();
        }

        string[] scenes = { scenePath };
        string outputDirectory = "Builds";
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string outputPath = Path.Combine(outputDirectory, "VIVE_Focus_Vision_Core.apk");

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildScript] Build Succeeded! File Output: {outputPath} (Size: {summary.totalSize} bytes)");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[BuildScript] Build Failed with {summary.totalErrors} error(s). Result: {summary.result}");
            EditorApplication.Exit(1);
        }
    }
}
