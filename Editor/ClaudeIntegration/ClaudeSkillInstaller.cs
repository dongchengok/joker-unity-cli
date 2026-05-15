using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Joker.UnityCli.Editor.ClaudeIntegration
{
    public static class ClaudeSkillInstaller
    {
        private const string ManifestFileName = "skills-manifest.json";
        private const string InstalledManifestFileName = ".joker-unity-manifest.json";
        private const string PackageName = "com.joker.unity-cli";
        private const string SkillsSubDir = "Editor/ClaudeIntegration/Skills";

        private static readonly string ProjectRoot =
            Path.GetDirectoryName(Application.dataPath);

        public static bool IsInstalled
        {
            get
            {
                var manifestPath = GetInstalledManifestPath();
                return File.Exists(manifestPath);
            }
        }

        public static string InstalledVersion
        {
            get
            {
                var manifestPath = GetInstalledManifestPath();
                if (!File.Exists(manifestPath)) return null;
                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var manifest = JsonUtility.FromJson<SkillsManifest>(json);
                    return manifest?.version;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static string SourceVersion
        {
            get
            {
                var sourcePath = GetSourceManifestPath();
                if (sourcePath == null || !File.Exists(sourcePath)) return null;
                try
                {
                    var json = File.ReadAllText(sourcePath);
                    var manifest = JsonUtility.FromJson<SkillsManifest>(json);
                    return manifest?.version;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static void AutoInstall()
        {
            var claudeDir = Path.Combine(ProjectRoot, ".claude");
            if (!Directory.Exists(claudeDir)) return;

            var sourceVersion = SourceVersion;
            if (sourceVersion == null) return;

            var installedVersion = InstalledVersion;
            if (installedVersion == sourceVersion) return;

            PerformInstall();
        }

        public static void Install()
        {
            var claudeDir = Path.Combine(ProjectRoot, ".claude");
            if (!Directory.Exists(claudeDir))
                Directory.CreateDirectory(claudeDir);

            PerformInstall();
        }

        public static void Uninstall()
        {
            var skillsDir = Path.Combine(ProjectRoot, ".claude", "skills");
            var manifestPath = GetInstalledManifestPath();

            var installedSkills = GetInstalledSkillDirectories();
            foreach (var skillDir in installedSkills)
            {
                var fullPath = Path.Combine(skillsDir, skillDir);
                if (Directory.Exists(fullPath))
                    Directory.Delete(fullPath, true);
            }

            if (File.Exists(manifestPath))
                File.Delete(manifestPath);
        }

        private static void PerformInstall()
        {
            var sourceRoot = GetSkillsSourceRoot();
            if (sourceRoot == null) return;

            var sourceManifestPath = Path.Combine(sourceRoot, ManifestFileName);
            if (!File.Exists(sourceManifestPath)) return;

            SkillsManifest manifest;
            try
            {
                var json = File.ReadAllText(sourceManifestPath);
                manifest = JsonUtility.FromJson<SkillsManifest>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Joker] Failed to read skills manifest: {e.Message}");
                return;
            }

            if (manifest?.skills == null || manifest.skills.Length == 0) return;

            var targetSkillsDir = Path.Combine(ProjectRoot, ".claude", "skills");
            if (!Directory.Exists(targetSkillsDir))
                Directory.CreateDirectory(targetSkillsDir);

            // Remove old skill directories before copying new ones
            var oldInstalled = GetInstalledSkillDirectories();
            foreach (var oldDir in oldInstalled)
            {
                var fullPath = Path.Combine(targetSkillsDir, oldDir);
                if (Directory.Exists(fullPath))
                    Directory.Delete(fullPath, true);
            }

            // Copy each skill directory
            foreach (var skill in manifest.skills)
            {
                if (string.IsNullOrEmpty(skill.directory)) continue;

                var sourceDir = Path.Combine(sourceRoot, skill.directory);
                var targetDir = Path.Combine(targetSkillsDir, skill.directory);

                if (!Directory.Exists(sourceDir)) continue;

                CopyDirectory(sourceDir, targetDir);
            }

            // Copy manifest as installed marker
            var installedManifestPath = GetInstalledManifestPath();
            File.Copy(sourceManifestPath, installedManifestPath, true);

            Debug.Log($"[Joker] Claude Code skills installed (v{manifest.version}): " +
                      $"{string.Join(", ", GetDirectoryNames(manifest.skills))}");
        }

        private static string GetSourceManifestPath()
        {
            var root = GetSkillsSourceRoot();
            return root != null ? Path.Combine(root, ManifestFileName) : null;
        }

        private static string GetInstalledManifestPath()
        {
            return Path.Combine(ProjectRoot, ".claude", "skills", InstalledManifestFileName);
        }

        private static string GetSkillsSourceRoot()
        {
            // Try local/embedded package first
            var localPath = Path.Combine(ProjectRoot, "Packages", PackageName, SkillsSubDir);
            if (Directory.Exists(localPath)) return localPath;

            // Try PackageCache (git URL / registry install)
            var cacheDir = Path.Combine(ProjectRoot, "Library", "PackageCache");
            if (Directory.Exists(cacheDir))
            {
                try
                {
                    var dirs = Directory.GetDirectories(cacheDir, PackageName + "@*");
                    foreach (var dir in dirs)
                    {
                        var skillsPath = Path.Combine(dir, SkillsSubDir);
                        if (Directory.Exists(skillsPath)) return skillsPath;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // Try resolving file: reference from manifest.json
            var manifestPath = Path.Combine(ProjectRoot, "Packages", "manifest.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var content = File.ReadAllText(manifestPath);
                    var match = System.Text.RegularExpressions.Regex.Match(
                        content, @"""com\.joker\.unity-cli""\s*:\s*""([^""]+)""");
                    if (match.Success && match.Groups[1].Value.StartsWith("file:"))
                    {
                        var relativePath = match.Groups[1].Value.Substring(5);
                        var packagesDir = Path.Combine(ProjectRoot, "Packages");
                        var resolvedPath = Path.GetFullPath(Path.Combine(packagesDir, relativePath));
                        var skillsPath = Path.Combine(resolvedPath, SkillsSubDir);
                        if (Directory.Exists(skillsPath)) return skillsPath;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            return null;
        }

        private static List<string> GetInstalledSkillDirectories()
        {
            var result = new List<string>();
            var manifestPath = GetInstalledManifestPath();
            if (!File.Exists(manifestPath)) return result;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonUtility.FromJson<SkillsManifest>(json);
                if (manifest?.skills != null)
                {
                    foreach (var skill in manifest.skills)
                    {
                        if (!string.IsNullOrEmpty(skill.directory))
                            result.Add(skill.directory);
                    }
                }
            }
            catch
            {
                // ignore
            }

            return result;
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.EndsWith(".meta")) continue;
                File.Copy(file, Path.Combine(targetDir, fileName), true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.EndsWith(".meta")) continue;
                CopyDirectory(dir, Path.Combine(targetDir, dirName));
            }
        }

        private static string[] GetDirectoryNames(SkillEntry[] skills)
        {
            var names = new string[skills.Length];
            for (int i = 0; i < skills.Length; i++)
                names[i] = skills[i].directory;
            return names;
        }

        [Serializable]
        private class SkillsManifest
        {
            public string version;
            public SkillEntry[] skills;
        }

        [Serializable]
        private class SkillEntry
        {
            public string name;
            public string directory;
        }
    }
}
