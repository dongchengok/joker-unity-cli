using System.IO;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Joker.UnityCli.Services;
using Joker.UnityCli.Tests.Infrastructure;
using Xunit;

namespace Joker.UnityCli.Tests.Regression;

/// <summary>
/// Regression tests for Bug #4: HttpExecHandler had many silent failure paths
/// with no diagnostics. Fixed by adding error logging in ExecService.
/// </summary>
public class Bug4_MissingErrorLoggingTests
{
    [Fact]
    public async Task ExecService_ServerReturnsEmptyBody_ThrowsIOException()
    {
        using var server = new MockHttpServer();
        server.StartWithResponse(200, "");

        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(server.Port);

        var service = new ExecService();
        var act = async () => await service.ExecuteAsync(
            fixture.ProjectPath, "1+1", "script", 3000, CancellationToken.None);

        await act.Should().ThrowAsync<IOException>()
            .WithMessage("*Failed to deserialize*");
    }

    [Fact]
    public async Task ExecService_ServerReturnsMalformedJson_ThrowsIOException()
    {
        using var server = new MockHttpServer();
        server.StartWithResponse(200, "<<<not json>>>");

        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(server.Port);

        var service = new ExecService();
        var act = async () => await service.ExecuteAsync(
            fixture.ProjectPath, "1+1", "script", 3000, CancellationToken.None);

        await act.Should().ThrowAsync<IOException>()
            .WithMessage("*Failed to deserialize*");
    }

    [Fact]
    public async Task ExecService_ServerReturnsNon200_ThrowsHttpRequestException()
    {
        // Use raw HttpListener to handle multiple 500 responses across retries.
        // MockHttpServer only handles a single request, which causes TaskCanceledException
        // on subsequent retry attempts instead of the expected HttpRequestException.
        var port = PortHelper.FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(port);

        // Server returns 500 on every request - retries will exhaust the timeout
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { break; }
            }
        });

        try
        {
            var service = new ExecService();
            var act = async () => await service.ExecuteAsync(
                fixture.ProjectPath, "1+1", "script", 3000, CancellationToken.None);
            await act.Should().ThrowAsync<HttpRequestException>();
        }
        finally
        {
            try { listener.Stop(); } catch { }
        }
    }
}
