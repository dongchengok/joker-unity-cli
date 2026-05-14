using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;

namespace Joker.UnityCli.Tests.Unit.Parsing;

public class CompileLogParserTests : IDisposable
{
    private readonly string _tempDir;

    public CompileLogParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"JokerCompileLogTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ParseLogForErrors_WithErrors_ReturnsErrorMessages()
    {
        var logFile = Path.Combine(_tempDir, "build.log");
        var logContent = """
            Some log line
            Assets/Scripts/Player.cs(10,5): error CS0234: The name 'foo' does not exist
            Another line
            Assets/Scripts/Game.cs(30,1): error CS1002: ; expected
            """;
        File.WriteAllText(logFile, logContent);

        var errors = CompileService.ParseLogForErrors(logFile);

        errors.Should().HaveCount(2);
        errors[0].Should().Contain("CS0234");
        errors[0].Should().Contain("The name 'foo' does not exist");
        errors[1].Should().Contain("CS1002");
    }

    [Fact]
    public void ParseLogForErrors_WarningsNotIncluded()
    {
        var logFile = Path.Combine(_tempDir, "build_log.log");
        var logContent = """
            Assets/Scripts/Player.cs(10,5): warning CS0162: Unreachable code detected
            """;
        File.WriteAllText(logFile, logContent);

        var errors = CompileService.ParseLogForErrors(logFile);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ParseLogForErrors_NoErrors_ReturnsEmptyList()
    {
        var logFile = Path.Combine(_tempDir, "clean.log");
        File.WriteAllText(logFile, "Compilation succeeded\nNo errors\n");

        var errors = CompileService.ParseLogForErrors(logFile);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ParseLogForErrors_NonexistentFile_ReturnsEmptyList()
    {
        var errors = CompileService.ParseLogForErrors(Path.Combine(_tempDir, "nonexistent.log"));

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ParseLogForErrors_MultilineMessage_CapturesFullMessage()
    {
        var logFile = Path.Combine(_tempDir, "multiline.log");
        var logContent = """
            Assets/Scripts/Player.cs(10,5): error CS0234: The name 'foo' does not exist
            Some unrelated line
            Assets/Scripts/Game.cs(30,1): error CS1002: ; expected
            """;
        File.WriteAllText(logFile, logContent);

        var errors = CompileService.ParseLogForErrors(logFile);

        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e.Contains("CS0234") && e.Contains("The name 'foo' does not exist"));
        errors.Should().Contain(e => e.Contains("CS1002") && e.Contains("; expected"));
    }

    [Fact]
    public void ParseLogForErrors_NonErrorLine_DoesNotMatch()
    {
        var logFile = Path.Combine(_tempDir, "noerror.log");
        File.WriteAllText(logFile, "Some random log line\nAnother random line\n");

        var errors = CompileService.ParseLogForErrors(logFile);

        errors.Should().BeEmpty();
    }
}
