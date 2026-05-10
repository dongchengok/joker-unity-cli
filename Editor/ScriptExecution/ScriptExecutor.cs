using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Joker.UnityCli.Editor.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using UnityEngine;

namespace Joker.UnityCli.Editor.ScriptExecution
{
    public static class ScriptExecutor
    {
        public static async Task<ExecResult> ExecuteAsync(ExecRequest request, CancellationToken ct)
        {
            return request.Mode == "compile"
                ? await ExecuteCompileAsync(request.Code, request.Timeout, ct)
                : await ExecuteScriptAsync(request.Code, request.Timeout, ct);
        }

        private static async Task<ExecResult> ExecuteScriptAsync(string code, int timeoutMs, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var options = ScriptOptions.Default
                    .WithImports("UnityEngine", "UnityEditor", "System", "System.Linq", "System.Collections.Generic")
                    .WithReferences(
                        typeof(object).Assembly,
                        typeof(UnityEngine.Debug).Assembly,
                        typeof(UnityEditor.EditorApplication).Assembly,
                        typeof(Enumerable).Assembly);

                var globals = new ScriptGlobals();
                var result = await CSharpScript.EvaluateAsync(code, options, globals, typeof(ScriptGlobals), ct);
                sw.Stop();
                return new ExecResult
                {
                    Type = "exec_result",
                    Success = true,
                    Result = result?.ToString() ?? "null",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (CompilationErrorException ex)
            {
                sw.Stop();
                var errors = string.Join("\n", ex.Diagnostics.Select(d => d.ToString()));
                return new ExecResult
                {
                    Type = "exec_result",
                    Success = false,
                    Error = errors,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new ExecResult
                {
                    Type = "exec_result",
                    Success = false,
                    Error = $"Timed out after {timeoutMs}ms",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new ExecResult
                {
                    Type = "exec_result",
                    Success = false,
                    Error = ex.ToString(),
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }

        private static async Task<ExecResult> ExecuteCompileAsync(string code, int timeoutMs, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                ct.ThrowIfCancellationRequested();

                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var references = new List<MetadataReference>
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(UnityEngine.Debug).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(UnityEditor.EditorApplication).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
                };

                var compilation = CSharpCompilation.Create(
                    $"JokerExec_{Guid.NewGuid():N}",
                    new[] { syntaxTree },
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                using var ms = new MemoryStream();
                var emitResult = compilation.Emit(ms);
                if (!emitResult.Success)
                {
                    sw.Stop();
                    var errors = string.Join("\n", emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                    return new ExecResult
                    {
                        Type = "exec_result",
                        Success = false,
                        Error = errors,
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                ms.Seek(0, SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());

                var executeMethod = assembly.GetTypes()
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    .FirstOrDefault(m => m.Name == "Execute" && m.GetParameters().Length == 0);

                if (executeMethod == null)
                {
                    sw.Stop();
                    return new ExecResult
                    {
                        Type = "exec_result",
                        Success = false,
                        Error = "No 'public static void Execute()' method found.",
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                var execResult = executeMethod.Invoke(null, null);
                sw.Stop();
                return new ExecResult
                {
                    Type = "exec_result",
                    Success = true,
                    Result = execResult?.ToString() ?? "null",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new ExecResult
                {
                    Type = "exec_result",
                    Success = false,
                    Error = $"Timed out after {timeoutMs}ms",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new ExecResult
                {
                    Type = "exec_result",
                    Success = false,
                    Error = ex.ToString(),
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }
    }
}
