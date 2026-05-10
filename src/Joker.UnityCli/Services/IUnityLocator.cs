using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface IUnityLocator
{
    /// <summary>
    /// Locate a Unity installation.
    /// If <paramref name="pathOrVersion"/> is a valid file path to Unity.exe, returns that installation.
    /// If it looks like a version string (e.g. "2022.3.20f1"), searches the hub directory for that version.
    /// If null or empty, returns the latest version found in the hub directory.
    /// </summary>
    UnityInstallation? Locate(string? pathOrVersion = null);
}
