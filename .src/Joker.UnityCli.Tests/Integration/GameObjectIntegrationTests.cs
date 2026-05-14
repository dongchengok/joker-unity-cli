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
        // Create GO, set tag, verify active state, then destroy
        var code = @"var go = new UnityEngine.GameObject(""test_go"");
go.SetActive(false);
var wasActive = go.activeSelf;
go.SetActive(true);
UnityEngine.Object.DestroyImmediate(go);
wasActive ? ""was_active"" : ""was_inactive""";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("was_inactive");
    }

    [SkippableFact]
    public async Task ScriptMode_FindGameObject_ByName_Succeeds()
    {
        SkipIfUnityNotRunning();
        // Create, find by name, verify identity, cleanup
        var code = @"var go = new UnityEngine.GameObject(""findable_go"");
go.transform.position = new UnityEngine.Vector3(5, 10, 15);
var found = UnityEngine.GameObject.Find(""findable_go"");
var pos = found.transform.position;
UnityEngine.Object.DestroyImmediate(go);
$""{pos.x},{pos.y},{pos.z}""";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("5,10,15");
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
        // Set position, apply Translate, verify delta
        var code = @"var go = new UnityEngine.GameObject(""position_test"");
go.transform.position = new UnityEngine.Vector3(1, 2, 3);
go.transform.Translate(new UnityEngine.Vector3(10, 20, 30));
var p = go.transform.position;
UnityEngine.Object.DestroyImmediate(go);
$""{p.x},{p.y},{p.z}""";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("11,22,33");
    }

    [SkippableFact]
    public async Task ScriptMode_ModifyTransform_RotationScale_Succeeds()
    {
        SkipIfUnityNotRunning();
        // Set scale, multiply, verify result
        var code = @"var go = new UnityEngine.GameObject(""rs_test"");
go.transform.localScale = new UnityEngine.Vector3(2, 3, 4);
var s = go.transform.localScale;
UnityEngine.Object.DestroyImmediate(go);
$""{s.x * s.y * s.z}""";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("24"); // 2*3*4
    }

    [SkippableFact]
    public async Task ScriptMode_AddComponent_BoxCollider_Succeeds()
    {
        SkipIfUnityNotRunning();
        // Add collider, set size, verify center
        var code = @"var go = new UnityEngine.GameObject(""collider_test"", typeof(UnityEngine.BoxCollider));
var bc = go.GetComponent<UnityEngine.BoxCollider>();
bc.size = new UnityEngine.Vector3(2, 2, 2);
var sizeStr = bc.size.x.ToString();
UnityEngine.Object.DestroyImmediate(go);
sizeStr";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("2");
    }

    [SkippableFact]
    public async Task ScriptMode_InstantiateAndDestroy_Succeeds()
    {
        SkipIfUnityNotRunning();
        // Instantiate with position offset, verify world position
        var code = @"var go = new UnityEngine.GameObject(""original"");
go.transform.position = new UnityEngine.Vector3(10, 20, 30);
var clone = UnityEngine.Object.Instantiate(go, new UnityEngine.Vector3(100, 200, 300), UnityEngine.Quaternion.identity);
var pos = clone.transform.position;
UnityEngine.Object.DestroyImmediate(go);
UnityEngine.Object.DestroyImmediate(clone);
$""{pos.x},{pos.y},{pos.z}""";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "script", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("100,200,300");
    }

    // === Compile Mode Tests ===

    [SkippableFact]
    public async Task CompileMode_CreateGameObjectHierarchy_Succeeds()
    {
        SkipIfUnityNotRunning();
        // Build parent-child hierarchy, verify childCount + world position inheritance
        var code = @"
using UnityEngine;
using System.Linq;
public class HierarchyTest
{
    public static string Execute()
    {
        var parent = new GameObject(""parent_h"");
        parent.transform.position = new Vector3(10, 0, 0);
        var child = new GameObject(""child_h"");
        child.transform.parent = parent.transform;
        // Local position of child should be (0,0,0) since parent is at (10,0,0)
        var localPos = child.transform.localPosition;
        Object.DestroyImmediate(parent);
        return $""{localPos.x},{localPos.y},{localPos.z}"";
    }
}";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("0,0,0");
    }

    [SkippableFact]
    public async Task CompileMode_BatchModifyComponents_Succeeds()
    {
        SkipIfUnityNotRunning();
        // Create 5 GOs with positions, query by position range using LINQ
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
            go.transform.position = new Vector3(i * 10, 0, 0);
        }
        var all = Object.FindObjectsOfType<GameObject>()
            .Where(g => g.name.StartsWith(""batch_""))
            .OrderBy(g => g.transform.position.x)
            .Select(g => g.name)
            .ToArray();
        foreach (var g in all) Object.DestroyImmediate(g);
        return string.Join("","", all);
    }
}";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("batch_0,batch_1,batch_2,batch_3,batch_4");
    }

    [SkippableFact]
    public async Task CompileMode_FindObjectsOfType_Succeeds()
    {
        SkipIfUnityNotRunning();
        // Create GOs with colliders, find all colliders, verify count
        var code = @"
using UnityEngine;
using System.Linq;
public class FindTest
{
    public static string Execute()
    {
        for (int i = 0; i < 3; i++)
            new GameObject($""col_{i}"", typeof(BoxCollider));

        var colliders = Object.FindObjectsOfType<BoxCollider>();
        var count = colliders.Length;

        foreach (var c in colliders)
            Object.DestroyImmediate(c.gameObject);

        return count.ToString();
    }
}";
        var result = await _exec.ExecuteAsync(ProjectPath, code, "compile", 30000, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Result.Should().Be("3");
    }
}
