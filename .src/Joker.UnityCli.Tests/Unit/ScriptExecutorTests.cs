using System.Collections.Generic;
using FluentAssertions;
using Joker.UnityCli.Editor.ScriptExecution;
using Xunit;

namespace Joker.UnityCli.Tests.Unit
{
    public class ScriptExecutorTests
    {
        [Fact]
        public void ParseExplicitUsings_ExtractsAllUsingStatements()
        {
            var code = @"
using System;
using System.IO;
using UnityEngine;
public class Test { }";

            var result = UsingParser.ParseExplicitUsings(code);

            result.Should().Contain("System");
            result.Should().Contain("System.IO");
            result.Should().Contain("UnityEngine");
            result.Should().HaveCount(3);
        }

        [Fact]
        public void ParseExplicitUsings_NoUsingStatements_ReturnsEmpty()
        {
            var code = "public class Test { }";
            var result = UsingParser.ParseExplicitUsings(code);
            result.Should().BeEmpty();
        }

        [Fact]
        public void ParseExplicitUsings_StaticUsing_NotIncluded()
        {
            var code = @"using static System.Math;
using System;
public class Test { }";
            var result = UsingParser.ParseExplicitUsings(code);
            result.Should().Contain("System");
            result.Should().NotContain("static System.Math");
            result.Should().HaveCount(1);
        }

        [Fact]
        public void ParseExplicitUsings_AliasUsing_NotIncluded()
        {
            var code = @"using X = System.Collections.Generic.List<int>;
using System;
public class Test { }";
            var result = UsingParser.ParseExplicitUsings(code);
            result.Should().Contain("System");
            result.Should().HaveCount(1);
        }

        [Fact]
        public void ParseExplicitUsings_EmptyCode_ReturnsEmpty()
        {
            var result = UsingParser.ParseExplicitUsings("");
            result.Should().BeEmpty();
        }

        [Fact]
        public void ParseExplicitUsings_UsingWithNoNamespace_Skipped()
        {
            var code = "using ; public class Test { }";
            var result = UsingParser.ParseExplicitUsings(code);
            result.Should().BeEmpty();
        }

        [Fact]
        public void ParseExplicitUsings_DuplicateUsings_AllReturned()
        {
            var code = @"using System;
using System;
public class Test { }";
            var result = UsingParser.ParseExplicitUsings(code);
            result.Should().HaveCount(2);
            result.Should().Contain("System");
        }
    }
}
