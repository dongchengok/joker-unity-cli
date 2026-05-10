using Joker.UnityCli.Models;

namespace Joker.UnityCli.Services;

public interface IProjectDetector
{
    UnityProject? Detect(string path);
    UnityProject? DetectFromCurrentDirectory(string startPath);
}
