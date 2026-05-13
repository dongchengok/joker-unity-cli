using System.Collections.Generic;

namespace Joker.UnityCli.Editor.ScriptExecution
{
    public static class UsingParser
    {
        public static List<string> ParseExplicitUsings(string code)
        {
            var usings = new List<string>();
            var lines = code.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("using ") && trimmed.EndsWith(";"))
                {
                    var ns = trimmed.Substring(6, trimmed.Length - 7).Trim();
                    if (!string.IsNullOrEmpty(ns) && ns != "static" && !ns.StartsWith("static ") && !ns.Contains("="))
                        usings.Add(ns);
                }
            }
            return usings;
        }
    }
}
