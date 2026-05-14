using Joker.UnityCli.Services;
using Xunit;

namespace Joker.UnityCli.Tests.Integration;

public abstract class UnityIntegrationTestBase : IDisposable
{
    protected string ProjectPath { get; }

    protected int? ServerPort => CompileService.TryReadServerPort(ProjectPath);

    protected bool IsUnityRunning => ServerPort != null;

    protected UnityIntegrationTestBase()
    {
        ProjectPath = Path.GetFullPath(
            Path.Combine("..", "..", "..", "..", "..", ".Unity2019"));
    }

    protected void SkipIfUnityNotRunning()
    {
        if (!IsUnityRunning)
            Skip.If(true, "Unity Editor is not running with .Unity2019 project");
    }

    protected async Task WaitForServerReady(int timeoutMs = 30000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (IsUnityRunning)
            {
                try
                {
                    var exec = new ExecService();
                    var result = await exec.ExecuteAsync(ProjectPath, "1", "script", 5000, CancellationToken.None);
                    if (result.Success) return;
                }
                catch { }
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"Unity server not ready after {timeoutMs}ms");
    }

    public virtual void Dispose() { }
}
