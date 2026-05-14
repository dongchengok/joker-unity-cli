using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using FluentAssertions;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Joker.UnityCli.Tests.Infrastructure;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class ExecServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WithMockHttpServer_ReturnsResult()
    {
        var port = PortHelper.FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var responseJson = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result", Id = "test", Success = true,
                Result = "42", DurationMs = 5
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        });

        var service = new ExecService();
        var result = await service.ExecuteAsync(tempDir, "6*7", "script", 5000, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Result.Should().Be("42");

        listener.Stop();
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task ExecuteAsync_UserCancellation_ThrowsOperationCanceledException()
    {
        var port = PortHelper.FindAvailablePort();
        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));

        // Start a server that never responds
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        _ = Task.Run(async () =>
        {
            try
            {
                // Accept connection but never send response
                var context = await listener.GetContextAsync();
                await Task.Delay(30000, CancellationToken.None);
                context.Response.Close();
            }
            catch { }
        });

        try
        {
            using var cts = new CancellationTokenSource();
            var service = new ExecService();
            var task = service.ExecuteAsync(tempDir, "code", "script", 30000, cts.Token);

            // Cancel after a short delay
            cts.CancelAfter(500);

            var act = async () => await task;
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_SendsCorrectMode()
    {
        var port = PortHelper.FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));

        string? receivedBody = null;
        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            using var reader = new StreamReader(context.Request.InputStream);
            receivedBody = await reader.ReadToEndAsync();

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var responseJson = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result", Id = "test", Success = true, DurationMs = 1
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        });

        var service = new ExecService();
        await service.ExecuteAsync(tempDir, "code", "compile", 5000, CancellationToken.None);

        receivedBody.Should().NotBeNull();
        var request = JsonSerializer.Deserialize<ExecRequest>(receivedBody!, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        request!.Mode.Should().Be("compile");

        listener.Stop();
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesConnection_WhenServerBrieflyUnavailable()
    {
        var port = PortHelper.FindAvailablePort();
        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");

        // Start the server AFTER a brief delay so first connection attempt fails
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            listener.Start();

            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var responseJson = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result", Id = "test", Success = true,
                Result = "recovered", DurationMs = 5
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        });

        try
        {
            var service = new ExecService();
            var result = await service.ExecuteAsync(tempDir, "code", "script", 10000, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Result.Should().Be("recovered");
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_Http500_ThrowsHttpRequestException()
    {
        var port = PortHelper.FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));

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
            var act = async () => await service.ExecuteAsync(tempDir, "code", "script", 3000, CancellationToken.None);
            await act.Should().ThrowAsync<HttpRequestException>();
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ServerSlowResponse_ThrowsOperationCanceledException()
    {
        var port = PortHelper.FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));

        // Server accepts request but delays response beyond client timeout
        _ = Task.Run(async () =>
        {
            try
            {
                var context = await listener.GetContextAsync();
                await Task.Delay(10000); // 10s delay, much longer than 3s timeout
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                var responseJson = JsonSerializer.Serialize(new ExecResult
                {
                    Type = "exec_result", Id = "test", Success = true, DurationMs = 10000
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
                await context.Response.OutputStream.WriteAsync(buffer);
                context.Response.Close();
            }
            catch { }
        });

        try
        {
            var service = new ExecService();
            var act = async () => await service.ExecuteAsync(tempDir, "code", "script", 3000, CancellationToken.None);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJsonResponse_ThrowsIOException()
    {
        var port = PortHelper.FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));

        // Server returns 200 with non-JSON body
        _ = Task.Run(async () =>
        {
            try
            {
                var context = await listener.GetContextAsync();
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                var buffer = System.Text.Encoding.UTF8.GetBytes("not json at all");
                await context.Response.OutputStream.WriteAsync(buffer);
                context.Response.Close();
            }
            catch { }
        });

        try
        {
            var service = new ExecService();
            var act = async () => await service.ExecuteAsync(tempDir, "code", "script", 5000, CancellationToken.None);
            await act.Should().ThrowAsync<IOException>()
                .WithMessage("*Failed to deserialize*");
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_PortFileMissing_ReturnsServerNotFound()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        // No server.json created

        try
        {
            var service = new ExecService();
            var result = await service.ExecuteAsync(tempDir, "code", "script", 5000, CancellationToken.None);

            result.Success.Should().BeFalse();
            result.ErrorCode.Should().Be("server_not_found");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_SendsCorrectRequestIdFormat()
    {
        var port = PortHelper.FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));

        string? receivedBody = null;
        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            using var reader = new StreamReader(context.Request.InputStream);
            receivedBody = await reader.ReadToEndAsync();

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var responseJson = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result", Id = "test", Success = true, DurationMs = 1
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        });

        var service = new ExecService();
        await service.ExecuteAsync(tempDir, "1+1", "script", 5000, CancellationToken.None);

        receivedBody.Should().NotBeNull();
        var request = JsonSerializer.Deserialize<ExecRequest>(receivedBody!, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        request!.Id.Should().NotBeNullOrEmpty();
        request.Id.Should().HaveLength(8);
        // Verify all characters are valid hex
        request.Id.Should().MatchRegex("^[0-9a-f]{8}$");

        listener.Stop();
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task ExecuteAsync_SendsContentTypeApplicationJson()
    {
        var port = PortHelper.FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));

        string? contentType = null;
        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            contentType = context.Request.ContentType;

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var responseJson = JsonSerializer.Serialize(new ExecResult
            {
                Type = "exec_result", Id = "test", Success = true, DurationMs = 1
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        });

        var service = new ExecService();
        await service.ExecuteAsync(tempDir, "1+1", "script", 5000, CancellationToken.None);

        contentType.Should().NotBeNull();
        contentType.Should().Be("application/json; charset=utf-8");

        listener.Stop();
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task ExecuteAsync_RetryExponentialBackoff()
    {
        var port = PortHelper.FindAvailablePort();
        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        int requestCount = 0;
        _ = Task.Run(async () =>
        {
            try
            {
                // First request: return 500 to trigger retry
                var context1 = await listener.GetContextAsync();
                Interlocked.Increment(ref requestCount);
                context1.Response.StatusCode = 500;
                context1.Response.Close();

                // Second request: return success
                var context2 = await listener.GetContextAsync();
                Interlocked.Increment(ref requestCount);
                context2.Response.StatusCode = 200;
                context2.Response.ContentType = "application/json";
                var responseJson = JsonSerializer.Serialize(new ExecResult
                {
                    Type = "exec_result", Id = "test", Success = true,
                    Result = "retried", DurationMs = 5
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
                await context2.Response.OutputStream.WriteAsync(buffer);
                context2.Response.Close();
            }
            catch { }
        });

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var service = new ExecService();
            var result = await service.ExecuteAsync(tempDir, "code", "script", 15000, CancellationToken.None);
            sw.Stop();

            result.Success.Should().BeTrue();
            requestCount.Should().Be(2);
            // First retry delay is 1 second, so total time should be at least ~1 second
            sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ConnectionRefusedWithinTimeout_RetriesAndSucceeds()
    {
        var port = PortHelper.FindAvailablePort();
        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");

        // Start the server AFTER a delay so initial connection attempts fail
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            listener.Start();

            try
            {
                var context = await listener.GetContextAsync();
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                var responseJson = JsonSerializer.Serialize(new ExecResult
                {
                    Type = "exec_result", Id = "test", Success = true,
                    Result = "late-start", DurationMs = 5
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
                await context.Response.OutputStream.WriteAsync(buffer);
                context.Response.Close();
            }
            catch { }
        });

        try
        {
            var service = new ExecService();
            var result = await service.ExecuteAsync(tempDir, "code", "script", 15000, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Result.Should().Be("late-start");
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_StatusStopped_ReturnsServerNotFound()
    {
        var port = PortHelper.FindAvailablePort();
        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "stopped" }));

        try
        {
            var service = new ExecService();
            var result = await service.ExecuteAsync(tempDir, "code", "script", 5000, CancellationToken.None);

            result.Success.Should().BeFalse();
            result.ErrorCode.Should().Be("server_not_found");
            result.Error.Should().Contain("stopped");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_StatusCompiling_WaitsAndSucceeds()
    {
        var port = PortHelper.FindAvailablePort();
        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        var serverJsonPath = Path.Combine(jokerDir, "server.json");

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        // Initially compiling
        await File.WriteAllTextAsync(serverJsonPath,
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "compiling" }));

        // After delay, update to ready
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            await File.WriteAllTextAsync(serverJsonPath,
                JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, status = "ready" }));
        });

        // Server responds after ready
        _ = Task.Run(async () =>
        {
            try
            {
                var context = await listener.GetContextAsync();
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                var responseJson = JsonSerializer.Serialize(new ExecResult
                {
                    Type = "exec_result", Id = "test", Success = true,
                    Result = "waited", DurationMs = 5
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
                await context.Response.OutputStream.WriteAsync(buffer);
                context.Response.Close();
            }
            catch { }
        });

        try
        {
            var service = new ExecService();
            var result = await service.ExecuteAsync(tempDir, "code", "script", 15000, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Result.Should().Be("waited");
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
