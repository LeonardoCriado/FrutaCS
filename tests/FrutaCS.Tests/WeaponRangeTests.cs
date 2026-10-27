using System;
using System.IO;
using GdUnit4;

using FrutaCS.Weapons;

namespace FrutaCS.Tests;

/// <summary>
/// Weapon range (CS2-workshop-style test environment) shape checks. Scene
/// and glue are parsed as text, like the other scene suites: C# scene
/// nodes cannot be instantiated under plain `dotnet test`.
/// </summary>
[TestSuite]
public class WeaponRangeTests
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
    public void RangeSceneHostsPlayerGeometryAndScript()
    {
        string tscn = ReadRepo(Path.Combine("scenes", "WeaponRange.tscn"));
        // Script + player, same wiring as fy_pileta/TestRange (no MatchManager).
        Assertions.AssertThat(tscn.Contains("res://src/ranges/WeaponRange.cs")).IsTrue();
        Assertions.AssertThat(tscn.Contains("res://src/player/PlayerBody.tscn")).IsTrue();
        // Static geometry: floor, 4 shell walls, thin + thick wallbang walls.
        int statics = tscn.Split("type=\"StaticBody3D\"").Length - 1;
        Assertions.AssertThat(statics >= 7).IsTrue();
        Assertions.AssertThat(tscn.Contains("Vector3(3000, 10, 3000)")).IsTrue();
    }

    [TestCase]
    public void RangeGlueOffersEveryWeaponAndFrozenDummies()
    {
        string cs = ReadRepo(Path.Combine("src", "ranges", "WeaponRange.cs"));
        // Dummies are Bot capsules with the brain frozen (the range owns them).
        Assertions.AssertThat(cs.Contains("res://src/bots/Bot.tscn")).IsTrue();
        Assertions.AssertThat(cs.Contains("SetPhysicsProcess(false)")).IsTrue();
        Assertions.AssertThat(cs.Contains("bot.Team = 1")).IsTrue();
    }

    [TestCase]
    public void PickerCoversEveryWeaponId()
    {
        // Anti-drift: a new weapon in WeaponData.AllIds must reach the picker.
        string cs = ReadRepo(Path.Combine("src", "ranges", "WeaponRange.cs"));
        foreach (string id in WeaponData.AllIds)
            Assertions.AssertThat(cs.Contains("\"" + id + "\"")).IsTrue();
    }

    [TestCase]
    public void PlayerBodyYieldsWeaponSwitchingToScene()
    {
        string cs = ReadRepo(Path.Combine("src", "player", "PlayerBody.cs"));
        Assertions.AssertThat(cs.Contains("[Export] public bool SceneOwnsWeaponSwitching")).IsTrue();
        // The guard must come before the 1/2/3/Q polling, not after it.
        int guard = cs.IndexOf("if (SceneOwnsWeaponSwitching)", System.StringComparison.Ordinal);
        int polling = cs.IndexOf("InputActions.WeaponSlot1)", System.StringComparison.Ordinal);
        Assertions.AssertThat(guard >= 0 && guard < polling).IsTrue();
    }

    [TestCase]
    public void MainMenuOpensTheRange()
    {
        string tscn = ReadRepo(Path.Combine("src", "ui", "MainMenu.tscn"));
        string cs = ReadRepo(Path.Combine("src", "ui", "MainMenu.cs"));
        Assertions.AssertThat(tscn.Contains("unique_name_in_owner = true")).IsTrue();
        Assertions.AssertThat(tscn.Contains("name=\"RangeButton\"")).IsTrue();
        Assertions.AssertThat(cs.Contains("res://scenes/WeaponRange.tscn")).IsTrue();
        Assertions.AssertThat(cs.Contains("%RangeButton")).IsTrue();
    }
}
