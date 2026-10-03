using System;
using System.IO;
using System.Linq;
using GdUnit4;
using Godot;

using FrutaCS.Weapons;

namespace FrutaCS.Tests;

[TestSuite]
public class WeaponSimTests
{
    private static WeaponStats AkLikeStats() => new(
        "ak47",
        36f, 0f, 4f, 0.775f, 628, 30, 90,
        0.3151f, 3.091f, 6.8428f, 0.3151f,
        0.98f, 2, 64f, 0f,
        Enumerable.Range(1, 30).Select(i => new Vector2(i * 0.3f, -i * 0.05f)).ToArray()
    );

    private static string DataDir()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FrutaCS.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir == null)
            throw new InvalidOperationException("repo root (FrutaCS.sln) not found");
        return Path.Combine(dir, "data", "weapons");
    }

    private static string ReadTres(string weaponId)
        => File.ReadAllText(Path.Combine(DataDir(), weaponId + ".tres"));

    private static int CountRecoilEntries(string tres)
    {
        int start = tres.IndexOf("RecoilTable = PackedVector2Array(", StringComparison.Ordinal);
        Assertions.AssertThat(start >= 0).IsTrue();
        start += "RecoilTable = PackedVector2Array(".Length;
        int end = tres.IndexOf(')', start);
        string body = tres.Substring(start, end - start).Trim();
        if (body.Length == 0)
            return 0;
        return body.Split(',').Length / 2;
    }

    private static int ReadIntField(string tres, string field)
    {
        foreach (string line in tres.Split('\n'))
        {
            string t = line.Trim();
            if (t.StartsWith(field + " =", StringComparison.Ordinal))
                return int.Parse(t.Split('=')[1].Trim());
        }
        throw new InvalidOperationException($"field {field} not found");
    }

    private static float ReadFloatField(string tres, string field)
    {
        foreach (string line in tres.Split('\n'))
        {
            string t = line.Trim();
            if (t.StartsWith(field + " =", StringComparison.Ordinal))
                return float.Parse(t.Split('=')[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
        throw new InvalidOperationException($"field {field} not found");
    }

    [TestCase]
    public void RecoilTableLengthMatchesMag()
    {
        string ak = ReadTres("ak47");
        Assertions.AssertThat(CountRecoilEntries(ak)).IsEqual(30);
        Assertions.AssertThat(ReadIntField(ak, "MagSize")).IsEqual(30);
        foreach (string id in new[] { "m4a1", "awp", "deagle", "knife" })
        {
            string tres = ReadTres(id);
            Assertions.AssertThat(CountRecoilEntries(tres)).IsEqual(ReadIntField(tres, "MagSize"));
        }
    }

    [TestCase]
    public void TablesCarry16ReferenceValues()
    {
        Assertions.AssertThat(ReadFloatField(ReadTres("ak47"), "Damage")).IsEqual(36f);
        Assertions.AssertThat(ReadFloatField(ReadTres("m4a1"), "Damage")).IsEqual(32f);
        Assertions.AssertThat(ReadFloatField(ReadTres("awp"), "Damage")).IsEqual(115f);
        Assertions.AssertThat(ReadFloatField(ReadTres("deagle"), "Damage")).IsEqual(54f);
        Assertions.AssertThat(ReadFloatField(ReadTres("knife"), "Damage")).IsEqual(65f);
        Assertions.AssertThat(ReadIntField(ReadTres("awp"), "MagSize")).IsEqual(10);
        Assertions.AssertThat(ReadIntField(ReadTres("deagle"), "MagSize")).IsEqual(7);
        Assertions.AssertThat(ReadIntField(ReadTres("deagle"), "Rpm")).IsEqual(267);
    }

    [TestCase]
    public void PatternIsDeterministic()
    {
        var sim = new WeaponSim(AkLikeStats());
        Vector2 first = sim.RecoilOffset(7);
        Vector2 second = sim.RecoilOffset(7);
        Assertions.AssertThat(first == second).IsTrue();
        var other = new WeaponSim(AkLikeStats());
        Assertions.AssertThat(other.RecoilOffset(7) == first).IsTrue();
    }

    [TestCase]
    public void FirstBulletAccurateStanding()
    {
        var sim = new WeaponSim(AkLikeStats());
        Assertions.AssertThat(sim.SpreadDeg(Stance.Stand) < sim.SpreadDeg(Stance.Move)).IsTrue();
    }

    [TestCase]
    public void DamageFallsWithDistance()
    {
        var sim = new WeaponSim(AkLikeStats());
        Assertions.AssertThat(sim.DamageAt(100f, false, 0f) > sim.DamageAt(2000f, false, 0f)).IsTrue();
    }

    [TestCase]
    public void WallbangThinVsThick()
    {
        var sim = new WeaponSim(AkLikeStats());
        Assertions.AssertThat(sim.DamageAt(100f, true, 40f) > 0).IsTrue();
        Assertions.AssertThat(sim.DamageAt(100f, true, 120f)).IsEqual(0);
    }

    [TestCase]
    public void FpsIndependent()
    {
        var sim60 = new WeaponSim(AkLikeStats());
        for (int i = 0; i < 60; i++)
            sim60.Tick(1f / 60f);
        var sim120 = new WeaponSim(AkLikeStats());
        for (int i = 0; i < 120; i++)
            sim120.Tick(1f / 120f);
        Assertions.AssertThat(sim60.CurrentBulletIndex).IsEqual(10);
        Assertions.AssertThat(sim120.CurrentBulletIndex).IsEqual(sim60.CurrentBulletIndex);
        Assertions.AssertThat(sim120.CurrentRecoilOffset == sim60.CurrentRecoilOffset).IsTrue();
    }
}
