using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>시험 브랜치의 다중 프로세스 검증에 사용하는 개발 빌드 메뉴.</summary>
public static class LobbyPrototypeTools
{
    private static string WorkDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/LobbyPrototypeTests"));
    [Serializable] private sealed class Result
    {
        public bool success;
        public string result, output, exception;
        public long errors, warnings;
    }

    [MenuItem("Tools/Lobby Prototype/Build Development Test Player")]
    public static void BuildDevelopmentTestPlayer()
    {
        Directory.CreateDirectory(WorkDirectory);
        var result = new Result { output = Path.Combine(WorkDirectory, "lobby-prototype.app") };
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before building the lobby test player.");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = result.output,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development
            });
            if (report == null) throw new InvalidOperationException("Unity rejected the build; see the Editor console.");
            result.success = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            result.result = report.summary.result.ToString();
            result.errors = report.summary.totalErrors;
            result.warnings = report.summary.totalWarnings;
        }
        catch (Exception exception)
        {
            result.result = "Exception";
            result.exception = exception.ToString();
        }
        File.WriteAllText(Path.Combine(WorkDirectory, "lobby-prototype-build-result.json"), JsonUtility.ToJson(result, true));
        if (result.success) Debug.Log("LOBBY_PROTOTYPE_BUILD_SUCCEEDED: " + result.output);
        else Debug.LogError("LOBBY_PROTOTYPE_BUILD_FAILED: " + result.result + " " + result.exception);
    }
}
