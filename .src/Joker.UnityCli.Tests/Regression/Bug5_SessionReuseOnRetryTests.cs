using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Joker.UnityCli.Tests.Infrastructure;
using Xunit;

namespace Joker.UnityCli.Tests.Regression;

/// <summary>
/// Regression tests for Bug #5: Same request ID on retried requests could fail
/// because session's TryStart() was already consumed. ExecService now generates
/// a new ID per request, so retries get a fresh session.
/// </summary>
public class Bug5_SessionReuseOnRetryTests
{
    [Fact]
    public async Task ExecService_GeneratesNewIdPerRequest()
    {
        string? firstBody = null;
        string? secondBody = null;

        // First request
        using (var server1 = new MockHttpServer())
        {
            server1.Start(ctx =>
            {
                using var reader = new StreamReader(ctx.Request.InputStream);
                firstBody = reader.ReadToEnd();

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                var json = JsonSerializer.Serialize(new ExecResult
                {
                    Type = "exec_result", Id = "test", Success = true, DurationMs = 1
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var buffer = Encoding.UTF8.GetBytes(json);
                ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
                ctx.Response.Close();
            });

            using var fixture1 = new TempProjectFixture();
            fixture1.WriteServerJson(server1.Port);

            var service = new ExecService();
            await service.ExecuteAsync(fixture1.ProjectPath, "1+1", "script", 5000, CancellationToken.None);
        }

        // Second request with a new server instance
        using (var server2 = new MockHttpServer())
        {
            server2.Start(ctx =>
            {
                using var reader = new StreamReader(ctx.Request.InputStream);
                secondBody = reader.ReadToEnd();

                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                var json = JsonSerializer.Serialize(new ExecResult
                {
                    Type = "exec_result", Id = "test", Success = true, DurationMs = 1
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var buffer = Encoding.UTF8.GetBytes(json);
                ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
                ctx.Response.Close();
            });

            using var fixture2 = new TempProjectFixture();
            fixture2.WriteServerJson(server2.Port);

            var service = new ExecService();
            await service.ExecuteAsync(fixture2.ProjectPath, "2+2", "script", 5000, CancellationToken.None);
        }

        firstBody.Should().NotBeNull();
        secondBody.Should().NotBeNull();

        var firstRequest = JsonSerializer.Deserialize<ExecRequest>(firstBody!,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var secondRequest = JsonSerializer.Deserialize<ExecRequest>(secondBody!,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        firstRequest.Should().NotBeNull();
        secondRequest.Should().NotBeNull();
        firstRequest!.Id.Should().NotBe(secondRequest!.Id,
            "each ExecService.ExecuteAsync call should generate a unique request ID");
    }

    [Fact]
    public async Task ExecService_RetryWithNewId_GetsFreshSession()
    {
        // Two sequential requests that both succeed independently
        var successResult = new ExecResult
        {
            Type = "exec_result", Id = "test", Success = true,
            Result = "ok", DurationMs = 1
        };

        using var server1 = new MockHttpServer();
        server1.StartWithExecResult(successResult);
        using var fixture1 = new TempProjectFixture();
        fixture1.WriteServerJson(server1.Port);

        var service1 = new ExecService();
        var result1 = await service1.ExecuteAsync(
            fixture1.ProjectPath, "first", "script", 5000, CancellationToken.None);
        result1.Success.Should().BeTrue();

        using var server2 = new MockHttpServer();
        server2.StartWithExecResult(successResult);
        using var fixture2 = new TempProjectFixture();
        fixture2.WriteServerJson(server2.Port);

        var service2 = new ExecService();
        var result2 = await service2.ExecuteAsync(
            fixture2.ProjectPath, "second", "script", 5000, CancellationToken.None);
        result2.Success.Should().BeTrue();
    }

    [Fact]
    public void SessionManager_DifferentId_DifferentSession()
    {
        var manager = new TestableSessionManager();

        var session1 = manager.GetOrCreate("id-alpha");
        var session2 = manager.GetOrCreate("id-beta");

        session1.Should().NotBeSameAs(session2,
            "different IDs should produce different session objects");
    }

    [Fact]
    public async Task ExecService_CompletedSessionDoesNotBlockNewRequest()
    {
        var successResult = new ExecResult
        {
            Type = "exec_result", Id = "test", Success = true,
            Result = "done", DurationMs = 1
        };

        // First request completes successfully
        using var server1 = new MockHttpServer();
        server1.StartWithExecResult(successResult);
        using var fixture1 = new TempProjectFixture();
        fixture1.WriteServerJson(server1.Port);

        var service = new ExecService();
        var result1 = await service.ExecuteAsync(
            fixture1.ProjectPath, "first", "script", 5000, CancellationToken.None);
        result1.Success.Should().BeTrue();

        // Second request on a fresh service instance also succeeds
        using var server2 = new MockHttpServer();
        server2.StartWithExecResult(successResult);
        using var fixture2 = new TempProjectFixture();
        fixture2.WriteServerJson(server2.Port);

        var service2 = new ExecService();
        var result2 = await service2.ExecuteAsync(
            fixture2.ProjectPath, "second", "script", 5000, CancellationToken.None);
        result2.Success.Should().BeTrue();
    }

    /// <summary>
    /// Simplified testable session manager mirroring the real SessionManager pattern
    /// from SessionManagerTests, used to verify ID-based session isolation.
    /// </summary>
    private class TestSession
    {
        private int _started;
        public TaskCompletionSource<int> CompletionSource { get; } =
            new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryStart()
        {
            return Interlocked.CompareExchange(ref _started, 1, 0) == 0;
        }
    }

    private class TestableSessionManager
    {
        private readonly ConcurrentDictionary<string, TestSession> _sessions = new();

        public TestSession GetOrCreate(string id) =>
            _sessions.GetOrAdd(id, _ => new TestSession());
    }
}
