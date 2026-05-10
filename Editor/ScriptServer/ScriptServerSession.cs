using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Joker.UnityCli.Editor.Models;
using Joker.UnityCli.Editor.ScriptExecution;
using UnityEditor;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public static class ScriptServerSession
    {
        public static async Task HandleAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream);
                using var writer = new StreamWriter(stream) { AutoFlush = true };

                var line = await reader.ReadLineAsync();
                if (line == null) return;

                var request = ParseExecRequest(line);
                if (request == null) return;

                var tcs = new TaskCompletionSource<ExecResult>();

                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        var task = ScriptExecutor.ExecuteAsync(request, ct);
                        task.ContinueWith(t =>
                        {
                            if (t.IsCompletedSuccessfully)
                                tcs.SetResult(t.Result);
                            else if (t.IsCanceled)
                                tcs.SetCanceled();
                            else
                                tcs.SetException(t.Exception!.InnerException ?? t.Exception);
                        }, TaskScheduler.Default);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                };

                var result = await tcs.Task;
                result.Id = request.Id;

                var responseJson = SerializeExecResult(result);
                await writer.WriteLineAsync(responseJson);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[JokerUnity] Session error: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }

        private static ExecRequest ParseExecRequest(string json)
        {
            var request = new ExecRequest();

            var id = ExtractStringValue(json, "id");
            if (id != null) request.Id = id;

            var type = ExtractStringValue(json, "type");
            if (type != null) request.Type = type;

            var code = ExtractStringValue(json, "code");
            if (code != null) request.Code = code;

            var mode = ExtractStringValue(json, "mode");
            if (mode != null) request.Mode = mode;

            var timeout = ExtractIntValue(json, "timeout");
            if (timeout.HasValue) request.Timeout = timeout.Value;

            return request;
        }

        private static string? ExtractStringValue(string json, string key)
        {
            var searchKey = $"\"{key}\"";
            var keyIndex = json.IndexOf(searchKey, StringComparison.Ordinal);
            if (keyIndex < 0) return null;

            var colonIndex = json.IndexOf(':', keyIndex + searchKey.Length);
            if (colonIndex < 0) return null;

            var valueStart = colonIndex + 1;
            while (valueStart < json.Length && json[valueStart] == ' ') valueStart++;

            if (valueStart >= json.Length) return null;

            if (json[valueStart] != '"') return null;

            var endQuote = valueStart + 1;
            while (endQuote < json.Length)
            {
                if (json[endQuote] == '\\' && endQuote + 1 < json.Length)
                {
                    endQuote += 2;
                    continue;
                }
                if (json[endQuote] == '"') break;
                endQuote++;
            }

            var raw = json.Substring(valueStart + 1, endQuote - valueStart - 1);
            return UnescapeJsonString(raw);
        }

        private static int? ExtractIntValue(string json, string key)
        {
            var searchKey = $"\"{key}\"";
            var keyIndex = json.IndexOf(searchKey, StringComparison.Ordinal);
            if (keyIndex < 0) return null;

            var colonIndex = json.IndexOf(':', keyIndex + searchKey.Length);
            if (colonIndex < 0) return null;

            var valueStart = colonIndex + 1;
            while (valueStart < json.Length && json[valueStart] == ' ') valueStart++;

            var endIdx = valueStart;
            while (endIdx < json.Length && (char.IsDigit(json[endIdx]) || json[endIdx] == '-')) endIdx++;

            if (endIdx == valueStart) return null;

            return int.Parse(json.Substring(valueStart, endIdx - valueStart));
        }

        private static string UnescapeJsonString(string s)
        {
            return s.Replace("\\\"", "\"")
                    .Replace("\\\\", "\\")
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t");
        }

        private static string SerializeExecResult(ExecResult result)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('{');
            sb.Append("\"type\":"); AppendString(sb, result.Type);
            sb.Append(',');
            sb.Append("\"id\":"); AppendString(sb, result.Id);
            sb.Append(',');
            sb.Append("\"success\":"); sb.Append(result.Success ? "true" : "false");
            sb.Append(',');
            sb.Append("\"result\":"); AppendString(sb, result.Result);
            sb.Append(',');
            sb.Append("\"output\":"); AppendString(sb, result.Output);
            sb.Append(',');
            sb.Append("\"error\":"); AppendString(sb, result.Error);
            sb.Append(',');
            sb.Append("\"durationMs\":"); sb.Append(result.DurationMs);
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendString(System.Text.StringBuilder sb, string value)
        {
            sb.Append('"');
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default: sb.Append(c); break;
                    }
                }
            }
            sb.Append('"');
        }
    }
}
