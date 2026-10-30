using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Builder.EditorTools
{
    /// <summary>Menu: Builder > Build Windows exe. Builds into Build/Windows at the repository root.</summary>
    public static class BuilderBuild
    {
        public const string ExeName = "AY3DBuilder.exe";

        /// <summary>&lt;repo&gt;/Build/Windows, found from the Unity project folder (&lt;repo&gt;/3Dmodeler).</summary>
        public static string WindowsOutputFolder =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Build", "Windows"));

        [MenuItem("Builder/Build Windows exe")]
        public static void BuildWindows()
        {
            string exePath = Path.Combine(WindowsOutputFolder, ExeName);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { BuilderSceneSetup.ScenePath },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Windows build failed: " + report.summary.result);

            // Burst writes debug files next to the build that are not needed to run it.
            foreach (var dir in Directory.GetDirectories(WindowsOutputFolder, "*_BurstDebugInformation_DoNotShip"))
                Directory.Delete(dir, true);

            // The helper for your Unity game (Mixamo animations on the mech) goes next to the exe too.
            string helper = Path.Combine(WindowsOutputFolder, "UnityHelper");
            Directory.CreateDirectory(helper);
            foreach (var file in new[] { "AYMechHumanoid.cs", "README.md" })
                File.Copy(Path.Combine(Application.dataPath, "UnityHelper", file), Path.Combine(helper, file), true);

            Debug.Log("[Builder] Windows build done: " + exePath);
        }

        public const string ApkName = "AY3DBuilder.apk";

        /// <summary>&lt;repo&gt;/Build/Android.</summary>
        public static string AndroidOutputFolder =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Build", "Android"));

        /// <summary>
        /// Menu: Builder > Build Android apk. Into Build/Android. Landscape, 64-bit ARM with IL2CPP (what Google Play
        /// needs), Android 7.0 or newer. Signed with Unity's debug key: fine to install by hand, not for the Play Store.
        /// </summary>
        [MenuItem("Builder/Build Android apk")]
        public static void BuildAndroid()
        {
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.ay.builder3d");
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            EditorUserBuildSettings.buildAppBundle = false;

            string apkPath = Path.Combine(AndroidOutputFolder, ApkName);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { BuilderSceneSetup.ScenePath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Android build failed: " + report.summary.result);
            Debug.Log($"[Builder] Android build done: {apkPath} ({new FileInfo(apkPath).Length / (1024 * 1024)} MB)");
        }
    }
}
