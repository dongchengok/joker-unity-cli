using System.IO;
using System.Net.Http;
using System.Text.Json;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class StatusService : IStatusService
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public async Task<ServerStatus> GetStatusAsync(string projectPath, CancellationToken ct)
    {
        var serverFile = Path.Combine(projectPath, ".joker-unity", "server.json");
        if (!File.Exists(serverFile))
        {
            return new ServerStatus { Status = "not_found" };
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(serverFile, ct);
        }
        catch (IOException)
        {
            return new ServerStatus { Status = "not_found" };
        }

        ServerFileData? data;
        try
        {
            data = JsonSerializer.Deserialize<ServerFileData>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return new ServerStatus { Status = "unknown" };
        }

        if (data == null)
        {
            return new ServerStatus { Status = "unknown" };
        }

        var status = new ServerStatus
        {
            Status = string.IsNullOrEmpty(data.Status) ? "unknown" : data.Status,
            Port = data.Port,
            Pid = data.Pid
        };

        if (status.Status == "ready")
        {
            try
            {
                var response = await SharedClient.GetAsync($"http://127.0.0.1:{status.Port}/exec", ct);
                status.ServerResponding = response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed;
            }
            catch
            {
                status.ServerResponding = false;
            }
        }

        return status;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private class ServerFileData
    {
        public int Port { get; set; }
        public int Pid { get; set; }
        public string? Status { get; set; }
    }
}
