#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public static class ScriptServer
    {
        private static TcpListener? _listener;
        private static CancellationTokenSource? _cts;
        private static int _port;

        public static int Port => _port;

        public static void Start()
        {
            Stop();

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            PortRegistry.Write(_port);
            Debug.Log($"[JokerUnity] Script server started on port {_port}");

            var ct = _cts.Token;
            Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var acceptTask = _listener.AcceptTcpClientAsync();
                        var tcs = new TaskCompletionSource<TcpClient>();
                        using var reg = ct.Register(() => tcs.TrySetCanceled());
                        var completed = await Task.WhenAny(acceptTask, tcs.Task);
                        if (completed == tcs.Task)
                            break;
                        var client = await acceptTask;
                        await ScriptServerSession.HandleAsync(client, ct);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Debug.LogError($"[JokerUnity] Server error: {ex.Message}");
                }
            }, ct);
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
            _cts = null;
            PortRegistry.Delete();
        }
    }
}
