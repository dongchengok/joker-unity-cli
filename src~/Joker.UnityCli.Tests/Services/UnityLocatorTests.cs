using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class UnityLocatorTests : IDisposable
{
    private readonly string _tempDir;

    public UnityLocatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"JokerLocatorTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Locate_WithExplicitPath_ReturnsInstallation()
    {
        var unityDir = Path.Combine(_tempDir, "Unity");
        Directory.CreateDirectory(unityDir);
        var exePath = Path.Combine(unityDir, "Unity.exe");
        File.WriteAllText(exePath, "");

        var locator = new UnityLocator();

        var result = locator.Locate(exePath);

        result.Should().NotBeNull();
        result!.Path.Should().Be(exePath);
    }

    [Fact]
    public void Locate_WithInvalidPath_ReturnsNull()
    {
        var locator = new UnityLocator();

        var result = locator.Locate("/nonexistent/Unity.exe");

        result.Should().BeNull();
    }

    [Fact]
    public void Locate_FromHubDirectory_FindsLatestVersion()
    {
        var hubDir = Path.Combine(_tempDir, "Hub", "Editor");
        var v1 = Path.Combine(hubDir, "2022.3.20f1", "Editor");
        var v2 = Path.Combine(hubDir, "2023.2.0f1", "Editor");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(v2);
        File.WriteAllText(Path.Combine(v1, "Unity.exe"), "");
        File.WriteAllText(Path.Combine(v2, "Unity.exe"), "");

        var locator = new UnityLocator(hubDir);

        var result = locator.Locate();

        result.Should().NotBeNull();
        result!.Version.Should().Be("2023.2.0f1");
    }

    [Fact]
    public void Locate_FromHubWithVersion_FindsSpecificVersion()
    {
        var hubDir = Path.Combine(_tempDir, "Hub", "Editor");
        var v1 = Path.Combine(hubDir, "2022.3.20f1", "Editor");
        var v2 = Path.Combine(hubDir, "2023.2.0f1", "Editor");
        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(v2);
        File.WriteAllText(Path.Combine(v1, "Unity.exe"), "");
        File.WriteAllText(Path.Combine(v2, "Unity.exe"), "");

        var locator = new UnityLocator(hubDir);

        var result = locator.Locate("2022.3.20f1");

        result.Should().NotBeNull();
        result!.Version.Should().Be("2022.3.20f1");
    }

    [Fact]
    public void Locate_NoHubDirectory_ReturnsNull()
    {
        var locator = new UnityLocator(Path.Combine(_tempDir, "NoHub"));

        var result = locator.Locate();

        result.Should().BeNull();
    }
}
