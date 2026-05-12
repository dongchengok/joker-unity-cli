using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public static class HttpServer
    {
        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static int _port;

        public static int Port => _port;

        public static void Start()
        {
            Stop();

            _port = FindAvailablePort();
            _cts = new CancellationTokenSource();

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();

            PortRegistry.Write(_port);
            Debug.Log($"[JokerUnity] HTTP server started on port {_port}");

            var ct = _cts.Token;
            Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var context = await _listener.GetContextAsync();
                        _ = Task.Run(() => HttpExecHandler.HandleAsync(context, ct), ct);
                    }
                }
                catch (HttpListenerException) { }
                catch (ObjectDisposedException) { }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Debug.LogError($"[JokerUnity] HTTP server error: {ex.Message}");
                }
            }, ct);
        }

        public static void Stop()
        {
            if (_listener != null)
                Debug.Log("[JokerUnity] HTTP server stopping");

            _cts?.Cancel();
            SessionManager.CancelAll();
            if (_listener != null)
            {
                try { _listener.Stop(); } catch { }
                _listener = null;
            }
            _cts = null;
            PortRegistry.Delete();
        }

        private static int FindAvailablePort()
        {
            var random = new System.Random();
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
            throw new InvalidOperationException("Failed to find available port in range 63000-63099");
        }
    }
}
