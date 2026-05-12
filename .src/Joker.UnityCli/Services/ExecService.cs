using System.IO;
using System.Net.Http;
using System.Text.Json;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class ExecService : IExecService
{
    private static readonly HttpClient SharedClient = new();

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
        var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        var retryDelay = TimeSpan.FromSeconds(1);
        var maxRetryDelay = TimeSpan.FromSeconds(5);
        int port;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                port = ReadServerPort(projectPath);
                using var cts = new CancellationTokenSource(Math.Max(5000, (int)(deadline - DateTime.UtcNow).TotalMilliseconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

                var response = await SharedClient.PostAsync($"http://127.0.0.1:{port}/exec", content, linkedCts.Token);
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
            catch (Exception ex) when (
                (ex is HttpRequestException
                 || (ex is FileNotFoundException fnf && fnf.Message.Contains("Unity server not running"))
                 || (ex is TaskCanceledException && !ct.IsCancellationRequested))
                && DateTime.UtcNow < deadline)
            {
                await Task.Delay(retryDelay, ct);
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, maxRetryDelay.Ticks));
                content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
            }
        }
    }

    public static int ReadServerPort(string projectPath)
    {
        var portFile = Path.Combine(projectPath, ".joker-unity", "server.json");
        if (!File.Exists(portFile))
            throw new FileNotFoundException(
                "Unity server not running. Open the Unity Editor project first.", portFile);

        var json = File.ReadAllText(portFile);
        var info = JsonSerializer.Deserialize<ServerInfo>(json, JsonOptions)
            ?? throw new IOException("Failed to read server port file.");

        return info.Port;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private class ServerInfo
    {
        public int Port { get; set; }
        public int Pid { get; set; }
    }
}
