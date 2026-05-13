using System.Text.Json;
using Joker.UnityCli.Services;
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
        ServerPort = CompileService.TryReadServerPort(ProjectPath);
    }

    protected void SkipIfUnityNotRunning()
    {
        if (!IsUnityRunning)
            Skip.If(true, "Unity Editor is not running with .Unity2019 project");
    }

    public virtual void Dispose() { }
}
