using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class ProjectDetectorTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"JokerTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateUnityProject(string name = "TestProject")
    {
        var projectDir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets"));
        Directory.CreateDirectory(Path.Combine(projectDir, "ProjectSettings"));

        File.WriteAllText(
            Path.Combine(projectDir, "ProjectSettings", "ProjectVersion.txt"),
            "m_EditorVersion: 2022.3.20f1\nm_EditorVersionWithRevision: 2022.3.20f1");

        var manifest = @"{
  ""dependencies"": {
    ""com.unity.render-pipelines.universal"": ""14.0.8"",
    ""com.unity.modules.ai"": ""1.0.0""
  }
}";
        Directory.CreateDirectory(Path.Combine(projectDir, "Packages"));
        File.WriteAllText(Path.Combine(projectDir, "Packages", "manifest.json"), manifest);

        return projectDir;
    }

    [Fact]
    public void Detect_ValidUnityProject_ReturnsProject()
    {
        var projectDir = CreateUnityProject();
        var detector = new ProjectDetector();

        var result = detector.Detect(projectDir);

        result.Should().NotBeNull();
        result!.Path.Should().Be(projectDir);
    }

    [Fact]
    public void Detect_ValidUnityProject_ParsesVersion()
    {
        var projectDir = CreateUnityProject();
        var detector = new ProjectDetector();

        var result = detector.Detect(projectDir);

        result!.UnityVersion.Should().Be("2022.3.20f1");
    }

    [Fact]
    public void Detect_ValidUnityProject_ParsesPackageDependencies()
    {
        var projectDir = CreateUnityProject();
        var detector = new ProjectDetector();

        var result = detector.Detect(projectDir);

        result!.PackageDependencies.Should().Contain("com.unity.render-pipelines.universal");
        result.PackageDependencies.Should().Contain("com.unity.modules.ai");
    }

    [Fact]
    public void Detect_InvalidPath_ReturnsNull()
    {
        var detector = new ProjectDetector();

        var result = detector.Detect("/nonexistent/path");

        result.Should().BeNull();
    }

    [Fact]
    public void Detect_DirectoryWithoutAssets_ReturnsNull()
    {
        var dir = Path.Combine(_tempDir, "NotUnity");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "ProjectSettings"));
        var detector = new ProjectDetector();

        var result = detector.Detect(dir);

        result.Should().BeNull();
    }

    [Fact]
    public void DetectFromCurrentDirectory_FindsProjectInParent()
    {
        var projectDir = CreateUnityProject();
        var subDir = Path.Combine(projectDir, "Assets", "Scripts");
        Directory.CreateDirectory(subDir);
        var detector = new ProjectDetector();

        var result = detector.DetectFromCurrentDirectory(subDir);

        result.Should().NotBeNull();
        result!.Path.Should().Be(projectDir);
    }
}
