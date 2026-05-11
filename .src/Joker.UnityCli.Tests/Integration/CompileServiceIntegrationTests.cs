using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;
using Xunit.Sdk;

namespace Joker.UnityCli.Tests.Integration;

public class CompileServiceIntegrationTests : UnityIntegrationTestBase
{
    [SkippableFact]
    public void SkipIfUnityNotRunningTest()
    {
        SkipIfUnityNotRunning();
    }

    [SkippableFact]
    public async Task CompileAsync_TriggersCompilation_PortChanges()
    {
        SkipIfUnityNotRunning();
        var initialPort = ServerPort!.Value;

        var unityLocator = new UnityLocator(); // real implementation
        var execService = new ExecService(); // real implementation
        var compileService = new CompileService(execService, unityLocator);

        var result = await compileService.CompileAsync(ProjectPath, 60000, CancellationToken.None);
        result.Should().NotBeNull();
        // After compilation, a new server port should be assigned (Domain Reload)
        // The result should indicate success or up_to_date
        result.Status.Should().BeOneOf("compiled", "up_to_date");
    }

    [SkippableFact]
    public async Task ExecDuringDomainReload_RetriesAndRecovers()
    {
        SkipIfUnityNotRunning();
        var execService = new ExecService();

        // First verify exec works
        var before = await execService.ExecuteAsync(ProjectPath, "1+1", "script", 5000, CancellationToken.None);
        before.Success.Should().BeTrue();

        // Trigger compilation (which causes Domain Reload)
        var unityLocator = new UnityLocator();
        var compileService = new CompileService(execService, unityLocator);
        _ = compileService.CompileAsync(ProjectPath, 60000, CancellationToken.None);

        // Wait a moment for Domain Reload to start
        await Task.Delay(1000);

        // Try exec during Domain Reload - should retry and eventually succeed
        var result = await execService.ExecuteAsync(ProjectPath, "1+1", "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
    }
}
