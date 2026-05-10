namespace Joker.UnityCli.Models;

public class ExecResult
{
    public string Type { get; set; } = "exec_result";
    public string Id { get; set; } = "";
    public bool Success { get; set; }
    public string? Result { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public long DurationMs { get; set; }
}