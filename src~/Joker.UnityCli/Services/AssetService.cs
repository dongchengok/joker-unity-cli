using System.Text.RegularExpressions;
using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public class AssetService : IAssetService
{
    public IEnumerable<AssetInfo> ListAssets(string assetsPath)
    {
        if (!Directory.Exists(assetsPath))
            return Enumerable.Empty<AssetInfo>();

        var assets = new List<AssetInfo>();

        foreach (var file in Directory.EnumerateFiles(assetsPath, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);

            // Skip .meta files
            if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            var relativePath = Path.GetRelativePath(assetsPath, file).Replace('\\', '/');
            var guid = ParseGuidFromMeta(file + ".meta");

            assets.Add(new AssetInfo
            {
                RelativePath = relativePath,
                Guid = guid,
                Extension = Path.GetExtension(file),
            });
        }

        return assets;
    }

    public IEnumerable<AssetInfo> SearchAssets(string assetsPath, string query)
    {
        if (string.IsNullOrEmpty(query))
            return ListAssets(assetsPath);

        return ListAssets(assetsPath)
            .Where(a => a.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string ParseGuidFromMeta(string metaPath)
    {
        if (!File.Exists(metaPath))
            return "";

        try
        {
            var content = File.ReadAllText(metaPath);
            var match = Regex.Match(content, @"guid:\s*([a-f0-9]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "";
        }
        catch
        {
            return "";
        }
    }
}
