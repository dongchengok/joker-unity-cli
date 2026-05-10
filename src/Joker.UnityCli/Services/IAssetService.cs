using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface IAssetService
{
    IEnumerable<AssetInfo> ListAssets(string assetsPath);
    IEnumerable<AssetInfo> SearchAssets(string assetsPath, string query);
}
