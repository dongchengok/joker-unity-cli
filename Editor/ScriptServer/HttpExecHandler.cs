using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Joker.UnityCli.Editor.Models;
using Joker.UnityCli.Editor.ScriptExecution;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEditor;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public static class HttpExecHandler
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };
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

                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            var task = ScriptExecutor.ExecuteAsync(execRequest, cts.Token);
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
