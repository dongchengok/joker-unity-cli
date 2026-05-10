namespace Joker.UnityCli.Models;

public class BuildResult
{
    public bool Success { get; set; }
    public string LogPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public TimeSpan Duration { get; set; }
}
