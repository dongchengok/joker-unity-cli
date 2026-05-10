using System.Text.Json;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class ProjectDetector : IProjectDetector
{
    public UnityProject? Detect(string path)
    {
        if (!Directory.Exists(path))
            return null;

        var hasAssets = Directory.Exists(System.IO.Path.Combine(path, "Assets"));
        var hasSettings = Directory.Exists(System.IO.Path.Combine(path, "ProjectSettings"));

        if (!hasAssets || !hasSettings)
            return null;

        var project = new UnityProject
        {
            Path = System.IO.Path.GetFullPath(path),
            Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar))
        };

        var versionFile = System.IO.Path.Combine(path, "ProjectSettings", "ProjectVersion.txt");
        if (File.Exists(versionFile))
        {
            var content = File.ReadAllText(versionFile);
            var match = System.Text.RegularExpressions.Regex.Match(content, @"m_EditorVersion:\s*(.+?)(\r?\n|$)");
            if (match.Success)
                project.UnityVersion = match.Groups[1].Value.Trim();
        }

        var manifestPath = System.IO.Path.Combine(path, "Packages", "manifest.json");
        if (File.Exists(manifestPath))
        {
            var json = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("dependencies", out var deps))
            {
                project.PackageDependencies = deps.EnumerateObject().Select(p => p.Name).ToList();
            }
        }

        return project;
    }

    public UnityProject? DetectFromCurrentDirectory(string startPath)
    {
        var current = startPath;
        while (current != null)
        {
            var result = Detect(current);
            if (result != null)
                return result;

            var parent = System.IO.Path.GetDirectoryName(current);
            if (parent == current)
                break;
            current = parent;
        }

        return null;
    }
}
