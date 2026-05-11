# CLI 基础框架 TDD 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用 TDD 方式构建 joker-unity-cli 的基础框架，包含项目检测、构建触发、资源管理三个核心模块。

**Architecture:** 独立 .NET 8 终端 CLI，使用 Spectre.Console.Cli 解析命令。三层结构：Commands（参数解析）→ Services（业务逻辑）→ 文件系统/Unity.exe。Services 层通过临时目录进行真实文件系统测试，不 mock。

**Tech Stack:** C# / .NET 8、Spectre.Console.Cli、xUnit、FluentAssertions

**Design Spec:** `docs/superpowers/specs/2025-05-10-cli-framework-design.md`

---

## File Structure

```
src/
├── Joker.UnityCli.sln
├── Joker.UnityCli/
│   ├── Joker.UnityCli.csproj
│   ├── Program.cs
│   ├── Models/
│   │   ├── UnityProject.cs
│   │   ├── UnityInstallation.cs
│   │   ├── AssetInfo.cs
│   │   └── BuildResult.cs
│   ├── Services/
│   │   ├── IProjectDetector.cs
│   │   ├── ProjectDetector.cs
│   │   ├── IUnityLocator.cs
│   │   ├── UnityLocator.cs
│   │   ├── IAssetService.cs
│   │   ├── AssetService.cs
│   │   ├── IBuildService.cs
│   │   └── BuildService.cs
│   └── Commands/
│       ├── InfoCommand.cs
│       ├── BuildCommand.cs
│       └── AssetsCommand.cs
└── Joker.UnityCli.Tests/
    ├── Joker.UnityCli.Tests.csproj
    ├── Services/
    │   ├── ProjectDetectorTests.cs
    │   ├── UnityLocatorTests.cs
    │   ├── AssetServiceTests.cs
    │   └── BuildServiceTests.cs
    └── Commands/
        └── InfoCommandTests.cs
```

---

### Task 1: 创建 .NET 解决方案和项目结构

**Files:**
- Create: `src/Joker.UnityCli.sln`
- Create: `src/Joker.UnityCli/Joker.UnityCli.csproj`
- Create: `src/Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj`

- [ ] **Step 1: 创建解决方案和项目**

```bash
cd E:/Work/joker-unity-cli
mkdir -p src
cd src
dotnet new sln -n Joker.UnityCli
dotnet new console -n Joker.UnityCli --framework net8.0
dotnet new xunit -n Joker.UnityCli.Tests --framework net8.0
dotnet sln add Joker.UnityCli/Joker.UnityCli.csproj
dotnet sln add Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj
```

- [ ] **Step 2: 添加 NuGet 依赖**

```bash
cd E:/Work/joker-unity-cli/src
dotnet add Joker.UnityCli/Joker.UnityCli.csproj package Spectre.Console.Cli
dotnet add Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj package FluentAssertions
dotnet add Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj reference Joker.UnityCli/Joker.UnityCli.csproj
```

- [ ] **Step 3: 创建目录结构**

```bash
cd E:/Work/joker-unity-cli/src/Joker.UnityCli
mkdir -p Models Services Commands
cd ../Joker.UnityCli.Tests
mkdir -p Services Commands
```

- [ ] **Step 4: 验证项目能编译和运行测试**

```bash
cd E:/Work/joker-unity-cli/src
dotnet build
dotnet test
```

Expected: BUILD SUCCEEDED, 1 test passed (xUnit 默认有一个示例测试)

- [ ] **Step 5: 删除示例文件，提交**

```bash
cd E:/Work/joker-unity-cli/src
rm Joker.UnityCli/Class1.cs 2>/dev/null
rm Joker.UnityCli.Tests/UnitTest1.cs 2>/dev/null
dotnet test
```

Expected: BUILD SUCCEEDED, 0 tests

---

### Task 2: 实现 UnityProject 模型 + ProjectDetector（TDD）

**Files:**
- Create: `src/Joker.UnityCli/Models/UnityProject.cs`
- Create: `src/Joker.UnityCli/Services/IProjectDetector.cs`
- Create: `src/Joker.UnityCli/Services/ProjectDetector.cs`
- Create: `src/Joker.UnityCli.Tests/Services/ProjectDetectorTests.cs`

- [ ] **Step 1: RED — 写 ProjectDetector 的失败测试**

```csharp
// src/Joker.UnityCli.Tests/Services/ProjectDetectorTests.cs
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
```

- [ ] **Step 2: 运行测试，确认失败**

```bash
cd E:/Work/joker-unity-cli/src
dotnet test Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj --filter "FullyQualifiedName~ProjectDetectorTests" -v n
```

Expected: 编译失败（类型不存在）

- [ ] **Step 3: GREEN — 写 Models 和 IProjectDetector 接口**

```csharp
// src/Joker.UnityCli/Models/UnityProject.cs
namespace Joker.UnityCli.Models;

public class UnityProject
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string UnityVersion { get; set; } = "";
    public List<string> PackageDependencies { get; set; } = new();
}
```

```csharp
// src/Joker.UnityCli/Services/IProjectDetector.cs
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface IProjectDetector
{
    UnityProject? Detect(string path);
    UnityProject? DetectFromCurrentDirectory(string startPath);
}
```

```csharp
// src/Joker.UnityCli/Services/ProjectDetector.cs
using System.Text.Json;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class ProjectDetector : IProjectDetector
{
    public UnityProject? Detect(string path)
    {
        if (!Directory.Exists(path))
            return null;

        var hasAssets = Directory.Exists(System.IO.Path.Combine(path, "Assets"));
        var hasSettings = Directory.Exists(System.IO.Path.Combine(path, "ProjectSettings"));

        if (!hasAssets || !hasSettings)
            return null;

        var project = new UnityProject
        {
            Path = System.IO.Path.GetFullPath(path),
            Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar))
        };

        var versionFile = System.IO.Path.Combine(path, "ProjectSettings", "ProjectVersion.txt");
        if (File.Exists(versionFile))
        {
            var content = File.ReadAllText(versionFile);
            var match = System.Text.RegularExpressions.Regex.Match(content, @"m_EditorVersion:\s*(.+?)(\r?\n|$)");
            if (match.Success)
                project.UnityVersion = match.Groups[1].Value.Trim();
        }

        var manifestPath = System.IO.Path.Combine(path, "Packages", "manifest.json");
        if (File.Exists(manifestPath))
        {
            var json = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("dependencies", out var deps))
            {
                project.PackageDependencies = deps.EnumerateObject().Select(p => p.Name).ToList();
            }
        }

        return project;
    }

    public UnityProject? DetectFromCurrentDirectory(string startPath)
    {
        var current = startPath;
        while (current != null)
        {
            var result = Detect(current);
            if (result != null)
                return result;

            var parent = System.IO.Path.GetDirectoryName(current);
            if (parent == current)
                break;
            current = parent;
        }

        return null;
    }
}
```

- [ ] **Step 4: 运行测试，确认通过**

```bash
cd E:/Work/joker-unity-cli/src
dotnet test Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj --filter "FullyQualifiedName~ProjectDetectorTests" -v n
```

Expected: 6 tests passed, 0 failed

- [ ] **Step 5: 提交**

```bash
cd E:/Work/joker-unity-cli
git add src/Joker.UnityCli/Models/UnityProject.cs src/Joker.UnityCli/Services/ src/Joker.UnityCli.Tests/Services/ProjectDetectorTests.cs
git commit -m "feat: add ProjectDetector with TDD - detect and parse Unity projects"
```

---

### Task 3: 实现 UnityLocator（TDD）

**Files:**
- Create: `src/Joker.UnityCli/Models/UnityInstallation.cs`
- Create: `src/Joker.UnityCli/Services/IUnityLocator.cs`
- Create: `src/Joker.UnityCli/Services/UnityLocator.cs`
- Create: `src/Joker.UnityCli.Tests/Services/UnityLocatorTests.cs`

- [ ] **Step 1: RED — 写 UnityLocator 的失败测试**

```csharp
// src/Joker.UnityCli.Tests/Services/UnityLocatorTests.cs
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
```

- [ ] **Step 2: 运行测试，确认失败**

```bash
cd E:/Work/joker-unity-cli/src
dotnet test Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj --filter "FullyQualifiedName~UnityLocatorTests" -v n
```

Expected: 编译失败（类型不存在）

- [ ] **Step 3: GREEN — 写 Models 和 UnityLocator**

```csharp
// src/Joker.UnityCli/Models/UnityInstallation.cs
namespace Joker.UnityCli.Models;

public class UnityInstallation
{
    public string Path { get; set; } = "";
    public string Version { get; set; } = "";
}
```

```csharp
// src/Joker.UnityCli/Services/IUnityLocator.cs
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface IUnityLocator
{
    UnityInstallation? Locate(string? explicitPath = null);
    UnityInstallation? Locate(string hubPath, string? version = null);
}
```

```csharp
// src/Joker.UnityCli/Services/UnityLocator.cs
using System.Text.RegularExpressions;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class UnityLocator : IUnityLocator
{
    private readonly string? _customHubPath;
    private const string DefaultHubPath = @"C:\Program Files\Unity\Hub\Editor";
    private const string ExeName = "Unity.exe";

    public UnityLocator() { }

    public UnityLocator(string hubPath)
    {
        _customHubPath = hubPath;
    }

    public UnityInstallation? Locate(string? explicitPath = null)
    {
        if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
        {
            return new UnityInstallation
            {
                Path = explicitPath,
                Version = ExtractVersionFromPath(explicitPath)
            };
        }

        return null;
    }

    public UnityInstallation? Locate(string hubPath, string? version = null)
    {
        var hub = _customHubPath ?? hubPath;
        if (!Directory.Exists(hub))
            return null;

        if (!string.IsNullOrEmpty(version))
        {
            var versionDir = System.IO.Path.Combine(hub, version, "Editor");
            var exe = System.IO.Path.Combine(versionDir, ExeName);
            if (File.Exists(exe))
            {
                return new UnityInstallation { Path = exe, Version = version };
            }
            return null;
        }

        var latest = Directory.GetDirectories(hub)
            .Select(d => System.IO.Path.GetFileName(d))
            .Where(v => Regex.IsMatch(v, @"^\d{4}\.\d+\.\d+"))
            .OrderByDescending(v => v)
            .FirstOrDefault();

        if (latest == null)
            return null;

        var latestExe = System.IO.Path.Combine(hub, latest, "Editor", ExeName);
        if (!File.Exists(latestExe))
            return null;

        return new UnityInstallation { Path = latestExe, Version = latest };
    }

    private static string ExtractVersionFromPath(string path)
    {
        var match = Regex.Match(path, @"(\d{4}\.\d+\.\d+\w\d+\w?\d*)");
        return match.Success ? match.Groups[1].Value : "";
    }
}
```

- [ ] **Step 4: 运行测试，确认通过**

```bash
cd E:/Work/joker-unity-cli/src
dotnet test Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj --filter "FullyQualifiedName~UnityLocatorTests" -v n
```

Expected: 5 tests passed, 0 failed

- [ ] **Step 5: 提交**

```bash
cd E:/Work/joker-unity-cli
git add src/Joker.UnityCli/Models/UnityInstallation.cs src/Joker.UnityCli/Services/IUnityLocator.cs src/Joker.UnityCli/Services/UnityLocator.cs src/Joker.UnityCli.Tests/Services/UnityLocatorTests.cs
git commit -m "feat: add UnityLocator with TDD - find Unity installations"
```

---

### Task 4: 实现 AssetService（TDD）

**Files:**
- Create: `src/Joker.UnityCli/Models/AssetInfo.cs`
- Create: `src/Joker.UnityCli/Services/IAssetService.cs`
- Create: `src/Joker.UnityCli/Services/AssetService.cs`
- Create: `src/Joker.UnityCli.Tests/Services/AssetServiceTests.cs`

- [ ] **Step 1: RED — 写 AssetService 的失败测试**

```csharp
// src/Joker.UnityCli.Tests/Services/AssetServiceTests.cs
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
```

- [ ] **Step 2: 运行测试，确认失败**

```bash
cd E:/Work/joker-unity-cli/src
dotnet test Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj --filter "FullyQualifiedName~AssetServiceTests" -v n
```

Expected: 编译失败（类型不存在）

- [ ] **Step 3: GREEN — 写 Models 和 AssetService**

```csharp
// src/Joker.UnityCli/Models/AssetInfo.cs
namespace Joker.UnityCli.Models;

public class AssetInfo
{
    public string RelativePath { get; set; } = "";
    public string Guid { get; set; } = "";
    public string Extension { get; set; } = "";
}
```

```csharp
// src/Joker.UnityCli/Services/IAssetService.cs
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface IAssetService
{
    IEnumerable<AssetInfo> ListAssets(string assetsPath);
    IEnumerable<AssetInfo> SearchAssets(string assetsPath, string query);
}
```

```csharp
// src/Joker.UnityCli/Services/AssetService.cs
using System.Text.RegularExpressions;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class AssetService : IAssetService
{
    public IEnumerable<AssetInfo> ListAssets(string assetsPath)
    {
        if (!Directory.Exists(assetsPath))
            yield break;

        foreach (var file in Directory.EnumerateFiles(assetsPath, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            var relativePath = System.IO.Path.GetRelativePath(assetsPath, file);
            var guid = ReadGuid(file + ".meta");

            yield return new AssetInfo
            {
                RelativePath = relativePath,
                Guid = guid,
                Extension = System.IO.Path.GetExtension(file)
            };
        }
    }

    public IEnumerable<AssetInfo> SearchAssets(string assetsPath, string query)
    {
        return ListAssets(assetsPath)
            .Where(a => a.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadGuid(string metaPath)
    {
        if (!File.Exists(metaPath))
            return "";

        var content = File.ReadAllText(metaPath);
        var match = Regex.Match(content, @"guid:\s*([a-f0-9]{16,32})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }
}
```

- [ ] **Step 4: 运行测试，确认通过**

```bash
cd E:/Work/joker-unity-cli/src
dotnet test Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj --filter "FullyQualifiedName~AssetServiceTests" -v n
```

Expected: 7 tests passed, 0 failed

- [ ] **Step 5: 提交**

```bash
cd E:/Work/joker-unity-cli
git add src/Joker.UnityCli/Models/AssetInfo.cs src/Joker.UnityCli/Services/IAssetService.cs src/Joker.UnityCli/Services/AssetService.cs src/Joker.UnityCli.Tests/Services/AssetServiceTests.cs
git commit -m "feat: add AssetService with TDD - list and search Unity assets"
```

---

### Task 5: 实现 BuildService（TDD）

**Files:**
- Create: `src/Joker.UnityCli/Models/BuildResult.cs`
- Create: `src/Joker.UnityCli/Services/IBuildService.cs`
- Create: `src/Joker.UnityCli/Services/BuildService.cs`
- Create: `src/Joker.UnityCli.Tests/Services/BuildServiceTests.cs`

- [ ] **Step 1: RED — 写 BuildService 的失败测试**

```csharp
// src/Joker.UnityCli.Tests/Services/BuildServiceTests.cs
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
```

- [ ] **Step 2: 运行测试，确认失败**

```bash
cd E:/Work/joker-unity-cli/src
dotnet test Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj --filter "FullyQualifiedName~BuildServiceTests" -v n
```

Expected: 编译失败（类型不存在）

- [ ] **Step 3: GREEN — 写 Models 和 BuildService**

```csharp
// src/Joker.UnityCli/Models/BuildResult.cs
namespace Joker.UnityCli.Models;

public class BuildResult
{
    public bool Success { get; set; }
    public string LogPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public TimeSpan Duration { get; set; }
}
```

```csharp
// src/Joker.UnityCli/Services/IBuildService.cs
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface IBuildService
{
    List<string> BuildCommandArgs(
        string projectPath,
        string unityPath,
        string buildTarget,
        string executeMethod,
        string outputPath,
        string[]? scenes = null,
        string? logFile = null
    );
    Task<BuildResult> BuildAsync(
        string projectPath,
        string unityPath,
        string buildTarget,
        string executeMethod,
        string outputPath,
        string[]? scenes = null,
        string? logFile = null,
        CancellationToken cancellationToken = default
    );
}
```

```csharp
// src/Joker.UnityCli/Services/BuildService.cs
using System.Diagnostics;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class BuildService : IBuildService
{
    public List<string> BuildCommandArgs(
        string projectPath,
        string unityPath,
        string buildTarget,
        string executeMethod,
        string outputPath,
        string[]? scenes = null,
        string? logFile = null)
    {
        var args = new List<string>
        {
            "-batchmode",
            "-quit",
            "-projectPath", projectPath,
            "-executeMethod", executeMethod,
            "-buildTarget", buildTarget,
        };

        if (scenes is { Length: > 0 })
        {
            args.Add("-scenes");
            args.Add(string.Join(",", scenes));
        }

        if (!string.IsNullOrEmpty(logFile))
        {
            args.Add("-logFile");
            args.Add(logFile);
        }

        return args;
    }

    public async Task<BuildResult> BuildAsync(
        string projectPath,
        string unityPath,
        string buildTarget,
        string executeMethod,
        string outputPath,
        string[]? scenes = null,
        string? logFile = null,
        CancellationToken cancellationToken = default)
    {
        var args = BuildCommandArgs(projectPath, unityPath, buildTarget, executeMethod, outputPath, scenes, logFile);
        var logPath = logFile ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"unity-build-{Guid.NewGuid():N}.log");

        var sw = Stopwatch.StartNew();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = unityPath,
                Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            await process.WaitForExitAsync(cancellationToken);
            sw.Stop();

            return new BuildResult
            {
                Success = process.ExitCode == 0,
                LogPath = logPath,
                OutputPath = outputPath,
                Duration = sw.Elapsed
            };
        }
        catch (Exception)
        {
            sw.Stop();
            return new BuildResult
            {
                Success = false,
                LogPath = logPath,
                OutputPath = outputPath,
                Duration = sw.Elapsed
            };
        }
    }
}
```

- [ ] **Step 4: 运行测试，确认通过**

```bash
cd E:/Work/joker-unity-cli/src
dotnet test Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj --filter "FullyQualifiedName~BuildServiceTests" -v n
```

Expected: 4 tests passed, 0 failed

- [ ] **Step 5: 提交**

```bash
cd E:/Work/joker-unity-cli
git add src/Joker.UnityCli/Models/BuildResult.cs src/Joker.UnityCli/Services/IBuildService.cs src/Joker.UnityCli/Services/BuildService.cs src/Joker.UnityCli.Tests/Services/BuildServiceTests.cs
git commit -m "feat: add BuildService with TDD - Unity build command generation and execution"
```

---

### Task 6: 实现 CLI 命令和 Program.cs 入口（TDD）

**Files:**
- Create: `src/Joker.UnityCli/Commands/InfoCommand.cs`
- Create: `src/Joker.UnityCli/Commands/BuildCommand.cs`
- Create: `src/Joker.UnityCli/Commands/AssetsCommand.cs`
- Create: `src/Joker.UnityCli.Tests/Commands/InfoCommandTests.cs`
- Modify: `src/Joker.UnityCli/Program.cs`

- [ ] **Step 1: RED — 写 InfoCommand 的失败测试**

```csharp
// src/Joker.UnityCli.Tests/Commands/InfoCommandTests.cs
using FluentAssertions;
using Joker.UnityCli.Commands;
using Joker.UnityCli.Services;
using Spectre.Console.Cli;
using Xunit;

namespace Joker.UnityCli.Tests.Commands;

public class InfoCommandTests : IDisposable
{
    private readonly string _tempDir;

    public InfoCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"JokerInfoCmdTest_{Guid.NewGuid():N}");
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
        Directory.CreateDirectory(Path.Combine(projectDir, "Packages"));
        File.WriteAllText(Path.Combine(projectDir, "Packages", "manifest.json"),
            "{\"dependencies\":{\"com.unity.modules.ai\":\"1.0.0\"}}");
        return projectDir;
    }

    [Fact]
    public async Task InfoCommand_WithValidProject_ReturnsZeroExitCode()
    {
        var projectDir = CreateUnityProject();
        var detector = new ProjectDetector();
        var app = new CommandApp<InfoCommand>();
        app.Configure(config =>
        {
            config.Settings.Registrator = new InfoCommandRegistratior(projectDir);
        });

        var result = await app.RunAsync(new[] { "--project", projectDir });

        result.Should().Be(0);
    }
}
```

注意：InfoCommand 的测试会根据 Spectre.Console.Cli 的实际 API 调整。如果 CommandApp 测试过于复杂，可以退而测试 Command 内部调用的 Service 逻辑（已在 Task 2 覆盖），Command 层做轻量集成测试即可。

- [ ] **Step 2: 运行测试，确认失败**

```bash
cd E:/Work/joker-unity-cli/src
dotnet test Joker.UnityCli.Tests/Joker.UnityCli.Tests.csproj --filter "FullyQualifiedName~InfoCommandTests" -v n
```

Expected: 编译失败

- [ ] **Step 3: GREEN — 写 Commands 和 Program.cs**

```csharp
// src/Joker.UnityCli/Commands/InfoCommand.cs
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class InfoCommand : Command<InfoCommand.Settings>
{
    private readonly IProjectDetector _detector;

    public InfoCommand(IProjectDetector detector)
    {
        _detector = detector;
    }

    public class Settings : CommandSettings
    {
        [CommandOption("-p|--project")]
        public string? ProjectPath { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var path = settings.ProjectPath ?? Directory.GetCurrentDirectory();
        var project = _detector.Detect(path) ?? _detector.DetectFromCurrentDirectory(path);

        if (project == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No Unity project found at '{0}'", path);
            return 1;
        }

        var table = new Table();
        table.AddColumn("Property");
        table.AddColumn("Value");
        table.AddRow("Name", project.Name);
        table.AddRow("Path", project.Path);
        table.AddRow("Unity Version", project.UnityVersion);
        table.AddRow("Packages", string.Join(", ", project.PackageDependencies));

        AnsiConsole.Write(table);
        return 0;
    }
}
```

```csharp
// src/Joker.UnityCli/Commands/BuildCommand.cs
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class BuildCommand : Command<BuildCommand.Settings>
{
    private readonly IBuildService _buildService;
    private readonly IProjectDetector _detector;
    private readonly IUnityLocator _locator;

    public BuildCommand(IBuildService buildService, IProjectDetector detector, IUnityLocator locator)
    {
        _buildService = buildService;
        _detector = detector;
        _locator = locator;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<PLATFORM>")]
        public string Platform { get; set; } = "";

        [CommandOption("-p|--project")]
        public string? ProjectPath { get; set; }

        [CommandOption("-u|--unity")]
        public string? UnityPath { get; set; }

        [CommandOption("-o|--output")]
        public string? OutputPath { get; set; }

        [CommandOption("-s|--scenes")]
        public string[]? Scenes { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var path = settings.ProjectPath ?? Directory.GetCurrentDirectory();
        var project = _detector.Detect(path) ?? _detector.DetectFromCurrentDirectory(path);

        if (project == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No Unity project found");
            return 1;
        }

        var unity = string.IsNullOrEmpty(settings.UnityPath)
            ? _locator.Locate()
            : _locator.Locate(settings.UnityPath);

        if (unity == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Unity installation not found");
            return 1;
        }

        AnsiConsole.MarkupLine("Building [blue]{0}[/] for [green]{1}[/]...", project.Name, settings.Platform);

        var outputPath = settings.OutputPath ?? Path.Combine(project.Path, "Build", settings.Platform);
        var result = _buildService.BuildAsync(
            project.Path,
            unity.Path,
            settings.Platform,
            "Joker.UnityCli.Editor.BuildPipeline.Build",
            outputPath,
            settings.Scenes
        ).GetAwaiter().GetResult();

        if (result.Success)
        {
            AnsiConsole.MarkupLine("[green]Build succeeded[/] in {0}", result.Duration);
            return 0;
        }

        AnsiConsole.MarkupLine("[red]Build failed[/]. Log: {0}", result.LogPath);
        return 1;
    }
}
```

```csharp
// src/Joker.UnityCli/Commands/AssetsCommand.cs
using Joker.UnityCli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class AssetsCommand : Command<AssetsCommand.Settings>
{
    private readonly IAssetService _assetService;
    private readonly IProjectDetector _detector;

    public AssetsCommand(IAssetService assetService, IProjectDetector detector)
    {
        _assetService = assetService;
        _detector = detector;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[QUERY]")]
        public string? Query { get; set; }

        [CommandOption("-p|--project")]
        public string? ProjectPath { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var path = settings.ProjectPath ?? Directory.GetCurrentDirectory();
        var project = _detector.Detect(path) ?? _detector.DetectFromCurrentDirectory(path);

        if (project == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No Unity project found");
            return 1;
        }

        var assetsPath = System.IO.Path.Combine(project.Path, "Assets");
        var assets = string.IsNullOrEmpty(settings.Query)
            ? _assetService.ListAssets(assetsPath)
            : _assetService.SearchAssets(assetsPath, settings.Query);

        var table = new Table();
        table.AddColumn("Path");
        table.AddColumn("GUID");
        table.AddColumn("Type");

        foreach (var asset in assets)
        {
            table.AddRow(asset.RelativePath, asset.Guid, asset.Extension);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("Total: {0} assets", assets.Count());
        return 0;
    }
}
```

```csharp
// src/Joker.UnityCli/Program.cs
using Joker.UnityCli.Commands;
using Joker.UnityCli.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Joker.UnityCli;

class Program
{
    static int Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProjectDetector, ProjectDetector>();
        services.AddSingleton<IUnityLocator, UnityLocator>();
        services.AddSingleton<IAssetService, AssetService>();
        services.AddSingleton<IBuildService, BuildService>();

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("joker-cli");
            config.AddCommand<InfoCommand>("info");
            config.AddCommand<BuildCommand>("build");
            config.AddCommand<AssetsCommand>("assets");
        });

        return app.Run(args);
    }
}
```

注意：Spectre.Console.Cli 的 `TypeRegistrar` 需要实现 `ITypeRegistrar` 接口。需要在项目中添加 `Spectre.Console.Cli.Extensions.DependencyInjection` 包或自行实现。实际代码可能需要根据 Spectre 版本调整。

- [ ] **Step 4: 运行所有测试，确认通过**

```bash
cd E:/Work/joker-unity-cli/src
dotnet test -v n
```

Expected: 所有测试通过

- [ ] **Step 5: 验证 CLI 可运行**

```bash
cd E:/Work/joker-unity-cli/src/Joker.UnityCli
dotnet run -- --help
```

Expected: 显示帮助信息，列出 info、build、assets 命令

- [ ] **Step 6: 提交**

```bash
cd E:/Work/joker-unity-cli
git add src/Joker.UnityCli/Commands/ src/Joker.UnityCli/Program.cs src/Joker.UnityCli.Tests/Commands/
git commit -m "feat: add CLI commands and entry point - info, build, assets"
```

---

### Task 7: 更新架构文档

**Files:**
- Modify: `docs/architecture.md`

- [ ] **Step 1: 更新架构文档，反映实际代码结构**

将 `docs/architecture.md` 中的"待实现后更新"部分替换为实际的模块信息。

- [ ] **Step 2: 提交**

```bash
cd E:/Work/joker-unity-cli
git add docs/architecture.md
git commit -m "docs: update architecture with implemented modules"
```

---

## 验证清单

完成所有任务后：

```bash
# 1. 所有测试通过
cd E:/Work/joker-unity-cli/src
dotnet test -v n

# 2. CLI 可执行
cd Joker.UnityCli
dotnet run -- --help
dotnet run -- info --project ../../.Unity2019

# 3. 代码无警告
dotnet build -warnaserror
```
