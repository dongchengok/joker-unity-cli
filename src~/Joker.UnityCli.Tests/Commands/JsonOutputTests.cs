using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Joker.UnityCli.Commands;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Tests.Commands;

public class JsonOutputTests
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    // ===================================================================
    // JSON serialization content tests (no CommandApp needed)
    // ===================================================================

    [Fact]
    public void JsonSerialization_UnityProject_ProducesValidJson()
    {
        // Arrange
        var project = new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string> { "com.unity.ugui", "com.unity.textmeshpro" }
        };

        // Act
        var json = JsonSerializer.Serialize(project, JsonSerializerOptions);
        var doc = JsonDocument.Parse(json);

        // Assert
        doc.RootElement.TryGetProperty("name", out var name).Should().BeTrue();
        name.GetString().Should().Be("TestProject");
        doc.RootElement.TryGetProperty("path", out var path).Should().BeTrue();
        path.GetString().Should().Be("/path/to/project");
        doc.RootElement.TryGetProperty("unityVersion", out var version).Should().BeTrue();
        version.GetString().Should().Be("2022.3.20f1");
        doc.RootElement.TryGetProperty("packageDependencies", out var deps).Should().BeTrue();
        deps.GetArrayLength().Should().Be(2);
        deps[0].GetString().Should().Be("com.unity.ugui");
        deps[1].GetString().Should().Be("com.unity.textmeshpro");
    }

    [Fact]
    public void JsonSerialization_AssetInfoList_ProducesValidJsonArray()
    {
        // Arrange
        var assets = new List<AssetInfo>
        {
            new() { RelativePath = "Assets/Scripts/Main.cs", Guid = "abc123", Extension = ".cs" },
            new() { RelativePath = "Assets/Textures/icon.png", Guid = "def456", Extension = ".png" }
        };

        // Act
        var json = JsonSerializer.Serialize(assets, JsonSerializerOptions);
        var doc = JsonDocument.Parse(json);

        // Assert
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(2);
        doc.RootElement[0].TryGetProperty("relativePath", out var rp).Should().BeTrue();
        rp.GetString().Should().Be("Assets/Scripts/Main.cs");
        doc.RootElement[0].TryGetProperty("guid", out var guid).Should().BeTrue();
        guid.GetString().Should().Be("abc123");
        doc.RootElement[0].TryGetProperty("extension", out var ext).Should().BeTrue();
        ext.GetString().Should().Be(".cs");
        doc.RootElement[1].TryGetProperty("relativePath", out var rp2).Should().BeTrue();
        rp2.GetString().Should().Be("Assets/Textures/icon.png");
    }

    [Fact]
    public void JsonSerialization_EmptyAssetList_ProducesEmptyJsonArray()
    {
        // Arrange
        var assets = new List<AssetInfo>();

        // Act
        var json = JsonSerializer.Serialize(assets, JsonSerializerOptions);
        var doc = JsonDocument.Parse(json);

        // Assert
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void JsonSerialization_BuildResult_Success_ProducesValidJson()
    {
        // Arrange
        var buildResult = new BuildResult
        {
            Success = true,
            LogPath = "/path/to/log.txt",
            OutputPath = "/path/to/project/Builds/Win64",
            Duration = TimeSpan.FromSeconds(45.3)
        };

        // Act
        var json = JsonSerializer.Serialize(buildResult, JsonSerializerOptions);
        var doc = JsonDocument.Parse(json);

        // Assert
        doc.RootElement.TryGetProperty("success", out var success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();
        doc.RootElement.TryGetProperty("logPath", out var logPath).Should().BeTrue();
        logPath.GetString().Should().Be("/path/to/log.txt");
        doc.RootElement.TryGetProperty("outputPath", out var outputPath).Should().BeTrue();
        outputPath.GetString().Should().Be("/path/to/project/Builds/Win64");
        doc.RootElement.TryGetProperty("duration", out var duration).Should().BeTrue();
        // TimeSpan serializes as a string
        duration.ValueKind.Should().Be(JsonValueKind.String);
        duration.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void JsonSerialization_BuildResult_Failure_ProducesValidJson()
    {
        // Arrange
        var buildResult = new BuildResult
        {
            Success = false,
            LogPath = "/path/to/error-log.txt",
            OutputPath = "/path/to/project/Builds/Win64",
            Duration = TimeSpan.FromMinutes(2.5)
        };

        // Act
        var json = JsonSerializer.Serialize(buildResult, JsonSerializerOptions);
        var doc = JsonDocument.Parse(json);

        // Assert
        doc.RootElement.TryGetProperty("success", out var success).Should().BeTrue();
        success.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void JsonSerialization_ErrorMessage_ProducesValidJson()
    {
        // Arrange
        var error = new { error = "No Unity project found at the specified path." };

        // Act
        var json = JsonSerializer.Serialize(error, JsonSerializerOptions);
        var doc = JsonDocument.Parse(json);

        // Assert
        doc.RootElement.TryGetProperty("error", out var errorProp).Should().BeTrue();
        errorProp.GetString().Should().Be("No Unity project found at the specified path.");
    }

    [Fact]
    public void JsonSerialization_OutputContainsNoAnsiCodes()
    {
        // Arrange
        var project = new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string> { "com.unity.ugui" }
        };

        // Act
        var json = JsonSerializer.Serialize(project, JsonSerializerOptions);

        // Assert - JSON output should not contain ANSI escape sequences
        json.Should().NotContain("\x1b");
        // Verify it's valid JSON
        var action = () => JsonDocument.Parse(json);
        action.Should().NotThrow("because the output should be valid JSON");
    }

    // ===================================================================
    // CommandApp integration tests (return code verification)
    // ===================================================================

    [Fact]
    public async Task InfoCommand_JsonFlag_ValidProject_ReturnsZero()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string> { "com.unity.ugui", "com.unity.textmeshpro" }
        });

        var app = CreateInfoApp(projectDetector);

        // Act
        var result = await app.RunAsync(["info", "--project", "/path/to/project", "--json"]);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task InfoCommand_JsonFlag_NoProject_ReturnsNonZero()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns((UnityProject?)null);

        var app = CreateInfoApp(projectDetector);

        // Act
        var result = await app.RunAsync(["info", "--project", "/invalid", "--json"]);

        // Assert
        result.Should().Be(1);
    }

    [Fact]
    public async Task AssetsCommand_JsonFlag_ValidAssets_ReturnsZero()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var assetService = Substitute.For<IAssetService>();
        assetService.ListAssets(Arg.Any<string>()).Returns(new List<AssetInfo>
        {
            new() { RelativePath = "Assets/Scripts/Main.cs", Guid = "abc123", Extension = ".cs" },
            new() { RelativePath = "Assets/Textures/icon.png", Guid = "def456", Extension = ".png" }
        });

        var app = CreateAssetsApp(projectDetector, assetService);

        // Act
        var result = await app.RunAsync(["assets", "--project", "/path/to/project", "--json"]);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task AssetsCommand_JsonFlag_NoAssets_ReturnsZero()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var assetService = Substitute.For<IAssetService>();
        assetService.ListAssets(Arg.Any<string>()).Returns(Array.Empty<AssetInfo>());

        var app = CreateAssetsApp(projectDetector, assetService);

        // Act
        var result = await app.RunAsync(["assets", "--project", "/path/to/project", "--json"]);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task AssetsCommand_JsonFlag_NoProject_ReturnsNonZero()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns((UnityProject?)null);

        var assetService = Substitute.For<IAssetService>();

        var app = CreateAssetsApp(projectDetector, assetService);

        // Act
        var result = await app.RunAsync(["assets", "--project", "/invalid", "--json"]);

        // Assert
        result.Should().Be(1);
    }

    [Fact]
    public async Task BuildCommand_JsonFlag_SuccessfulBuild_ReturnsZero()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var unityLocator = Substitute.For<IUnityLocator>();
        unityLocator.Locate(Arg.Any<string?>()).Returns(new UnityInstallation
        {
            Path = "/path/to/Unity.exe",
            Version = "2022.3.20f1"
        });

        var buildService = Substitute.For<IBuildService>();
        buildService.BuildAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string[]?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BuildResult
            {
                Success = true,
                LogPath = "/path/to/log.txt",
                OutputPath = "/path/to/project/Builds/Win64",
                Duration = TimeSpan.FromSeconds(45.3)
            });

        var app = CreateBuildApp(projectDetector, unityLocator, buildService);

        // Act
        var result = await app.RunAsync(["build", "Win64", "--project", "/path/to/project", "--json"]);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task BuildCommand_JsonFlag_FailedBuild_ReturnsNonZero()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var unityLocator = Substitute.For<IUnityLocator>();
        unityLocator.Locate(Arg.Any<string?>()).Returns(new UnityInstallation
        {
            Path = "/path/to/Unity.exe",
            Version = "2022.3.20f1"
        });

        var buildService = Substitute.For<IBuildService>();
        buildService.BuildAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string[]?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BuildResult
            {
                Success = false,
                LogPath = "/path/to/error-log.txt",
                OutputPath = "/path/to/project/Builds/Win64",
                Duration = TimeSpan.FromMinutes(2.5)
            });

        var app = CreateBuildApp(projectDetector, unityLocator, buildService);

        // Act
        var result = await app.RunAsync(["build", "Win64", "--project", "/path/to/project", "--json"]);

        // Assert
        result.Should().Be(1);
    }

    [Fact]
    public async Task BuildCommand_JsonFlag_NoProject_ReturnsNonZero()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns((UnityProject?)null);

        var unityLocator = Substitute.For<IUnityLocator>();
        var buildService = Substitute.For<IBuildService>();

        var app = CreateBuildApp(projectDetector, unityLocator, buildService);

        // Act
        var result = await app.RunAsync(["build", "Win64", "--project", "/invalid", "--json"]);

        // Assert
        result.Should().Be(1);
    }

    // --- Non-JSON mode still works ---

    [Fact]
    public async Task InfoCommand_WithoutJsonFlag_ReturnsZero()
    {
        // Arrange
        var projectDetector = Substitute.For<IProjectDetector>();
        projectDetector.Detect(Arg.Any<string>()).Returns(new UnityProject
        {
            Name = "TestProject",
            Path = "/path/to/project",
            UnityVersion = "2022.3.20f1",
            PackageDependencies = new List<string>()
        });

        var app = CreateInfoApp(projectDetector);

        // Act
        var result = await app.RunAsync(["info", "--project", "/path/to/project"]);

        // Assert
        result.Should().Be(0);
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    private static CommandApp CreateInfoApp(IProjectDetector projectDetector)
    {
        var services = new ServiceCollection();
        services.AddSingleton(projectDetector);
        var registrar = new DependencyInjectionTypeRegistrar(services);

        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.AddCommand<InfoCommand>("info");
        });
        return app;
    }

    private static CommandApp CreateAssetsApp(IProjectDetector projectDetector, IAssetService assetService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(projectDetector);
        services.AddSingleton(assetService);
        var registrar = new DependencyInjectionTypeRegistrar(services);

        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.AddCommand<AssetsCommand>("assets");
        });
        return app;
    }

    private static CommandApp CreateBuildApp(
        IProjectDetector projectDetector,
        IUnityLocator unityLocator,
        IBuildService buildService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(projectDetector);
        services.AddSingleton(unityLocator);
        services.AddSingleton(buildService);
        var registrar = new DependencyInjectionTypeRegistrar(services);

        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.AddCommand<BuildCommand>("build");
        });
        return app;
    }
}
