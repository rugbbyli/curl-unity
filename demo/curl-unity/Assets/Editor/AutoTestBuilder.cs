#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Unity CLI build script for device testing.
///
/// Usage:
///   Unity -batchmode -quit -nographics \
///     -projectPath demo/curl-unity \
///     -executeMethod AutoTestBuilder.BuildMacOS \
///     -logFile build.log
///
/// Environment variables:
///   UNITY_IOS_TEAM_ID  — Apple Developer Team ID (required for iOS)
/// </summary>
public static class AutoTestBuilder
{
    const string TestDefine = "CURL_UNITY_AUTOTEST";
    const string BuildDir = "Build";
    const string BundleId = "com.basecity.curlunitytest";

    // ================================================================
    // Public entry points (called via -executeMethod)
    // ================================================================

    public static void BuildMacOS()
    {
        RunWithRestoredSettings(BuildTargetGroup.Standalone, () =>
        {
#pragma warning disable CS0618 // SetApplicationIdentifier(BuildTargetGroup) is obsolete in 2023+
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, BundleId);
#pragma warning restore CS0618

            var options = new BuildPlayerOptions
            {
                scenes = GetScenes(),
                locationPathName = $"{BuildDir}/macOS/curl-unity-test.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };

            RunBuild(options, "macOS");
        });
    }

    public static void BuildWindows()
    {
        RunWithRestoredSettings(BuildTargetGroup.Standalone, () =>
        {
#pragma warning disable CS0618
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, BundleId);
#pragma warning restore CS0618

            // Use Mono backend for cross-compilation from macOS
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

            var options = new BuildPlayerOptions
            {
                scenes = GetScenes(),
                locationPathName = $"{BuildDir}/Windows/curl-unity-test.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            RunBuild(options, "Windows");
        });
    }

    public static void BuildAndroid()
    {
        RunWithRestoredSettings(BuildTargetGroup.Android, () =>
        {
#pragma warning disable CS0618
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleId);
#pragma warning restore CS0618

            // Android-specific
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;

            var options = new BuildPlayerOptions
            {
                scenes = GetScenes(),
                locationPathName = $"{BuildDir}/Android/curl-unity-test.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            RunBuild(options, "Android");
        });
    }

    public static void BuildiOS()
    {
        RunWithRestoredSettings(BuildTargetGroup.iOS, () =>
        {
#pragma warning disable CS0618
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BundleId);
#pragma warning restore CS0618

            // iOS signing — Team ID must be provided explicitly, no fallback default,
            // to avoid baking any individual's team id into the committed project.
            var teamId = Environment.GetEnvironmentVariable("UNITY_IOS_TEAM_ID");
            if (string.IsNullOrEmpty(teamId))
            {
                Debug.LogError(
                    "[AutoTestBuilder] UNITY_IOS_TEAM_ID is not set. " +
                    "Export your Apple Developer Team ID before running BuildiOS.");
                EditorApplication.Exit(2);
                return;
            }

            PlayerSettings.iOS.appleDeveloperTeamID = teamId;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.buildNumber = "1";

            Debug.Log($"[AutoTestBuilder] iOS Team ID: {teamId}");

            var options = new BuildPlayerOptions
            {
                scenes = GetScenes(),
                locationPathName = $"{BuildDir}/iOS",
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            };

            RunBuild(options, "iOS");
        });
    }

    // ================================================================
    // Internals
    // ================================================================

    /// <summary>
    /// 保存 Player/Editor 设置，调用 build 动作，然后恢复原值。
    /// 避免 CLI 构建污染开发者本地项目（company/product/defines 等被持久化）。
    /// </summary>
    static void RunWithRestoredSettings(BuildTargetGroup group, Action build)
    {
        var savedCompany = PlayerSettings.companyName;
        var savedProduct = PlayerSettings.productName;
#pragma warning disable CS0618
        var savedDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#pragma warning restore CS0618

        try
        {
            Debug.Log($"[AutoTestBuilder] Setting up for {group}");
            PlayerSettings.companyName = "BaseCityTest";
            PlayerSettings.productName = "curl-unity-test";
            AddDefine(group, TestDefine);

            build();
        }
        finally
        {
            PlayerSettings.companyName = savedCompany;
            PlayerSettings.productName = savedProduct;
#pragma warning disable CS0618
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, savedDefines);
#pragma warning restore CS0618
            Debug.Log($"[AutoTestBuilder] Restored PlayerSettings for {group}");
        }
    }

    static void AddDefine(BuildTargetGroup group, string define)
    {
#pragma warning disable CS0618
        var current = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#pragma warning restore CS0618
        if (current.Contains(define)) return;

        var updated = string.IsNullOrEmpty(current) ? define : $"{current};{define}";
#pragma warning disable CS0618
        PlayerSettings.SetScriptingDefineSymbolsForGroup(group, updated);
#pragma warning restore CS0618
        Debug.Log($"[AutoTestBuilder] Added define: {define} (full: {updated})");
    }

    static string[] GetScenes()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            // Fallback: find SampleScene
            var guids = AssetDatabase.FindAssets("t:Scene SampleScene");
            if (guids.Length > 0)
                scenes = new[] { AssetDatabase.GUIDToAssetPath(guids[0]) };
        }

        Debug.Log($"[AutoTestBuilder] Build scenes: {string.Join(", ", scenes)}");
        return scenes;
    }

    static void RunBuild(BuildPlayerOptions options, string platformName)
    {
        Debug.Log($"[AutoTestBuilder] Starting {platformName} build -> {options.locationPathName}");

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            var sizeMB = summary.totalSize / (1024.0 * 1024.0);
            Debug.Log($"[AutoTestBuilder] {platformName} build succeeded: " +
                      $"{options.locationPathName} ({sizeMB:F1} MB)");
        }
        else
        {
            Debug.LogError($"[AutoTestBuilder] {platformName} build FAILED: {summary.result}");
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError($"  {msg.content}");
                }
            }
            EditorApplication.Exit(2);
        }
    }
}
#endif
