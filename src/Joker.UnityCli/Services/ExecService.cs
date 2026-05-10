using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class ExecService : IExecService
{
    public async Task<ExecResult> ExecuteAsync(string projectPath, string code, string mode, int timeoutMs, CancellationToken ct)
    {
        var port = ReadServerPort(projectPath);
        var request = new ExecRequest
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Code = code,
            Mode = mode,
            Timeout = timeoutMs
        };

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port, ct);

        using var stream = client.GetStream();
        using var writer = new StreamWriter(stream) { AutoFlush = true };
        using var reader = new StreamReader(stream);

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        await writer.WriteAsync(requestJson).WaitAsync(ct);
        await writer.WriteAsync("\n").WaitAsync(ct);

        var responseLine = await reader.ReadLineAsync(ct)
            ?? throw new IOException("Server closed connection without response");

        return JsonSerializer.Deserialize<ExecResult>(responseLine, JsonOptions)
            ?? throw new IOException("Failed to deserialize server response");
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
