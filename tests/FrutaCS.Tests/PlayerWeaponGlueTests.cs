using System;
using System.Globalization;
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

    private static Vector2[] ReadRecoilTable(string weaponId)
    {
        string tres = ReadRepo(Path.Combine("data", "weapons", weaponId + ".tres"));
        const string marker = "RecoilTable = PackedVector2Array(";
        int start = tres.IndexOf(marker, StringComparison.Ordinal);
        Assertions.AssertThat(start >= 0).IsTrue();
        start += marker.Length;
        int end = tres.IndexOf(')', start);
        string body = tres.Substring(start, end - start).Trim();
        string[] parts = body.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var table = new Vector2[parts.Length / 2];
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = new Vector2(
                float.Parse(parts[2 * i], CultureInfo.InvariantCulture),
                float.Parse(parts[2 * i + 1], CultureInfo.InvariantCulture));
        }
        return table;
    }

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
        var run = new MovementInput(Vector3.Forward, false, false);
        for (int i = 0; i < 60; i++)
            sim.Tick(in run, in p, Delta);
        float before = new Vector3(sim.Velocity.X, 0f, sim.Velocity.Z).Length();
        // Jump while already holding a perpendicular strafe wish: the old
        // code froze horizontal velocity for this tick.
        var jumpStrafe = new MovementInput(Vector3.Right, true, false);
        sim.Tick(in jumpStrafe, in p, Delta);
        float after = new Vector3(sim.Velocity.X, 0f, sim.Velocity.Z).Length();
        Assertions.AssertThat(sim.IsOnFloor).IsFalse();
        Assertions.AssertThat(after > before).IsTrue();
    }

    [TestCase]
    public void SyncFromEngineAdoptsFloorAndFriction()
    {
        var sim = new MovementSim();
        var p = MovementParams.Default;
        var jump = new MovementInput(Vector3.Zero, true, false);
        sim.Tick(in jump, in p, Delta);
        Assertions.AssertThat(sim.IsOnFloor).IsFalse();
        // Engine reports landed with clipped velocity: sim follows instead
        // of integrating gravity while landed with Air stance.
        sim.SyncFromEngine(new Vector3(100f, 0f, 0f), true);
        Assertions.AssertThat(sim.IsOnFloor).IsTrue();
        Assertions.AssertThat(sim.Velocity == new Vector3(100f, 0f, 0f)).IsTrue();
        var idle = new MovementInput(Vector3.Zero, false, false);
        sim.Tick(in idle, in p, Delta);
        float speed = new Vector3(sim.Velocity.X, 0f, sim.Velocity.Z).Length();
        Assertions.AssertThat(sim.IsOnFloor).IsTrue();
        Assertions.AssertThat(sim.Velocity.Y == 0f).IsTrue();
        Assertions.AssertThat(speed < 100f).IsTrue(); // Friction reapplied.
    }

    [TestCase]
    public void SyncFromEngineAdoptsClippedVelocity()
    {
        var sim = new MovementSim();
        var p = MovementParams.Default;
        var run = new MovementInput(Vector3.Forward, false, false);
        for (int i = 0; i < 120; i++)
            sim.Tick(in run, in p, Delta);
        // Engine clipped the wall hit to zero: the sim must not retain 250
        // and push through the wall on the next tick.
        sim.SyncFromEngine(Vector3.Zero, true);
        Assertions.AssertThat(sim.Velocity == Vector3.Zero).IsTrue();
        sim.Tick(in run, in p, Delta);
        float speed = new Vector3(sim.Velocity.X, 0f, sim.Velocity.Z).Length();
        Assertions.AssertThat(speed > 0f && speed < 50f).IsTrue();
    }

    [TestCase]
    public void AkPatternMatchesTask4Dump()
    {
        // Port of the wall-pattern check from tools/verify_task5.gd (deleted
        // per the C#-only constraint): the committed AK table keeps the Task 4
        // dump shape — climb 1-6, left 7-10, long right 11-19, settle 20-30.
        Vector2[] table = ReadRecoilTable("ak47");
        Assertions.AssertThat(table.Length).IsEqual(30);
        Assertions.AssertThat(table[0] == new Vector2(0.55f, 0f)).IsTrue();
        Assertions.AssertThat(table[5] == new Vector2(4f, -0.45f)).IsTrue();
        Assertions.AssertThat(table[29] == new Vector2(9.3f, 0.85f)).IsTrue();
        for (int i = 1; i < 6; i++)
            Assertions.AssertThat(table[i].X > table[i - 1].X).IsTrue();
        for (int i = 1; i < 30; i++)
            Assertions.AssertThat(table[i].X - table[i - 1].X <= 0.75f).IsTrue();
        float minYaw = table[0].Y;
        int minIdx = 0;
        float maxYaw = table[0].Y;
        int maxIdx = 0;
        for (int i = 1; i < 30; i++)
        {
            if (table[i].Y < minYaw)
            {
                minYaw = table[i].Y;
                minIdx = i;
            }
            if (table[i].Y > maxYaw)
            {
                maxYaw = table[i].Y;
                maxIdx = i;
            }
        }
        Assertions.AssertThat(minYaw == -1.95f && minIdx == 9).IsTrue();
        Assertions.AssertThat(maxYaw == 1.8f && maxIdx == 18).IsTrue();
        for (int i = 19; i < 30; i++)
            Assertions.AssertThat(table[i].X > 8.9f && table[i].X < 9.35f).IsTrue();
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
