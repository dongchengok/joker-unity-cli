namespace Joker.UnityCli.Models;

public class UnityProject
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string UnityVersion { get; set; } = "";
    public List<string> PackageDependencies { get; set; } = new();
}
