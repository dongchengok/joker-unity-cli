namespace Joker.UnityCli.Models;

public class ExecRequest
{
    public string Type { get; set; } = "exec";
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string Mode { get; set; } = "script";
    public int Timeout { get; set; } = 30000;
}