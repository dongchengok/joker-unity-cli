using System.IO;
using System.Net.Http;
using System.Text.Json;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class ExecService : IExecService
{
    private static readonly HttpClient SharedClient = new();

    private const int MaxRetries = 10;
    private static readonly TimeSpan CompilingPollInterval = TimeSpan.FromSeconds(1);

    public async Task<ExecResult> ExecuteAsync(string projectPath, string code, string mode, int timeoutMs, CancellationToken ct)
    {
        var request = new ExecRequest
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Code = code,
            Mode = mode,
            Timeout = timeoutMs
        };

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        var retryDelay = TimeSpan.FromSeconds(1);
        var maxRetryDelay = TimeSpan.FromSeconds(5);
        var retryCount = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (retryCount >= MaxRetries)
            {
                return new ExecResult
                {
                    Success = false,
                    ErrorCode = "max_retries_exceeded",
                    Error = $"Exceeded maximum retry count ({MaxRetries})"
                };
            }

            // Read server info - handle file errors as fast-fail
            ServerInfo serverInfo;
            try
            {
                serverInfo = ReadServerInfo(projectPath);
            }
            catch (FileNotFoundException)
            {
                return new ExecResult
                {
                    Success = false,
                    ErrorCode = "server_not_found",
                    Error = "Unity server not running. Open the Unity Editor project first."
                };
            }
            catch (IOException ex)
            {
                return new ExecResult
                {
                    Success = false,
                    ErrorCode = "server_not_found",
                    Error = ex.Message
                };
            }

            // Fast fail: server explicitly stopped
            if (serverInfo.Status == "stopped")
            {
                return new ExecResult
                {
                    Success = false,
                    ErrorCode = "server_not_found",
                    Error = "Unity server is stopped. Open the Unity Editor project first."
                };
            }

            // Wait while server is compiling
            if (serverInfo.Status == "compiling")
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    if (DateTime.UtcNow >= deadline)
                    {
                        return new ExecResult
                        {
                            Success = false,
                            ErrorCode = "max_retries_exceeded",
                            Error = "Timed out waiting for compilation to finish"
                        };
                    }

                    await Task.Delay(CompilingPollInterval, ct);

                    try
                    {
                        var updatedInfo = ReadServerInfo(projectPath);
                        if (updatedInfo.Status != "compiling")
                            break;
                    }
                    catch (FileNotFoundException)
                    {
                        return new ExecResult
                        {
                            Success = false,
                            ErrorCode = "server_not_found",
                            Error = "Unity server not running. Open the Unity Editor project first."
                        };
                    }
                    catch (IOException)
                    {
                        // Transient read error, keep polling
                    }
                }

                // Status changed from compiling, restart loop to re-evaluate
                continue;
            }

            // status == null, "ready", "unknown", or any other value: proceed with request
            try
            {
                var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
                using var cts = new CancellationTokenSource(Math.Max(5000, (int)(deadline - DateTime.UtcNow).TotalMilliseconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

                var response = await SharedClient.PostAsync($"http://127.0.0.1:{serverInfo.Port}/exec", content, linkedCts.Token);

                // Handle 503 Service Unavailable (server compiling, rejects request) -> retry
                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    retryCount++;
                    await Task.Delay(retryDelay, ct);
                    retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, maxRetryDelay.Ticks));
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token);
                try
                {
                    return JsonSerializer.Deserialize<ExecResult>(responseBody, JsonOptions)
                        ?? throw new IOException("Failed to deserialize server response");
                }
                catch (JsonException ex)
                {
                    throw new IOException("Failed to deserialize server response", ex);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                (ex is HttpRequestException
                 || (ex is TaskCanceledException && !ct.IsCancellationRequested))
                && DateTime.UtcNow < deadline)
            {
                retryCount++;
                if (retryCount >= MaxRetries)
                {
                    return new ExecResult
                    {
                        Success = false,
                        ErrorCode = "max_retries_exceeded",
                        Error = $"Exceeded maximum retry count ({MaxRetries})"
                    };
                }

                await Task.Delay(retryDelay, ct);
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, maxRetryDelay.Ticks));
            }
        }
    }

    public static ServerInfo ReadServerInfo(string projectPath)
    {
        var portFile = Path.Combine(projectPath, ".joker-unity", "server.json");
        if (!File.Exists(portFile))
            throw new FileNotFoundException(
                "Unity server not running. Open the Unity Editor project first.", portFile);

        var json = File.ReadAllText(portFile);
        try
        {
            var info = JsonSerializer.Deserialize<ServerInfo>(json, JsonOptions)
                ?? throw new IOException("Failed to read server port file.");

            if (info.Port <= 0)
                throw new IOException("Failed to read server port file.");

            return info;
        }
        catch (JsonException ex)
        {
            throw new IOException("Failed to read server port file.", ex);
        }
    }

    public static int ReadServerPort(string projectPath)
    {
        var info = ReadServerInfo(projectPath);
        return info.Port;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public class ServerInfo
{
    public int Port { get; set; }
    public int Pid { get; set; }
    public string? Status { get; set; }
}
