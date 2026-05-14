using System.Text.Json;

namespace Joker.UnityCli.Tests.Infrastructure;

public sealed class TempProjectFixture : IDisposable
{
    public string ProjectPath { get; }

    public TempProjectFixture()
    {
        ProjectPath = Path.Combine(Path.GetTempPath(), $"joker-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ProjectPath);
    }

    public void WriteServerJson(int port, int pid = 9999, string status = "ready")
    {
        var jokerDir = Path.Combine(ProjectPath, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        File.WriteAllText(
            Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid, status }));
    }

    public void DeleteServerJson()
    {
        var path = Path.Combine(ProjectPath, ".joker-unity", "server.json");
        if (File.Exists(path))
            File.Delete(path);
    }

    public void Dispose()
    {
        try { Directory.Delete(ProjectPath, true); } catch { }
    }
}
