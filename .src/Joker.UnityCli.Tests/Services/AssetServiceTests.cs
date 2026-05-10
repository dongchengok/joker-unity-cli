using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class AssetServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _assetsDir;

    public AssetServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"JokerAssetTest_{Guid.NewGuid():N}");
        _assetsDir = Path.Combine(_tempDir, "Assets");
        Directory.CreateDirectory(_assetsDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void CreateAsset(string relativePath, string guid)
    {
        var fullPath = Path.Combine(_assetsDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, "");

        var metaContent = $"fileFormatVersion: 2\nguid: {guid}\n";
        File.WriteAllText(fullPath + ".meta", metaContent);
    }

    [Fact]
    public void ListAssets_ReturnsAllAssets()
    {
        CreateAsset("Scripts/Player.cs", "a1b2c3d4e5f67890");
        CreateAsset("Scenes/Main.unity", "b2c3d4e5f6789012");
        var service = new AssetService();

        var assets = service.ListAssets(_assetsDir).ToList();

        assets.Should().HaveCount(2);
    }

    [Fact]
    public void ListAssets_ParsesGuidFromMeta()
    {
        CreateAsset("Scripts/Player.cs", "a1b2c3d4e5f67890");
        var service = new AssetService();

        var assets = service.ListAssets(_assetsDir).ToList();

        assets[0].Guid.Should().Be("a1b2c3d4e5f67890");
    }

    [Fact]
    public void ListAssets_RelativePathIsFromAssetsRoot()
    {
        CreateAsset("Scripts/Player.cs", "a1b2c3d4e5f67890");
        var service = new AssetService();

        var assets = service.ListAssets(_assetsDir).ToList();

        assets[0].RelativePath.Should().Be("Scripts/Player.cs");
    }

    [Fact]
    public void SearchAssets_FiltersByName()
    {
        CreateAsset("Scripts/Player.cs", "a1b2c3d4e5f67890");
        CreateAsset("Scripts/Enemy.cs", "b2c3d4e5f6789012");
        CreateAsset("Scenes/Main.unity", "c3d4e5f678901234");
        var service = new AssetService();

        var assets = service.SearchAssets(_assetsDir, "Player").ToList();

        assets.Should().HaveCount(1);
        assets[0].RelativePath.Should().Be("Scripts/Player.cs");
    }

    [Fact]
    public void SearchAssets_CaseInsensitive()
    {
        CreateAsset("Scripts/PlayerController.cs", "a1b2c3d4e5f67890");
        var service = new AssetService();

        var assets = service.SearchAssets(_assetsDir, "player").ToList();

        assets.Should().HaveCount(1);
    }

    [Fact]
    public void SearchAssets_ByExtension()
    {
        CreateAsset("Scripts/Player.cs", "a1b2c3d4e5f67890");
        CreateAsset("Scenes/Main.unity", "b2c3d4e5f6789012");
        var service = new AssetService();

        var assets = service.SearchAssets(_assetsDir, ".unity").ToList();

        assets.Should().HaveCount(1);
        assets[0].RelativePath.Should().Be("Scenes/Main.unity");
    }

    [Fact]
    public void ListAssets_SkipsMetaFiles()
    {
        CreateAsset("Player.cs", "a1b2c3d4e5f67890");
        var service = new AssetService();

        var assets = service.ListAssets(_assetsDir).ToList();

        assets.Should().HaveCount(1);
        assets[0].RelativePath.Should().Be("Player.cs");
    }
}
