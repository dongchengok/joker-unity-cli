using System.IO;
using UnityEditor;
using UnityEngine;

namespace Joker.UnityCli.Editor.ScriptExecution
{
    public class ScriptGlobals
    {
        public GameObject SelectedObject => Selection.activeGameObject;
        public string ProjectPath => Directory.GetParent(Application.dataPath).FullName;
    }
}
