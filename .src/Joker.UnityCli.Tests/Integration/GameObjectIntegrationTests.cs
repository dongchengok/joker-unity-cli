using FluentAssertions;
using Joker.UnityCli.Services;
using Xunit;
using Xunit.Sdk;

namespace Joker.UnityCli.Tests.Integration;

[Collection("UnityIntegration")]
public class GameObjectIntegrationTests : UnityIntegrationTestBase
{
    private readonly ExecService _exec = new();

    [SkippableFact]
    public void SkipIfUnityNotRunningTest()
    {
        SkipIfUnityNotRunning();
    }

    // === Script Mode Tests ===

    [SkippableFact]
    public async Task ScriptMode_CreateGameObject_Succeeds()
    {
        SkipIfUnityNotRunning();
        var code = @"var go = new UnityEngine.GameObject(""test_go"");
UnityEngine.Object.DestroyImmediate(go);
""created""";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("created");
    }

    [SkippableFact]
    public async Task ScriptMode_FindGameObject_ByName_Succeeds()
    {
        SkipIfUnityNotRunning();
        var make = @"var go = new UnityEngine.GameObject(""findable_go"");
go.name";
        var result = await _exec.ExecuteAsync(ProjectPath, make, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("findable_go");
    }

    [SkippableFact]
    public async Task ScriptMode_DestroyGameObject_Succeeds()
    {
        SkipIfUnityNotRunning();
        var code = @"var go = new UnityEngine.GameObject(""to_destroy"");
UnityEngine.Object.DestroyImmediate(go);
go == null ? ""destroyed"" : ""alive""";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("destroyed");
    }

    [SkippableFact]
    public async Task ScriptMode_ModifyTransform_Position_Succeeds()
    {
        SkipIfUnityNotRunning();
        var code = @"var go = new UnityEngine.GameObject(""position_test"");
go.transform.position = new UnityEngine.Vector3(1, 2, 3);
var p = go.transform.position;
UnityEngine.Object.DestroyImmediate(go);
$""{p.x},{p.y},{p.z}""";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("1,2,3");
    }

    [SkippableFact]
    public async Task ScriptMode_ModifyTransform_RotationScale_Succeeds()
    {
        SkipIfUnityNotRunning();
        var code = @"var go = new UnityEngine.GameObject(""rs_test"");
go.transform.localScale = new UnityEngine.Vector3(2, 2, 2);
var s = go.transform.localScale;
UnityEngine.Object.DestroyImmediate(go);
$""{s.x},{s.y},{s.z}""";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("2,2,2");
    }

    [SkippableFact]
    public async Task ScriptMode_AddComponent_BoxCollider_Succeeds()
    {
        SkipIfUnityNotRunning();
        var code = @"var go = new UnityEngine.GameObject(""collider_test"", typeof(UnityEngine.BoxCollider));
var bc = go.GetComponent<UnityEngine.BoxCollider>();
UnityEngine.Object.DestroyImmediate(go);
bc != null ? ""has_collider"" : ""no_collider""";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("has_collider");
    }

    [SkippableFact]
    public async Task ScriptMode_InstantiateAndDestroy_Succeeds()
    {
        SkipIfUnityNotRunning();
        var code = @"var go = new UnityEngine.GameObject(""original"");
var clone = UnityEngine.Object.Instantiate(go);
var cloneName = clone.name;
UnityEngine.Object.DestroyImmediate(go);
UnityEngine.Object.DestroyImmediate(clone);
cloneName";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Contain("original");
    }

    // === Compile Mode Tests ===

    [SkippableFact]
    public async Task CompileMode_CreateGameObjectHierarchy_Succeeds()
    {
        SkipIfUnityNotRunning();
        var code = @"
using UnityEngine;
public class HierarchyTest
{
    public static string Execute()
    {
        var parent = new GameObject(""parent_h"");
        var child = new GameObject(""child_h"");
        child.transform.parent = parent.transform;
        int count = parent.transform.childCount;
        Object.DestroyImmediate(parent);
        return count == 1 ? ""has_child"" : ""no_child"";
    }
}";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("has_child");
    }

    [SkippableFact]
    public async Task CompileMode_BatchModifyComponents_Succeeds()
    {
        SkipIfUnityNotRunning();
        var code = @"
using UnityEngine;
using System.Linq;
public class BatchTest
{
    public static string Execute()
    {
        for (int i = 0; i < 5; i++)
        {
            var go = new GameObject($""batch_{i}"");
            go.transform.position = new Vector3(i, 0, 0);
        }
        var found = Object.FindObjectsOfType<GameObject>().Length;
        return found > 0 ? ""found"" : ""not_found"";
    }
}";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("found");
    }

    [SkippableFact]
    public async Task CompileMode_FindObjectsOfType_Succeeds()
    {
        SkipIfUnityNotRunning();
        var code = @"
using UnityEngine;
public class FindTest
{
    public static string Execute()
    {
        var go = new GameObject(""find_target"");
        var all = Object.FindObjectsOfType<GameObject>();
        Object.DestroyImmediate(go);
        return all.Length > 0 ? ""found_some"" : ""found_none"";
    }
}";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("found_some");
    }
}
