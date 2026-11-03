using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ShiftRegister.Editor
{
    /// <summary>One-click builds into Builds/ (git-ignored), using the scenes enabled in Build Settings.</summary>
    public static class BuildTools
    {
        public const string WebPath = "Builds/Web";
        public const string WindowsPath = "Builds/Windows/ShiftRegister.exe";
        public const string LinuxPath = "Builds/Linux/ShiftRegister.x86_64";
        public const string AndroidPath = "Builds/Android/ShiftRegister.apk";

        [MenuItem("Shift Register/Build/Web")]
        public static void BuildWeb() => Build(BuildTarget.WebGL, WebPath);

        [MenuItem("Shift Register/Build/Windows")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, WindowsPath);

        [MenuItem("Shift Register/Build/Linux")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, LinuxPath);

        /// <summary>An installable APK for testing on a phone (signed with the debug keystore).</summary>
        [MenuItem("Shift Register/Build/Android APK")]
        public static void BuildAndroid() => Build(BuildTarget.Android, AndroidPath);

        public static BuildReport Build(BuildTarget target, string path)
        {
            // Smallest download for the web build. This setting lives in Library/, not in the
            // project settings, so set it here for every machine.
            if (target == BuildTarget.WebGL)
                UnityEditor.WebGL.UserBuildSettings.codeOptimization = UnityEditor.WebGL.WasmCodeOptimization.DiskSizeLTO;
            // An APK installs directly; an app bundle (.aab) only goes through the Play Store.
            if (target == BuildTarget.Android)
                EditorUserBuildSettings.buildAppBundle = false;

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("No scenes enabled in Build Settings.");
                return null;
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None
            });

            var s = report.summary;
            string where = Path.GetFullPath(target == BuildTarget.WebGL ? path : Path.GetDirectoryName(path));
            string line = $"{target} build {s.result}: {s.totalSize / (1024f * 1024f):F1} MB in {s.totalTime.TotalSeconds:F0} s, " +
                          $"{s.totalErrors} errors, {s.totalWarnings} warnings → {where}";
            if (s.result == BuildResult.Succeeded) Debug.Log(line);
            else Debug.LogError(line);
            return report;
        }
    }
}
