using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Joker.UnityCli.Editor.Models;
using Joker.UnityCli.Editor.ScriptExecution;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEditor;
using UnityEngine;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public static class HttpExecHandler
    {
        private static volatile bool _isCompiling;
        private static int _isExecuting;
        private static readonly ConcurrentBag<Task> _scriptTasks = new ConcurrentBag<Task>();

        public static bool IsCompiling
        {
            get { return _isCompiling; }
            set { _isCompiling = value; }
        }

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        public static void WaitForScriptTasks(TimeSpan timeout)
        {
            var remaining = _scriptTasks.ToArray();
            if (remaining.Length == 0)
                return;
            try { Task.WaitAll(remaining, timeout); } catch { }
            // Clear completed tasks so their closures (holding MetadataReferences)
            // become eligible for GC before domain reload.
            while (_scriptTasks.TryTake(out _)) { }
        }

        public static void TriggerDelayedRecompile()
        {
            if (_isCompiling) return;
            _isCompiling = true;
            PortRegistry.WriteStatus("compiling");
            UnityEngine.Debug.Log("[JokerUnity] Recompile scheduled...");

            // Phase 1: Wait for HTTP response, then clean up all references
            EditorApplication.CallbackFunction phase1Cleanup = null;
            int f1 = 0;
            phase1Cleanup = () =>
            {
                if (++f1 < 30) return; // ~500ms: ensure HTTP response is sent

                EditorApplication.update -= phase1Cleanup;

                // Clear script task closures (holding MetadataReferences)
                WaitForScriptTasks(TimeSpan.FromSeconds(3));

                // Stop server and clear handler task closures
                HttpServer.Stop();

                UnityEngine.Debug.Log("[JokerUnity] Cleanup complete, settling...");

                // Phase 2: Wait for OS to fully release file handles, then trigger recompile
                EditorApplication.CallbackFunction phase2Recompile = null;
                int f2 = 0;
                phase2Recompile = () =>
                {
                    if (++f2 < 60) return; // ~1s for OS file handle release

                    EditorApplication.update -= phase2Recompile;

                    UnityEngine.Debug.Log("[JokerUnity] Triggering domain reload");
                    UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                };
                EditorApplication.update += phase2Recompile;
            };
            EditorApplication.update += phase1Cleanup;
        }

        public static async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                if (request.HttpMethod != "POST")
                {
                    response.StatusCode = 405;
                    response.Close();
                    return;
                }

                if (request.Url.LocalPath != "/exec")
                {
                    response.StatusCode = 404;
                    response.Close();
                    return;
                }

                // Reject requests during compilation or while another request is executing.
                // Interlocked.CompareExchange ensures atomic check-and-set: only one
                // request can pass through at a time, closing the timing gap between
                // the check here and script dispatch on the main thread.
                if (_isCompiling || Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
                {
                    response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    response.ContentType = "application/json";
                    var busyResult = new ExecResult
                    {
                        Type = "exec_result",
                        Success = false,
                        ErrorCode = "compiling",
                        Error = "Unity is currently recompiling. Please retry after compilation completes."
                    };
                    var errorBuffer = System.Text.Encoding.UTF8.GetBytes(
                        JsonConvert.SerializeObject(busyResult, JsonSettings));
                    await response.OutputStream.WriteAsync(errorBuffer, 0, errorBuffer.Length, ct);
                    response.Close();
                    return;
                }

                try
                {
                    string requestBody;
                    using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        requestBody = await reader.ReadToEndAsync();
                    }

                    ExecRequest execRequest;
                    try
                    {
                        execRequest = JsonConvert.DeserializeObject<ExecRequest>(requestBody, JsonSettings);
                        if (execRequest == null || string.IsNullOrEmpty(execRequest.Code))
                        {
                            response.StatusCode = 400;
                            response.Close();
                            return;
                        }
                    }
                    catch (JsonException)
                    {
                        response.StatusCode = 400;
                        response.Close();
                        return;
                    }

                    var session = SessionManager.GetOrCreate(execRequest.Id);

                    if (session.TryStart())
                    {
                        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(execRequest.Timeout);

                        EditorApplication.CallbackFunction callback = null;
                        callback = () =>
                        {
                            EditorApplication.update -= callback;
                            if (ct.IsCancellationRequested)
                            {
                                session.CompletionSource.TrySetCanceled();
                                return;
                            }
                            try
                            {
                                var task = ScriptExecutor.ExecuteAsync(execRequest, cts.Token);
                                _scriptTasks.Add(task);
                                task.ContinueWith(t =>
                                {
                                    if (t.Status == TaskStatus.RanToCompletion)
                                        session.CompletionSource.TrySetResult(t.Result);
                                    else if (t.IsCanceled)
                                        session.CompletionSource.TrySetCanceled();
                                    else
                                        session.CompletionSource.TrySetException(t.Exception.InnerException ?? t.Exception);
                                }, TaskScheduler.Default);
                            }
                            catch (Exception ex)
                            {
                                session.CompletionSource.TrySetException(ex);
                            }
                        };
                        EditorApplication.update += callback;
                    }

                    var result = await session.CompletionSource.Task;
                    result.Id = execRequest.Id;

                    response.StatusCode = 200;
                    response.ContentType = "application/json";
                    var responseJson = JsonConvert.SerializeObject(result, JsonSettings);
                    var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct);
                    response.Close();
                }
                finally
                {
                    Interlocked.Exchange(ref _isExecuting, 0);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[JokerUnity] HTTP handler error: {ex.Message}");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { }
            }
        }
    }
}
