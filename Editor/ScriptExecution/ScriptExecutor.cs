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
        private const int MaxRetries = 3;

        public static async Task<ExecResult> ExecuteAsync(ExecRequest request, CancellationToken ct)
        {
            UnityEngine.Debug.Log($"[Joker] ScriptExecutor: mode={request.Mode}");
            return request.Mode == "compile"
                ? await ExecuteCompileAsync(request.Code, request.Timeout, ct)
                : await ExecuteScriptAsync(request.Code, request.Timeout, ct);
        }

        internal static List<string> ParseExplicitUsings(string code) => UsingParser.ParseExplicitUsings(code);

        private static List<string> GetDefaultUsings()
        {
            return new List<string>
            {
                "UnityEngine", "UnityEditor",
                "System", "System.Linq", "System.Collections.Generic",
                "System.IO", "System.Text", "System.Threading.Tasks",
                "UnityEngine.SceneManagement"
            };
        }

        private static List<MetadataReference> GetDefaultReferences()
        {
            var refs = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("UnityEngine").Location),
                MetadataReference.CreateFromFile(Assembly.Load("UnityEditor").Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            };
            TryAddReference(refs, typeof(System.IO.File));
            TryAddReference(refs, typeof(System.Text.RegularExpressions.Regex));
            TryAddReference(refs, typeof(System.Threading.Tasks.Task));
            return refs;
        }

        private static void TryAddReference(List<MetadataReference> refs, Type type)
        {
            try
            {
                var loc = type.Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                    refs.Add(MetadataReference.CreateFromFile(loc));
            }
            catch { }
        }

        private static void ApplyFixes(List<ErrorAnalysis> fixes, List<MetadataReference> references, List<string> usings)
        {
            foreach (var fix in fixes)
            {
                switch (fix.FixAction)
                {
                    case FixAction.AddReference:
                        if (!references.Any(r => (r.Display ?? "").Contains(fix.Detail)))
                        {
                            try
                            {
                                var asm = Assembly.Load(fix.Detail);
                                if (!string.IsNullOrEmpty(asm.Location))
                                    references.Add(MetadataReference.CreateFromFile(asm.Location));
                            }
                            catch { }
                        }
                        break;
                    case FixAction.AddUsing:
                        if (!usings.Contains(fix.Detail))
                            usings.Add(fix.Detail);
                        break;
                    case FixAction.RemoveReference:
                        references.RemoveAll(r => (r.Display ?? "").Contains(fix.Detail));
                        break;
                }
            }
        }

        private static List<string> BuildUsings(string code)
        {
            var usings = GetDefaultUsings();
            foreach (var u in ParseExplicitUsings(code))
            {
                if (!usings.Contains(u))
                    usings.Add(u);
            }
            return usings;
        }

        private static async Task<ExecResult> ExecuteScriptAsync(string code, int timeoutMs, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var usings = BuildUsings(code);
                var references = GetDefaultReferences();

                for (int attempt = 0; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        var options = ScriptOptions.Default
                            .WithImports(usings)
                            .WithReferences(references);

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
                        if (attempt >= MaxRetries)
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

                        var analyses = CompilationErrorAnalyzer.Analyze(ex.Diagnostics);
                        if (!analyses.All(a => a.CanAutoFix))
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

                        ApplyFixes(analyses.ToList(), references, usings);
                        UnityEngine.Debug.Log($"[Joker] ScriptExecutor: retry attempt {attempt + 1} after applying fixes");
                    }
                }

                sw.Stop();
                return new ExecResult
                {
                    Type = "exec_result",
                    Success = false,
                    Error = "Max retries exceeded",
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

#pragma warning disable CS1998
        private static async Task<ExecResult> ExecuteCompileAsync(string code, int timeoutMs, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                ct.ThrowIfCancellationRequested();

                var usings = BuildUsings(code);
                var references = GetDefaultReferences();

                for (int attempt = 0; attempt <= MaxRetries; attempt++)
                {
                    var syntaxTree = CSharpSyntaxTree.ParseText(
                        string.Join("\n", usings.Select(u => $"using {u};")) + "\n" + code);

                    var compilation = CSharpCompilation.Create(
                        $"JokerExec_{Guid.NewGuid():N}",
                        new[] { syntaxTree },
                        references,
                        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                    using (var ms = new MemoryStream())
                    {
                        var emitResult = compilation.Emit(ms);
                        if (emitResult.Success)
                        {
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

                        if (attempt >= MaxRetries)
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

                        var errorDiags = emitResult.Diagnostics
                            .Where(d => d.Severity == DiagnosticSeverity.Error);
                        var analyses = CompilationErrorAnalyzer.Analyze(errorDiags);
                        if (!analyses.All(a => a.CanAutoFix))
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

                        ApplyFixes(analyses.ToList(), references, usings);
                        UnityEngine.Debug.Log($"[Joker] ScriptExecutor (compile): retry attempt {attempt + 1} after applying fixes");
                    }
                }

                sw.Stop();
                return new ExecResult
                {
                    Type = "exec_result",
                    Success = false,
                    Error = "Max retries exceeded",
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
