namespace Joker.UnityCli.Models;

public class ServerStatus
{
    public string Status { get; set; } = "unknown";
    public int Port { get; set; }
    public int Pid { get; set; }
    public bool ServerResponding { get; set; }
}
