using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class LogServiceTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public LogServiceTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public void LogEntry_Serializes_To_CamelCase_Json()
    {
        var entry = new LogEntry
        {
            FilePath = "Assets/Scripts/Player.cs",
            Line = 10,
            Column = 5,
            Severity = "error",
            Code = "CS0234",
            Message = "The name 'foo' does not exist"
        };

        var json = JsonSerializer.Serialize(entry, _jsonOptions);
        json.Should().Contain("\"filePath\":\"Assets/Scripts/Player.cs\"");
        json.Should().Contain("\"line\":10");
        json.Should().Contain("\"column\":5");
        json.Should().Contain("\"severity\":\"error\"");
        json.Should().Contain("\"code\":\"CS0234\"");
        json.Should().Contain("\"message\":\"The name \\u0027foo\\u0027 does not exist\"");
    }

    [Fact]
    public void LogEntry_Defaults()
    {
        var entry = new LogEntry();

        entry.FilePath.Should().Be("");
        entry.Line.Should().Be(0);
        entry.Column.Should().Be(0);
        entry.Severity.Should().Be("");
        entry.Code.Should().Be("");
        entry.Message.Should().Be("");
    }

    [Fact]
    public async Task GetLogEntriesAsync_FileNotFound_ReturnsEmptyList()
    {
        var tempFile = Path.GetTempFileName();
        File.Delete(tempFile);

        try
        {
            var service = new LogService(tempFile);
            var result = await service.GetLogEntriesAsync(100, false, null, CancellationToken.None);

            result.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetLogEntriesAsync_EmptyFile_ReturnsEmptyList()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var service = new LogService(tempFile);
            var result = await service.GetLogEntriesAsync(100, false, null, CancellationToken.None);

            result.Should().BeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetLogEntriesAsync_WithErrors_ReturnsMatchedEntries()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var logContent = """
                Some random log line
                Assets/Scripts/Player.cs(10,5): error CS0234: The name 'foo' does not exist in the current context
                Another random line
                Assets/Scripts/Enemy.cs(20,8): warning CS0162: Unreachable code detected
                Assets/Scripts/Game.cs(30,1): error CS1002: ; expected
                """;
            await File.WriteAllTextAsync(tempFile, logContent);

            var service = new LogService(tempFile);
            var result = await service.GetLogEntriesAsync(100, false, null, CancellationToken.None);

            result.Should().HaveCount(3);

            result[0].FilePath.Should().Be("Assets/Scripts/Player.cs");
            result[0].Line.Should().Be(10);
            result[0].Column.Should().Be(5);
            result[0].Severity.Should().Be("error");
            result[0].Code.Should().Be("CS0234");
            result[0].Message.Should().Be("The name 'foo' does not exist in the current context");

            result[1].FilePath.Should().Be("Assets/Scripts/Enemy.cs");
            result[1].Line.Should().Be(20);
            result[1].Column.Should().Be(8);
            result[1].Severity.Should().Be("warning");
            result[1].Code.Should().Be("CS0162");
            result[1].Message.Should().Be("Unreachable code detected");

            result[2].FilePath.Should().Be("Assets/Scripts/Game.cs");
            result[2].Line.Should().Be(30);
            result[2].Column.Should().Be(1);
            result[2].Severity.Should().Be("error");
            result[2].Code.Should().Be("CS1002");
            result[2].Message.Should().Be("; expected");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetLogEntriesAsync_ErrorsOnly_FiltersWarnings()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var logContent = """
                Assets/Scripts/Player.cs(10,5): error CS0234: The name 'foo' does not exist
                Assets/Scripts/Enemy.cs(20,8): warning CS0162: Unreachable code detected
                Assets/Scripts/Game.cs(30,1): error CS1002: ; expected
                """;
            await File.WriteAllTextAsync(tempFile, logContent);

            var service = new LogService(tempFile);
            var result = await service.GetLogEntriesAsync(100, true, null, CancellationToken.None);

            result.Should().HaveCount(2);
            result.Should().OnlyContain(e => e.Severity == "error");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetLogEntriesAsync_TailCount_LimitsResults()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var logContent = """
                Assets/Scripts/A.cs(1,1): error CS0001: Error A
                Assets/Scripts/B.cs(2,2): error CS0002: Error B
                Assets/Scripts/C.cs(3,3): error CS0003: Error C
                Assets/Scripts/D.cs(4,4): error CS0004: Error D
                Assets/Scripts/E.cs(5,5): error CS0005: Error E
                """;
            await File.WriteAllTextAsync(tempFile, logContent);

            var service = new LogService(tempFile);
            var result = await service.GetLogEntriesAsync(3, false, null, CancellationToken.None);

            result.Should().HaveCount(3);
            result[0].Code.Should().Be("CS0003");
            result[1].Code.Should().Be("CS0004");
            result[2].Code.Should().Be("CS0005");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetLogEntriesAsync_DuplicateEntries_Deduplicates()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var logContent = """
                Assets/Scripts/Player.cs(10,5): error CS0234: The name 'foo' does not exist
                Some other log line
                Assets/Scripts/Player.cs(10,5): error CS0234: The name 'foo' does not exist
                """;
            await File.WriteAllTextAsync(tempFile, logContent);

            var service = new LogService(tempFile);
            var result = await service.GetLogEntriesAsync(100, false, null, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].Code.Should().Be("CS0234");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetLogEntriesAsync_ProjectPath_FiltersByProject()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var logContent = """
                Assets/Scripts/Player.cs(10,5): error CS0234: Error in Player
                C:/Projects/MyGame/Assets/Scripts/Enemy.cs(20,8): error CS0162: Error in Enemy
                C:/Projects/OtherGame/Assets/Scripts/NPC.cs(30,1): error CS1002: Error in NPC
                """;
            await File.WriteAllTextAsync(tempFile, logContent);

            var service = new LogService(tempFile);
            var result = await service.GetLogEntriesAsync(100, false, "C:/Projects/MyGame", CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].FilePath.Should().Be("C:/Projects/MyGame/Assets/Scripts/Enemy.cs");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetLogEntriesAsync_NoProjectPath_ReturnsAll()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var logContent = """
                Assets/Scripts/Player.cs(10,5): error CS0234: Error A
                C:/Projects/MyGame/Assets/Scripts/Enemy.cs(20,8): warning CS0162: Error B
                """;
            await File.WriteAllTextAsync(tempFile, logContent);

            var service = new LogService(tempFile);
            var result = await service.GetLogEntriesAsync(100, false, null, CancellationToken.None);

            result.Should().HaveCount(2);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
