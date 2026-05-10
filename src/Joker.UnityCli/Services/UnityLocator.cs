using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class UnityLocator : IUnityLocator
{
    private static readonly System.Text.RegularExpressions.Regex VersionPattern = new(@"^\d+\.\d+\.\d+[abcdef]\d+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly string _hubPath;

    public UnityLocator(string? hubPath = null)
    {
        _hubPath = hubPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Unity", "Hub", "Editor");
    }

    public UnityInstallation? Locate(string? pathOrVersion = null)
    {
        // No argument: find latest version from hub
        if (string.IsNullOrEmpty(pathOrVersion))
            return FindLatestVersion(_hubPath);

        // Looks like a version string: search hub for that version
        if (VersionPattern.IsMatch(pathOrVersion))
            return FindSpecificVersion(_hubPath, pathOrVersion);

        // Treat as explicit file path
        if (File.Exists(pathOrVersion))
        {
            return new UnityInstallation
            {
                Path = pathOrVersion,
                Version = ExtractVersionFromPath(pathOrVersion)
            };
        }

        return null;
    }

    private UnityInstallation? FindLatestVersion(string hubPath)
    {
        if (!Directory.Exists(hubPath))
            return null;

        var versions = GetValidVersions(hubPath);
        if (versions.Count == 0)
            return null;

        var latest = versions
            .OrderByDescending(SortKeyForVersion)
            .First();

        var exePath = Path.Combine(hubPath, latest, "Editor", "Unity.exe");
        return new UnityInstallation
        {
            Path = exePath,
            Version = latest
        };
    }

    private UnityInstallation? FindSpecificVersion(string hubPath, string version)
    {
        var editorDir = Path.Combine(hubPath, version, "Editor");
        if (!Directory.Exists(editorDir))
            return null;

        var exePath = Path.Combine(editorDir, "Unity.exe");
        if (!File.Exists(exePath))
            return null;

        return new UnityInstallation
        {
            Path = exePath,
            Version = version
        };
    }

    private static List<string> GetValidVersions(string hubPath)
    {
        return Directory.GetDirectories(hubPath)
            .Select(Path.GetFileName)
            .Where(name => name != null)
            .Where(name => VersionPattern.IsMatch(name!))
            .Where(name => File.Exists(Path.Combine(hubPath, name!, "Editor", "Unity.exe")))
            .Cast<string>()
            .ToList();
    }

    private static string ExtractVersionFromPath(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null)
        {
            var parent = Path.GetDirectoryName(dir);
            if (parent != null)
            {
                var parentName = Path.GetFileName(parent);
                if (VersionPattern.IsMatch(parentName))
                    return parentName;
            }
        }

        return "";
    }

    private static string SortKeyForVersion(string version)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            version, @"^(\d+)\.(\d+)\.(\d+)([abcdef])(\d+)$");

        if (!match.Success)
            return version;

        var major = int.Parse(match.Groups[1].Value).ToString("D4");
        var minor = int.Parse(match.Groups[2].Value).ToString("D4");
        var patch = int.Parse(match.Groups[3].Value).ToString("D4");
        var suffix = match.Groups[4].Value;
        var build = int.Parse(match.Groups[5].Value).ToString("D4");

        return $"{major}.{minor}.{patch}.{suffix}.{build}";
    }
}
