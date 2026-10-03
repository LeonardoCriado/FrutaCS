using System;
using System.IO;
using System.Linq;
using GdUnit4;
using Godot;

using FrutaCS.Player;
using FrutaCS.Weapons;

namespace FrutaCS.Tests;

/// <summary>
/// Task 5 glue checks. Scene assertions parse the .tscn files as text
/// (same pattern as WeaponSimTests parsing .tres): C# scene nodes cannot
/// be instantiated under plain `dotnet test` (no engine runtime).
/// Sim-behavior tests cover the Task 5 carried fixes: jump-tick air
/// steering, knife secondary damage, staged wall penetration.
/// </summary>
[TestSuite]
public class PlayerWeaponGlueTests
{
    private const float Delta = 1f / 60f;

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FrutaCS.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir == null)
            throw new InvalidOperationException("repo root (FrutaCS.sln) not found");
        return dir;
    }

    private static string ReadRepo(string relative)
        => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    private static WeaponStats AkLikeStats() => new(
        "ak47",
        36f, 0f, 4f, 0.775f, 628, 30, 90,
        0.3151f, 3.091f, 6.8428f, 0.3151f,
        0.98f, 2, 64f, 0f,
        Enumerable.Range(1, 30).Select(i => new Vector2(i * 0.3f, -i * 0.05f)).ToArray()
    );

    private static WeaponStats KnifeLikeStats() => new(
        "knife",
        65f, 20f, 1f, 0.85f, 0, 0, -1,
        0f, 0f, 0f, 0f,
        1f, 0, 0f, 48f,
        Array.Empty<Vector2>()
    );

    [TestCase]
    public void PlayerBodySceneShape()
    {
        string tscn = ReadRepo(Path.Combine("src", "player", "PlayerBody.tscn"));
        Assertions.AssertThat(tscn.Contains("res://src/player/PlayerBody.cs")).IsTrue();
        Assertions.AssertThat(tscn.Contains("res://src/weapons/WeaponView.tscn")).IsTrue();
        Assertions.AssertThat(tscn.Contains("height = 72")).IsTrue();
        Assertions.AssertThat(tscn.Contains("radius = 16")).IsTrue();
        Assertions.AssertThat(tscn.Contains("0, 64, 0")).IsTrue();
        Assertions.AssertThat(tscn.Contains("res://data/weapons/ak47.tres")).IsTrue();
    }

    [TestCase]
    public void WeaponViewSceneSharesOneMaterial()
    {
        string tscn = ReadRepo(Path.Combine("src", "weapons", "WeaponView.tscn"));
        Assertions.AssertThat(tscn.Contains("res://src/weapons/WeaponSystem.cs")).IsTrue();
        int materials = tscn.Split("type=\"StandardMaterial3D\"").Length - 1;
        Assertions.AssertThat(materials).IsEqual(1);
        Assertions.AssertThat(tscn.Contains("FireSound")).IsTrue();
        Assertions.AssertThat(tscn.Contains("Muzzle")).IsTrue();
    }

    [TestCase]
    public void JumpTickSteersImmediately()
    {
        var sim = new MovementSim();
        var p = MovementParams.Default;
        var run = new MovementInput(Vector3.Forward, false);
        for (int i = 0; i < 60; i++)
            sim.Tick(in run, in p, Delta);
        float before = new Vector3(sim.Velocity.X, 0f, sim.Velocity.Z).Length();
        // Jump while already holding a perpendicular strafe wish: the old
        // code froze horizontal velocity for this tick.
        var jumpStrafe = new MovementInput(Vector3.Right, true);
        sim.Tick(in jumpStrafe, in p, Delta);
        float after = new Vector3(sim.Velocity.X, 0f, sim.Velocity.Z).Length();
        Assertions.AssertThat(sim.IsOnFloor).IsFalse();
        Assertions.AssertThat(after > before).IsTrue();
    }

    [TestCase]
    public void KnifeSecondaryUsesSecondaryDamage()
    {
        var sim = new WeaponSim(KnifeLikeStats());
        Assertions.AssertThat(sim.DamageAt(10f, false, 0f)).IsEqual(65);
        Assertions.AssertThat(sim.DamageAt(10f, false, 0f, true)).IsEqual(20);
        Assertions.AssertThat(sim.DamageAt(100f, false, 0f, true)).IsEqual(0);
        Assertions.AssertThat(sim.DamageAt(10f, true, 10f, true)).IsEqual(0);
    }

    [TestCase]
    public void StagedPenetrationDecaysPerWall()
    {
        var sim = new WeaponSim(AkLikeStats());
        int open = sim.DamageAt(500f, false, 0f);
        int one = sim.DamageAt(500f, 1, 40f);
        int two = sim.DamageAt(500f, 2, 40f);
        Assertions.AssertThat(open > one).IsTrue();
        Assertions.AssertThat(one > two).IsTrue();
        Assertions.AssertThat(two > 0).IsTrue();
        // AK has 2 stages: a third wall stops the bullet; thick walls gate.
        Assertions.AssertThat(sim.DamageAt(500f, 3, 40f)).IsEqual(0);
        Assertions.AssertThat(sim.DamageAt(500f, 1, 120f)).IsEqual(0);
        // Legacy single-wall overload keeps its exact behavior.
        Assertions.AssertThat(sim.DamageAt(500f, true, 40f)).IsEqual(one);
        Assertions.AssertThat(sim.DamageAt(500f, false, 0f)).IsEqual(open);
    }
}
