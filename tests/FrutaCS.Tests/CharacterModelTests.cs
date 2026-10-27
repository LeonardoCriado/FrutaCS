using System;
using System.IO;
using System.Linq;
using GdUnit4;
using Godot;

using FrutaCS.Weapons;

namespace FrutaCS.Tests;

/// <summary>
/// Humanoid bots + first-person holding hands. Shape checks parse the
/// scene/glue files as text (same pattern as the other scene suites);
/// the walk-cycle cadence is pure math and runs under plain .NET.
/// </summary>
[TestSuite]
public class CharacterModelTests
{
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

    [TestCase]
    public void BotBuildsHumanoidWithTeamColors()
    {
        string model = ReadRepo(Path.Combine("src", "bots", "BotModel.cs"));
        foreach (string part in new[]
        {
            "Pelvis", "Torso", "Yoke", "Head", "Cap", "Glasses",
            "Hip", "Leg", "Foot", "UpperArm", "ForeArm", "Glove",
        })
        {
            Assertions.AssertThat(model.Contains(part)).IsTrue();
        }
        // CT blue + T orange, matching the nameplates.
        Assertions.AssertThat(model.Contains("0.20f, 0.42f, 0.85f")).IsTrue();
        Assertions.AssertThat(model.Contains("0.88f, 0.38f, 0.18f")).IsTrue();
    }

    [TestCase]
    public void BotSceneBodyIsHumanoidContainer()
    {
        string tscn = ReadRepo(Path.Combine("src", "bots", "Bot.tscn"));
        // The visual capsule is gone; "Body" now hosts the procedural
        // humanoid. The collision capsule (radius 16, height 72) stays.
        Assertions.AssertThat(tscn.Contains("name=\"Body\" type=\"Node3D\"")).IsTrue();
        Assertions.AssertThat(tscn.Contains("CapsuleMesh")).IsFalse();
        Assertions.AssertThat(tscn.Contains("height = 72")).IsTrue();
        Assertions.AssertThat(tscn.Contains("radius = 16")).IsTrue();

        string glue = ReadRepo(Path.Combine("src", "bots", "Bot.cs"));
        Assertions.AssertThat(glue.Contains("BotModel.Build(")).IsTrue();
        Assertions.AssertThat(glue.Contains("_rig?.Animate(")).IsTrue();
        Assertions.AssertThat(glue.Contains("PaintTeamColor")).IsFalse();
    }

    [TestCase]
    public void WalkCycleMathIsPure()
    {
        // Cadence grows with speed and freezes while standing still.
        float dt = 1f / 60f;
        Assertions.AssertThat(FrutaCS.Bots.BotRig.NextPhase(0f, 250f, dt) > 0f).IsTrue();
        Assertions.AssertThat(FrutaCS.Bots.BotRig.NextPhase(0f, 250f, dt)
            > FrutaCS.Bots.BotRig.NextPhase(0f, 130f, dt)).IsTrue();
        Assertions.AssertThat(FrutaCS.Bots.BotRig.NextPhase(3.5f, 0f, dt)).IsEqual(3.5f);

        // Swing: zero standing and at phase 0, bounded by the peak.
        Assertions.AssertThat(FrutaCS.Bots.BotRig.SwingFor(0f, 250f)).IsEqual(0f);
        Assertions.AssertThat(FrutaCS.Bots.BotRig.SwingFor(1.234f, 0f)).IsEqual(0f);
        float peak = FrutaCS.Bots.BotRig.SwingFor(Mathf.Pi / 2f, 250f);
        Assertions.AssertThat(Mathf.Abs(peak - FrutaCS.Bots.BotModel.MaxSwingRad) < 0.001f).IsTrue();
        for (int i = 0; i < 32; i++)
        {
            float swing = FrutaCS.Bots.BotRig.SwingFor(i * 0.37f, 250f);
            Assertions.AssertThat(Mathf.Abs(swing) <= FrutaCS.Bots.BotModel.MaxSwingRad + 0.0001f).IsTrue();
        }
    }

    [TestCase]
    public void ViewmodelHandsCoverEveryWeapon()
    {
        string vm = ReadRepo(Path.Combine("src", "weapons", "WeaponViewmodel.cs"));
        Assertions.AssertThat(vm.Contains("AddArms(")).IsTrue();
        Assertions.AssertThat(vm.Contains("int team = 0")).IsTrue();
        foreach (string part in new[] { "Sleeve", "Wrist", "Glove" })
            Assertions.AssertThat(vm.Contains(part)).IsTrue();

        // Every weapon entry carries both hand anchors (5 Vector3 args:
        // position, rotation, muzzle, grip, support).
        foreach (string id in WeaponData.AllIds)
        {
            string marker = "[\"" + id + "\"] = new(";
            int start = vm.IndexOf(marker, StringComparison.Ordinal);
            Assertions.AssertThat(start >= 0).IsTrue();
            // Entry runs until the next dictionary key (or the dict close).
            int next = vm.IndexOf("[\"", start + marker.Length, StringComparison.Ordinal);
            int end = next >= 0 ? next : vm.IndexOf("};", start, StringComparison.Ordinal);
            int vectors = vm.Substring(start, end - start)
                .Split("new Vector3(").Length - 1;
            Assertions.AssertThat(vectors).IsEqual(5);
        }

        // WeaponSystem feeds the player's team so the sleeve matches.
        string glue = ReadRepo(Path.Combine("src", "weapons", "WeaponSystem.cs"));
        Assertions.AssertThat(glue.Contains("int team = _body != null ? _body.Team : 0;")).IsTrue();
        Assertions.AssertThat(glue.Contains("WeaponViewmodel.Build(_viewmodel, _muzzle, WeaponId, team)")).IsTrue();
    }
}
