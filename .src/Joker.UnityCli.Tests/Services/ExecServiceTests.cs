using System.IO;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Joker.UnityCli.Models;
using Joker.UnityCli.Services;
using Xunit;

namespace Joker.UnityCli.Tests.Services;

public class ExecServiceTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public ExecServiceTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public void ExecRequest_Serializes_To_CamelCase_Json()
    {
        var request = new ExecRequest
        {
            Id = "test-id-123",
            Code = "Debug.Log(\"Hello World\");",
            Timeout = 60000,
            Mode = "script"
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        json.Should().Contain("\"type\":\"exec\"");
        json.Should().Contain("\"id\":\"test-id-123\"");
        json.Should().Contain("\"code\":\"Debug.Log(\\u0022Hello World\\u0022);\"");
        json.Should().Contain("\"timeout\":60000");
        json.Should().Contain("\"mode\":\"script\"");
    }

    [Fact]
    public void ExecRequest_Deserializes_From_CamelCase_Json()
    {
        var json = "{\"type\":\"exec\",\"id\":\"test-id-123\",\"code\":\"Debug.Log(\\u0022Hello World\\u0022);\",\"timeout\":60000,\"mode\":\"script\"}";
        var request = JsonSerializer.Deserialize<ExecRequest>(json, _jsonOptions);

        request.Should().NotBeNull();
        request!.Type.Should().Be("exec");
        request.Id.Should().Be("test-id-123");
        request.Code.Should().Be("Debug.Log(\"Hello World\");");
        request.Timeout.Should().Be(60000);
        request.Mode.Should().Be("script");
    }

    [Fact]
    public void ExecResult_Serializes_To_CamelCase_Json()
    {
        var result = new ExecResult
        {
            Id = "test-id-123",
            Success = true,
            Result = "Success",
            Output = "Hello World",
            DurationMs = 1500
        };

        var json = JsonSerializer.Serialize(result, _jsonOptions);
        json.Should().Contain("\"type\":\"exec_result\"");
        json.Should().Contain("\"id\":\"test-id-123\"");
        json.Should().Contain("\"success\":true");
        json.Should().Contain("\"result\":\"Success\"");
        json.Should().Contain("\"output\":\"Hello World\"");
        json.Should().Contain("\"durationMs\":1500");
    }

    [Fact]
    public void ExecResult_Deserializes_Error_Case()
    {
        var json = "{\"type\":\"exec_result\",\"id\":\"test-id-123\",\"success\":false,\"error\":\"Compilation error\",\"durationMs\":1000}";
        var result = JsonSerializer.Deserialize<ExecResult>(json, _jsonOptions);

        result.Should().NotBeNull();
        result!.Type.Should().Be("exec_result");
        result.Id.Should().Be("test-id-123");
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Compilation error");
        result.DurationMs.Should().Be(1000);
        result.Result.Should().BeNull();
        result.Output.Should().BeNull();
    }

    [Fact]
    public void ExecRequest_Default_Mode_Is_Script()
    {
        var request = new ExecRequest();

        request.Mode.Should().Be("script");
    }

    [Fact]
    public void ExecResult_Defaults()
    {
        var result = new ExecResult();

        result.Type.Should().Be("exec_result");
    }

    [Fact]
    public async Task ExecuteAsync_WithMockHttpServer_ReturnsResult()
    {
        var port = FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId }));

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
    public void ReadServerPort_WhenFileMissing_ThrowsFileNotFoundException()
    {
        var act = () => ExecService.ReadServerPort(Path.GetTempPath());
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public async Task ExecuteAsync_SendsCorrectMode()
    {
        var port = FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId }));

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
        var port = FindAvailablePort();
        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId }));

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
        var port = FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId }));

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
        var port = FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId }));

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
        var port = FindAvailablePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        await File.WriteAllTextAsync(Path.Combine(jokerDir, "server.json"),
            JsonSerializer.Serialize(new { port, pid = Environment.ProcessId }));

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
    public async Task ExecuteAsync_PortFileTemporarilyMissing_RetriesAndSucceeds()
    {
        var port = FindAvailablePort();
        var tempDir = Path.Combine(Path.GetTempPath(), "joker-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var jokerDir = Path.Combine(tempDir, ".joker-unity");
        Directory.CreateDirectory(jokerDir);
        var serverJsonPath = Path.Combine(jokerDir, "server.json");

        // Start server first
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        // Server task: handle one request and return result
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
                    Result = "recovered", DurationMs = 5
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
                await context.Response.OutputStream.WriteAsync(buffer);
                context.Response.Close();
            }
            catch { }
        });

        try
        {
            // First attempt: port file missing → FileNotFoundException → retry
            // After delay, create the port file
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                await File.WriteAllTextAsync(serverJsonPath,
                    JsonSerializer.Serialize(new { port, pid = Environment.ProcessId }));
            });

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

    private static int FindAvailablePort()
    {
        var random = new Random();
        for (int i = 0; i < 100; i++)
        {
            var port = random.Next(63000, 63100);
            try
            {
                var testListener = new HttpListener();
                testListener.Prefixes.Add($"http://127.0.0.1:{port}/");
                testListener.Start();
                testListener.Stop();
                return port;
            }
            catch (HttpListenerException) { }
        }
        throw new InvalidOperationException("No available port found for test");
    }
}
