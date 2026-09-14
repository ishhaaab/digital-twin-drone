using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

/// <summary>
/// Headless build entry point for local validation or licensed CI runners:
///   Unity -batchmode -nographics -projectPath <proj> -executeMethod BuildScript.Build -quit
/// Compiles the project as a standalone Windows build and reports success/errors.
/// </summary>
public static class BuildScript
{
    public static void Build()
    {
        // A per-process path prevents concurrent editor jobs from deleting or
        // overwriting each other's build output.
        string buildDir = Path.Combine(
            Path.GetTempPath(),
            $"digital-twin-drone-build-{System.Diagnostics.Process.GetCurrentProcess().Id}");
        Directory.CreateDirectory(buildDir);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = Path.Combine(buildDir, "DigitalTwinDrone.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        Debug.Log($"[BuildScript] Output: {options.locationPathName}");
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildScript] BUILD OK  size={summary.totalSize}  errors={summary.totalErrors}");
        }
        else
        {
            Debug.LogError($"[BuildScript] BUILD FAILED result={summary.result} errors={summary.totalErrors}");
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError($"[BuildScript] {step.name}: {msg.content}");
            }
            EditorApplication.Exit(1);
        }
    }
}
