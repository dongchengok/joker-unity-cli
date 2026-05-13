using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Joker.UnityCli.Editor.ScriptExecution
{
    public enum FixAction
    {
        AddUsing,
        AddReference,
        RemoveReference,
        CannotFix
    }

    public class ErrorAnalysis
    {
        public string ErrorCode { get; set; } = "";
        public bool CanAutoFix => FixAction != FixAction.CannotFix;
        public FixAction FixAction { get; set; } = FixAction.CannotFix;
        public string Detail { get; set; } = "";
    }

    public static class CompilationErrorAnalyzer
    {
        private static readonly Dictionary<string, string> NamespaceToAssemblyMap = new Dictionary<string, string>()
        {
            { "System.IO", "mscorlib" },
            { "System.Text", "mscorlib" },
            { "System.Text.RegularExpressions", "System" },
            { "System.Threading.Tasks", "mscorlib" },
            { "System.Reflection", "mscorlib" },
            { "System.Diagnostics", "System" },
            { "System.Net.Http", "System.Net.Http" },
            { "System.Linq", "System.Core" },
            { "System.Collections.Generic", "mscorlib" },
            { "UnityEngine.UI", "UnityEngine.UI" },
            { "UnityEngine.SceneManagement", "UnityEngine.CoreModule" },
            { "UnityEngine.EventSystems", "UnityEngine.UI" },
            { "UnityEngine.Networking", "UnityEngine" },
        };

        public static IReadOnlyList<ErrorAnalysis> Analyze(IEnumerable<Diagnostic> diagnostics)
        {
            var results = new List<ErrorAnalysis>();

            foreach (var diag in diagnostics)
            {
                if (diag.Severity != DiagnosticSeverity.Error)
                    continue;

                var id = diag.Id;
                var message = diag.GetMessage(CultureInfo.InvariantCulture);

                switch (id)
                {
                    case "CS0012":
                        var cs0012Assembly = ExtractAssemblyFromCS0012(message);
                        if (cs0012Assembly != null)
                        {
                            results.Add(new ErrorAnalysis
                            {
                                ErrorCode = "CS0012",
                                FixAction = FixAction.AddReference,
                                Detail = cs0012Assembly
                            });
                        }
                        else
                        {
                            results.Add(new ErrorAnalysis { ErrorCode = "CS0012" });
                        }
                        break;

                    case "CS0246":
                        var cs0246TypeName = ExtractTypeNameFromMessage(message);
                        if (cs0246TypeName != null)
                        {
                            var fix = ResolveTypeLocation(cs0246TypeName);
                            results.Add(fix ?? new ErrorAnalysis { ErrorCode = "CS0246" });
                        }
                        else
                        {
                            results.Add(new ErrorAnalysis { ErrorCode = "CS0246" });
                        }
                        break;

                    case "CS0234":
                        var cs0234Namespace = ExtractNamespaceFromCS0234(message);
                        if (cs0234Namespace != null)
                        {
                            var asmName = ResolveNamespace(cs0234Namespace);
                            if (asmName != null)
                            {
                                results.Add(new ErrorAnalysis
                                {
                                    ErrorCode = "CS0234",
                                    FixAction = FixAction.AddReference,
                                    Detail = asmName
                                });
                            }
                            else
                            {
                                results.Add(new ErrorAnalysis { ErrorCode = "CS0234" });
                            }
                        }
                        else
                        {
                            results.Add(new ErrorAnalysis { ErrorCode = "CS0234" });
                        }
                        break;

                    case "CS0433":
                        var conflictedAsm = ExtractConflictingAssembly(message);
                        if (conflictedAsm != null)
                        {
                            results.Add(new ErrorAnalysis
                            {
                                ErrorCode = "CS0433",
                                FixAction = FixAction.RemoveReference,
                                Detail = conflictedAsm
                            });
                        }
                        else
                        {
                            results.Add(new ErrorAnalysis { ErrorCode = "CS0433" });
                        }
                        break;

                    case "CS0103":
                        var cs0103Name = ExtractTypeNameFromMessage(message);
                        if (cs0103Name != null)
                        {
                            var fix = ResolveTypeLocation(cs0103Name);
                            if (fix != null)
                            {
                                fix.ErrorCode = "CS0103";
                                results.Add(fix);
                            }
                            else
                            {
                                results.Add(new ErrorAnalysis { ErrorCode = "CS0103" });
                            }
                        }
                        else
                        {
                            results.Add(new ErrorAnalysis { ErrorCode = "CS0103" });
                        }
                        break;

                    default:
                        results.Add(new ErrorAnalysis { ErrorCode = id });
                        break;
                }
            }

            return results.AsReadOnly();
        }

        private static string ExtractAssemblyFromCS0012(string message)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                message, @"assembly\s+'([^']+)'");
            return match.Success ? match.Groups[1].Value.Split(',')[0].Trim() : null;
        }

        private static string ExtractTypeNameFromMessage(string message)
        {
            // CS0246: "The type or namespace name 'X' could not be found"
            // CS0103: "The name 'X' does not exist in the current context"
            var match = System.Text.RegularExpressions.Regex.Match(
                message, @"name\s+'([^']+)'");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ExtractNamespaceFromCS0234(string message)
        {
            // "The type or namespace name 'X' does not exist in the namespace 'Y'"
            var match = System.Text.RegularExpressions.Regex.Match(
                message, @"namespace\s+'([^']+)'");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ExtractConflictingAssembly(string message)
        {
            // "The type 'X' exists in both 'UnityEngine.CoreModule, ...' and 'UnityEngine, ...'"
            var matches = System.Text.RegularExpressions.Regex.Matches(
                message, @"'([^']+)'");
            if (matches.Count >= 2)
            {
                var first = matches[0].Groups[1].Value;
                var second = matches[1].Groups[1].Value;
                if (first.Contains("CoreModule"))
                    return first.Split(',')[0].Trim();
                if (second.Contains("CoreModule"))
                    return second.Split(',')[0].Trim();
            }
            return null;
        }

        private static ErrorAnalysis ResolveTypeLocation(string typeName)
        {
            // Step 1: 通过映射表的命名空间推测，检查类型是否存在
            foreach (var kvp in NamespaceToAssemblyMap)
            {
                var fullName = $"{kvp.Key}.{typeName}";
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.IsDynamic) continue;
                    try
                    {
                        var t = asm.GetType(fullName, false);
                        if (t != null)
                        {
                            return new ErrorAnalysis
                            {
                                ErrorCode = "CS0246",
                                FixAction = FixAction.AddReference,
                                Detail = asm.GetName().Name ?? kvp.Value
                            };
                        }
                    }
                    catch { }
                }
            }

            // Step 2: 兜底扫描 — 在所有已加载程序集中搜索类型名
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                try
                {
                    var types = asm.GetExportedTypes();
                    if (types.Any(t => t.Name == typeName))
                    {
                        return new ErrorAnalysis
                        {
                            ErrorCode = "CS0246",
                            FixAction = FixAction.AddReference,
                            Detail = asm.GetName().Name ?? asm.FullName ?? ""
                        };
                    }
                }
                catch { }
            }

            return null;
        }

        private static string ResolveNamespace(string namespaceName)
        {
            // 先查映射表
            if (NamespaceToAssemblyMap.TryGetValue(namespaceName, out var asmName))
                return asmName;

            // 映射表未命中：在已加载程序集中搜索
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                try
                {
                    if (asm.GetExportedTypes().Any(t => t.Namespace == namespaceName))
                        return asm.GetName().Name ?? asm.FullName ?? "";
                }
                catch { }
            }

            return null;
        }
    }
}
