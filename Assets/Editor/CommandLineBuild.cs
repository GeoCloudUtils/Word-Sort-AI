#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace WordSortPuzzle.Editor
{
    internal static class CommandLineBuild
    {
        public static void BuildWebGL()
        {
            string configuredOutput = Environment.GetEnvironmentVariable("WORD_SORT_WEBGL_OUTPUT");
            string outputPath = string.IsNullOrWhiteSpace(configuredOutput)
                ? Path.GetFullPath("Webgl Build")
                : Path.GetFullPath(configuredOutput);
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled scenes are configured for the build.");

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"WebGL build failed: {report.summary.result}");
        }
    }
}
#endif
