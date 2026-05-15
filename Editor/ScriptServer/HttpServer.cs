using System;
using System.Collections.Concurrent;
using System.Linq;
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
        private static Task _listenerTask;
        private static int _port;

        // Track in-flight request tasks so Stop() can wait for them to complete,
        // preventing DLL file locks during Unity domain reload.
        private static readonly ConcurrentBag<Task> _handlerTasks = new ConcurrentBag<Task>();

        public static int Port => _port;
        public static bool IsRunning => _listener != null && _listener.IsListening;

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
            _listenerTask = Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var context = await _listener.GetContextAsync();
                        var handlerTask = Task.Run(() => HttpExecHandler.HandleAsync(context, ct), ct);
                        _handlerTasks.Add(handlerTask);
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

            // Wait for listener loop and all in-flight request handlers to complete
            // so they release all assembly references before Unity copies the new DLL.
            var tasks = new System.Collections.Generic.List<Task>();
            if (_listenerTask != null)
                tasks.Add(_listenerTask);
            tasks.AddRange(_handlerTasks);
            if (tasks.Count > 0)
            {
                try { Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(3)); } catch { }
            }
            // Clear completed handler tasks so their closures become eligible for GC
            while (_handlerTasks.TryTake(out _)) { }

            // Wait for ScriptExecutor tasks started from EditorApplication.update callbacks
            HttpExecHandler.WaitForScriptTasks(TimeSpan.FromSeconds(3));

            // Force GC to release dynamic assembly references
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            _listenerTask = null;
            _cts = null;
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
            throw new InvalidOperationException("Failed to find available port in range 63000-63100");
        }
    }
}
