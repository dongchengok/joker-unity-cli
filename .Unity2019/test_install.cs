using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

var projectRoot = Path.GetDirectoryName(Application.dataPath);
var skillsSubDir = "Editor/ClaudeIntegration/Skills";

var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
var content = File.ReadAllText(manifestPath);
var match = Regex.Match(content, @"""com\.joker\.unity-cli""\s*:\s*""([^""]+)""");
var refValue = match.Groups[1].Value;

var rel = refValue.Substring(5);
var resolved = Path.GetFullPath(Path.Combine(Path.Combine(projectRoot, "Packages"), rel));
var sourceRoot = Path.Combine(resolved, skillsSubDir);

var sourceManifest = Path.ReadAllText(Path.Combine(sourceRoot, "skills-manifest.json"));
var targetSkillsDir = Path.Combine(projectRoot, ".claude", "skills");

$"sourceRoot={sourceRoot}, exists={Directory.Exists(sourceRoot)}, manifestContent={sourceManifest}, targetExists={Directory.Exists(targetSkillsDir)}"
