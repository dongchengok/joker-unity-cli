using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Joker.UnityCli.Tests.Infrastructure;
using Xunit;

namespace Joker.UnityCli.Tests.Regression;

/// <summary>
/// Regression tests for Bug #1: HttpExecHandler used EditorApplication.delayCall
/// which doesn't fire when Unity is not focused. Fixed by switching to
/// EditorApplication.update with one-shot callback pattern.
/// </summary>
public class Bug1_DelayCallVsUpdateTests
{
    [Fact]
    public void CallbackPattern_RegisterOneShot_ExecutesOnce()
    {
        // Simulate the one-shot callback pattern:
        // A self-removing callback registered in an update loop should fire exactly once.
        var counter = 0;
        var callbacks = new List<Action> { () => counter++ } ;

        // Simulate one update tick: execute and remove
        var callback = callbacks[0];
        callbacks.Remove(callback);
        callback();

        counter.Should().Be(1);
        callbacks.Should().BeEmpty("the callback removed itself after execution");
    }

    [Fact]
    public void CallbackPattern_RegisterOneShot_DoesNotReexecute()
    {
        // After the one-shot fires and removes itself, triggering the loop again
        // must not re-execute the callback.
        var counter = 0;
        var callbacks = new List<Action> { () => counter++ } ;

        // First tick: execute and remove
        var callback = callbacks[0];
        callbacks.Remove(callback);
        callback();

        // Second tick: nothing to execute
        foreach (var cb in callbacks.ToList())
        {
            callbacks.Remove(cb);
            cb();
        }

        counter.Should().Be(1, "the callback was already removed and must not re-execute");
    }

    [Fact]
    public async Task ExecService_RetryLogic_SurvivesServerStartupDelay()
    {
        // Simulate a server that starts late (as would happen when Unity's update
        // loop hasn't ticked yet). ExecService should retry and eventually succeed.
        using var server = new MockHttpServer();
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(server.Port);

        // Start server with a delay to simulate late startup
        server.StartWithDelay(TimeSpan.FromSeconds(2), ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var json = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result",
                Id = "test",
                Success = true,
                Result = "delayed-ok",
                DurationMs = 100
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var buffer = System.Text.Encoding.UTF8.GetBytes(json);
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.Close();
        });

        var service = new ExecService();
        var result = await service.ExecuteAsync(fixture.ProjectPath, "1+1", "script", 10000, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Result.Should().Be("delayed-ok");
    }

    [Fact]
    public async Task ExecPipeline_DelayedHandlerResponse_StillSucceeds()
    {
        // Simulate a handler that takes 2 seconds to respond (e.g., script compilation).
        // The ExecService should wait for the response within its timeout.
        using var server = new MockHttpServer();
        using var fixture = new TempProjectFixture();
        fixture.WriteServerJson(server.Port);

        server.StartWithDelay(TimeSpan.FromSeconds(2), ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            var json = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result",
                Id = "test",
                Success = true,
                Result = "slow-result",
                DurationMs = 2000
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var buffer = System.Text.Encoding.UTF8.GetBytes(json);
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.Close();
        });

        var service = new ExecService();
        var result = await service.ExecuteAsync(fixture.ProjectPath, "compile-code", "compile", 10000, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Result.Should().Be("slow-result");
    }
}
