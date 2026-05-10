namespace Joker.UnityCli.Models;

public class LogEntry
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string Severity { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}
