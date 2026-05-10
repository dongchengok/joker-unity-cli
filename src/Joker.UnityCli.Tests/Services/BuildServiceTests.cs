using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class BuildServiceTests : IDisposable
{
    private readonly string _tempDir;

    public BuildServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"JokerBuildTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void BuildCommandArgs_WindowsStandalone_GeneratesCorrectArgs()
    {
        var service = new BuildService();

        var args = service.BuildCommandArgs(
            projectPath: "C:/Projects/MyGame",
            unityPath: "C:/Unity/Editor/Unity.exe",
            buildTarget: "Win64",
            executeMethod: "Joker.UnityCli.Editor.BuildPipeline.Build",
            outputPath: "C:/Builds/MyGame.exe"
        );

        args.Should().Contain("-batchmode");
        args.Should().Contain("-quit");
        args.Should().Contain("-projectPath");
        args.Should().Contain("C:/Projects/MyGame");
        args.Should().Contain("-executeMethod");
        args.Should().Contain("Joker.UnityCli.Editor.BuildPipeline.Build");
        args.Should().Contain("-buildTarget");
        args.Should().Contain("Win64");
    }

    [Fact]
    public void BuildCommandArgs_WithCustomScenes_IncludesScenes()
    {
        var service = new BuildService();

        var args = service.BuildCommandArgs(
            projectPath: "C:/Projects/MyGame",
            unityPath: "C:/Unity/Editor/Unity.exe",
            buildTarget: "Win64",
            executeMethod: "Joker.UnityCli.Editor.BuildPipeline.Build",
            outputPath: "C:/Builds/MyGame.exe",
            scenes: new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/Game.unity" }
        );

        args.Should().Contain("-scenes");
        args.Should().Contain("Assets/Scenes/Main.unity,Assets/Scenes/Game.unity");
    }

    [Fact]
    public void BuildCommandArgs_WithLogFile_IncludesLogFile()
    {
        var service = new BuildService();

        var args = service.BuildCommandArgs(
            projectPath: "C:/Projects/MyGame",
            unityPath: "C:/Unity/Editor/Unity.exe",
            buildTarget: "Win64",
            executeMethod: "Joker.UnityCli.Editor.BuildPipeline.Build",
            outputPath: "C:/Builds/MyGame.exe",
            logFile: "C:/Logs/build.log"
        );

        args.Should().Contain("-logFile");
        args.Should().Contain("C:/Logs/build.log");
    }

    [Fact]
    public void BuildCommandArgs_WithoutOptionalParams_MinimalArgs()
    {
        var service = new BuildService();

        var args = service.BuildCommandArgs(
            projectPath: "C:/Projects/MyGame",
            unityPath: "C:/Unity/Editor/Unity.exe",
            buildTarget: "Win64",
            executeMethod: "Joker.UnityCli.Editor.BuildPipeline.Build",
            outputPath: "C:/Builds/MyGame.exe"
        );

        args.Should().NotContain("-scenes");
        args.Should().NotContain("-logFile");
    }
}
