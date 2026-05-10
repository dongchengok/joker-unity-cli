using System.ComponentModel;
using Spectre.Console.Cli;

namespace Joker.UnityCli.Commands;

public class GlobalCommandSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Output results in JSON format (for AI/machine consumption)")]
    public bool JsonOutput { get; set; }
}
