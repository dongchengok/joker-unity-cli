using System.Text.Json;
using Xunit;

namespace Joker.UnityCli.Tests.Integration;

public abstract class UnityIntegrationTestBase : IDisposable
{
    protected string ProjectPath { get; }
    protected int? ServerPort { get; }
    protected bool IsUnityRunning => ServerPort != null;

    protected UnityIntegrationTestBase()
    {
        ProjectPath = Path.GetFullPath(
            Path.Combine("..", "..", "..", "..", "..", ".Unity2019"));
        ServerPort = TryReadServerPort(ProjectPath);
    }

    protected void SkipIfUnityNotRunning()
    {
        if (!IsUnityRunning)
            Skip.If(true, "Unity Editor is not running with .Unity2019 project");
    }

    private static int? TryReadServerPort(string projectPath)
    {
        var portFile = Path.Combine(projectPath, ".joker-unity", "server.json");
        if (!File.Exists(portFile))
            return null;

        try
        {
            var json = File.ReadAllText(portFile);
            var info = JsonSerializer.Deserialize<ServerInfo>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return info?.Port;
        }
        catch
        {
            return null;
        }
    }

    private class ServerInfo
    {
        public int Port { get; set; }
    }

    public virtual void Dispose() { }
}
