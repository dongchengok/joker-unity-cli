using System.Net;
using System.Text.Json;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Tests.Infrastructure;

public sealed class MockHttpServer : IDisposable
{
    private HttpListener? _listener;
    private Task? _serverTask;

    public int Port { get; }

    public MockHttpServer()
    {
        Port = PortHelper.FindAvailablePort();
    }

    public void Start(Action<HttpListenerContext> handler)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();

        _serverTask = Task.Run(async () =>
        {
            try
            {
                var context = await _listener.GetContextAsync();
                handler(context);
            }
            catch { }
        });
    }

    public void StartWithResponse(int statusCode, string body)
    {
        Start(ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json";
            var buffer = System.Text.Encoding.UTF8.GetBytes(body);
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.Close();
        });
    }

    public void StartWithExecResult(ExecResult result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        StartWithResponse(200, json);
    }

    public void StartWithDelay(TimeSpan delay, Action<HttpListenerContext> handler)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();

        _serverTask = Task.Run(async () =>
        {
            await Task.Delay(delay);
            try
            {
                var context = await _listener.GetContextAsync();
                handler(context);
            }
            catch { }
        });
    }

    public void Dispose()
    {
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }
}
