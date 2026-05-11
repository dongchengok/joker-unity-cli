using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Joker.UnityCli.Editor.Models;
using Joker.UnityCli.Editor.ScriptExecution;
using Newtonsoft.Json;
using UnityEditor;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public static class HttpExecHandler
    {
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
                    execRequest = JsonConvert.DeserializeObject<ExecRequest>(requestBody);
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

                var tcs = new TaskCompletionSource<ExecResult>();
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
                                tcs.SetResult(t.Result);
                            else if (t.IsCanceled)
                                tcs.SetCanceled();
                            else
                                tcs.SetException(t.Exception.InnerException ?? t.Exception);
                        }, TaskScheduler.Default);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                };

                var result = await tcs.Task;
                result.Id = execRequest.Id;

                response.StatusCode = 200;
                response.ContentType = "application/json";
                var responseJson = JsonConvert.SerializeObject(result);
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct);
                response.Close();
            }
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
