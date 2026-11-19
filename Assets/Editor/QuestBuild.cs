using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Meta Quest용 APK 빌드. 메뉴(Build > Quest APK) 또는 커맨드라인 -executeMethod QuestBuild.BuildApk 로 실행.
/// 결과물: Builds/Android/Untrodden.apk
/// </summary>
public static class QuestBuild
{
    const string OutputPath = "Builds/Android/Untrodden.apk";

    [MenuItem("Build/Quest APK")]
    public static void BuildApk()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));

        var options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = OutputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[QuestBuild] {report.summary.result} - {report.summary.totalSize / (1024 * 1024)} MB, errors: {report.summary.totalErrors}");

        if (Application.isBatchMode)
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
