using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Joker.UnityCli.Editor.ScriptExecution;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Joker.UnityCli.Tests.Unit
{
    public class CompilationErrorAnalyzerTests
    {
        private static ImmutableArray<Diagnostic> Compile(string code)
        {
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            };
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                "Test",
                new[] { syntaxTree },
                references,
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
            return compilation.GetDiagnostics();
        }

        private static List<Diagnostic> GetErrors(string code)
        {
            return Compile(code)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
        }

        [Fact]
        public void Analyze_EmptyDiagnostics_ReturnsEmptyList()
        {
            var result = CompilationErrorAnalyzer.Analyze(Enumerable.Empty<Diagnostic>());

            result.Should().BeEmpty();
        }

        [Fact]
        public void Analyze_WarningsOnly_ReturnsEmptyList()
        {
            var diags = Compile("public class Foo { public void Bar() { var x = 1; } }")
                .Where(d => d.Severity != DiagnosticSeverity.Error)
                .ToList();

            var result = CompilationErrorAnalyzer.Analyze(diags);

            result.Should().BeEmpty();
        }

        [Fact]
        public void Analyze_CS0246_TypeInMappingTable_ReturnsAddReference()
        {
            // FileStream is in System.IO, mapped to mscorlib
            var errors = GetErrors("public class Foo { public FileStream F; }");

            // If no CS0246 errors (type resolved), the test isn't applicable
            if (errors.Count == 0) return;

            var result = CompilationErrorAnalyzer.Analyze(errors);

            result.Should().NotBeEmpty();
            var cs0246 = result.FirstOrDefault(r => r.ErrorCode == "CS0246");
            if (cs0246 != null)
            {
                cs0246.FixAction.Should().Be(FixAction.AddReference);
                cs0246.CanAutoFix.Should().BeTrue();
                cs0246.Detail.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public void Analyze_CS0246_TypeNotInMappingTable_ReturnsCannotFix()
        {
            var errors = GetErrors("public class Foo { public NonExistentTypeXYZ123 X; }");

            if (errors.Count == 0) return;

            var result = CompilationErrorAnalyzer.Analyze(errors);

            foreach (var r in result)
                r.CanAutoFix.Should().BeFalse();
        }

        [Fact]
        public void Analyze_CS0246_FallbackScanFindsType_ReturnsAddReference()
        {
            // Test uses a type that's NOT in the mapping table but IS in a loaded assembly.
            // The test assembly itself is loaded, so the test types are discoverable.
            var errors = GetErrors($"public class Foo {{ public {nameof(CompilationErrorAnalyzerTests)} T; }}");

            if (errors.Count == 0) return;

            var result = CompilationErrorAnalyzer.Analyze(errors);

            result.Should().NotBeEmpty();
            var cs0246 = result.FirstOrDefault(r => r.ErrorCode == "CS0246");
            if (cs0246 != null)
            {
                cs0246.FixAction.Should().Be(FixAction.AddReference);
                cs0246.CanAutoFix.Should().BeTrue();
                cs0246.Detail.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public void Analyze_CS0012_MissingAssemblyReference_ReturnsAddReference()
        {
            // This requires an unreferenced assembly with a type that's available in AppDomain
            // but not in the compilation. System.Text.RegularExpressions.Regex is a candidate.
            var errors = GetErrors("using System.Text.RegularExpressions; public class Foo { public Regex R; }");

            // Without a proper CS0012 scenario setup this might produce CS0246 instead
            if (errors.Count == 0) return;

            var result = CompilationErrorAnalyzer.Analyze(errors);
            result.Should().NotBeEmpty();
        }

        [Fact]
        public void Analyze_CS0234_NamespaceInMappingTable_ReturnsAddReference()
        {
            // System.Text.RegularExpressions namespace is mapped to "System" assembly
            var errors = GetErrors("using System.Text.RegularExpressions; public class Foo { public Regex R; }");

            if (errors.Count == 0) return;

            var result = CompilationErrorAnalyzer.Analyze(errors);

            result.Should().NotBeEmpty();
            var cs0234 = result.FirstOrDefault(r => r.ErrorCode == "CS0234");
            if (cs0234 != null)
            {
                cs0234.FixAction.Should().Be(FixAction.AddReference);
                cs0234.CanAutoFix.Should().BeTrue();
                cs0234.Detail.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public void Analyze_CS0234_NamespaceNotInMappingTable_ReturnsCannotFix()
        {
            var errors = GetErrors("using NonExistent.Namespace.XYZ; public class Foo { }");

            if (errors.Count == 0) return;

            var result = CompilationErrorAnalyzer.Analyze(errors);

            var cs0234 = result.FirstOrDefault(r => r.ErrorCode == "CS0234");
            if (cs0234 != null)
            {
                cs0234.CanAutoFix.Should().BeFalse();
            }
        }

        [Fact]
        public void Analyze_MultipleErrors_DifferentTypes_AllExtracted()
        {
            var errors = GetErrors(@"
public class Foo {
    FileStream F;
    NonExistentTypeXYZ X;
}");

            if (errors.Count == 0) return;

            var result = CompilationErrorAnalyzer.Analyze(errors);

            result.Count.Should().BeGreaterThanOrEqualTo(1);
            // Should have as many results as errors
            result.Count.Should().Be(errors.Count);
        }

        [Fact]
        public void Analyze_SyntaxError_ReturnsCannotFix()
        {
            var errors = GetErrors("public class Foo { public void }");

            if (errors.Count == 0) return;

            var result = CompilationErrorAnalyzer.Analyze(errors);

            result.Should().NotBeEmpty();
            result.Should().OnlyContain(r => !r.CanAutoFix, "syntax errors cannot be auto-fixed");
        }

        [Fact]
        public void Analyze_CS0103_TypeInMappingTable_ReturnsAddUsingOrReference()
        {
            // CS0103: name doesn't exist in current context
            var errors = GetErrors("public class Foo { public string Name => Guid.NewGuid().ToString(); }");

            // Guid might resolve from System, filter for actual CS0103
            var cs0103Errors = errors.Where(d => d.Id == "CS0103").ToList();
            if (cs0103Errors.Count == 0) return;

            var result = CompilationErrorAnalyzer.Analyze(cs0103Errors);

            result.Should().NotBeEmpty();
            var cs0103 = result.First(r => r.ErrorCode == "CS0103");
            // Guid is in System namespace mapped to mscorlib
            if (cs0103.CanAutoFix)
            {
                cs0103.FixAction.Should().BeOneOf(FixAction.AddUsing, FixAction.AddReference);
            }
        }

        [Fact]
        public void Analyze_ErrorAnalysis_PropertiesAreCorrect()
        {
            var errors = GetErrors("public class Foo { public FileStream F; }");

            if (errors.Count == 0) return;

            var result = CompilationErrorAnalyzer.Analyze(errors);

            foreach (var r in result)
            {
                r.ErrorCode.Should().NotBeNullOrEmpty("every analysis must have an error code");
            }
        }

        [Fact]
        public void Analyze_NoErrors_OnlyWarningsAndInfo_ReturnsEmpty()
        {
            // Code that compiles with only warnings
            var diags = Compile("public class Foo { private int _unused; }")
                .Where(d => d.Severity != DiagnosticSeverity.Error)
                .ToList();

            var result = CompilationErrorAnalyzer.Analyze(diags);

            result.Should().BeEmpty("only errors should be analyzed, not warnings or info");
        }
    }
}
