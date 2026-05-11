namespace Joker.UnityCli.Models;

public class CompileResult
{
    public bool Success { get; set; }
    public string Status { get; set; } = "";
    public List<string> Errors { get; set; } = [];
    public long DurationMs { get; set; }
}
