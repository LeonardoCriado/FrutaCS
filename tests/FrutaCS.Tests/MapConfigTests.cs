using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using GdUnit4;

namespace FrutaCS.Tests;

/// <summary>
/// Task 8 measurement cross-check + scene shape. The authoritative numbers
/// live in src/maps/MapConfig.cs; this test parses that source (single
/// source of truth — Resources cannot be instantiated under dotnet test)
/// and verifies the layout against docs/reference/mediciones.md within
/// ±10%: poolday CT-T centroid 962u, enemy min/mean/max 635/1047/1436u,
/// clear sightline 1249u (eye-to-eye over enemy spawn pairs, cover
/// footprints as blockers); same-team spacing vs iceworld 126u. The scene
/// shape half parses src/maps/fy_pileta.tscn and pins markers, pickups,
/// nav region, lighting and glue wiring (repo text-check pattern).
/// </summary>
[TestSuite]
public class MapConfigTests
{
    private const float Tol = 0.10f;

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FrutaCS.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir == null)
            throw new InvalidOperationException("repo root (FrutaCS.sln) not found");
        return dir;
    }

    private readonly struct Vec
    {
        public readonly float X, Y, Z;
        public Vec(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    private static float Dist(Vec a, Vec b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static List<Vec> ParseVectors(string src, string section, int count)
    {
        int from = src.IndexOf(section, StringComparison.Ordinal);
        Assertions.AssertThat(from >= 0).IsTrue();
        var matches = Regex.Matches(src.Substring(from),
            @"new Vector3\(\s*(-?[\d.]+)f?,\s*(-?[\d.]+)f?,\s*(-?[\d.]+)f?\s*\)");
        var list = new List<Vec>();
        // Stop at the next section header to avoid bleeding into later arrays.
        string[] nextSections = { "SpawnsT", "MixCT", "MixT", "Pickups", "CoverSpots", "CoverSizes", "PoolRectMin", "IslandMin", "BaseLoadoutIds", "BuyZones" };
        int cut = int.MaxValue;
        foreach (string s in nextSections)
        {
            if (s == section)
                continue;
            int i = src.IndexOf(s, from + section.Length, StringComparison.Ordinal);
            if (i > 0)
                cut = Math.Min(cut, i - from);
        }
        foreach (Match m in matches)
        {
            if (m.Index > cut)
                break;
            list.Add(new Vec(
                float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)));
            if (list.Count == count)
                break;
        }
        return list;
    }

    private static string MapConfigSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "maps", "MapConfig.cs"));

    private static void Within(string what, float value, float target)
    {
        float rel = Math.Abs(value - target) / target;
        Assertions.AssertThat(rel <= Tol + 1e-6f).IsTrue();
    }

    [TestCase]
    public void SpawnCountsAndSymmetry()
    {
        string src = MapConfigSource();
        var ct = ParseVectors(src, "SpawnsCT", 7);
        var t = ParseVectors(src, "SpawnsT", 7);
        Assertions.AssertThat(ct.Count).IsEqual(7);
        Assertions.AssertThat(t.Count).IsEqual(7);
        // T is the point mirror of CT (symmetric map).
        for (int i = 0; i < 7; i++)
        {
            Assertions.AssertThat(Math.Abs(t[i].X + ct[i].X) < 0.5f).IsTrue();
            Assertions.AssertThat(Math.Abs(t[i].Z + ct[i].Z) < 0.5f).IsTrue();
            Assertions.AssertThat(ct[i].Y).IsEqual(0f);
        }
        // Inside the 2000x2000 arena, on the deck (spawns never in the pit).
        foreach (Vec v in ct)
        {
            Assertions.AssertThat(Math.Abs(v.X) <= 1000f).IsTrue();
            Assertions.AssertThat(Math.Abs(v.Z) <= 1000f).IsTrue();
        }
    }

    [TestCase]
    public void SpawnDistancesMatchReference()
    {
        string src = MapConfigSource();
        var ct = ParseVectors(src, "SpawnsCT", 7);
        var t = ParseVectors(src, "SpawnsT", 7);
        float cx = 0f, cz = 0f;
        foreach (Vec v in ct)
        {
            cx += v.X;
            cz += v.Z;
        }
        float centroid = 2f * MathF.Sqrt((cx / 7f) * (cx / 7f) + (cz / 7f) * (cz / 7f));
        float min = float.MaxValue, max = 0f, sum = 0f;
        int n = 0;
        foreach (Vec a in ct)
            foreach (Vec b in t)
            {
                float d = Dist(a, b);
                min = Math.Min(min, d);
                max = Math.Max(max, d);
                sum += d;
                n++;
            }
        Within("centroid vs poolday 962", centroid, 962f);
        Within("enemy min vs poolday 635", min, 635f);
        Within("enemy mean vs poolday 1047", sum / n, 1047f);
        Within("enemy max vs poolday 1436", max, 1436f);
        float same = float.MaxValue;
        for (int i = 0; i < 7; i++)
            for (int j = 0; j < 7; j++)
                if (i != j)
                    same = Math.Min(same, Dist(ct[i], ct[j]));
        Within("same-team nearest vs iceworld 126", same, 126f);
    }

    [TestCase]
    public void LongestSightlineMatchesReference()
    {
        string src = MapConfigSource();
        var ct = ParseVectors(src, "SpawnsCT", 7);
        var t = ParseVectors(src, "SpawnsT", 7);
        var spots = ParseVectors(src, "CoverSpots", 7);
        var sizes = ParseVectors(src, "CoverSizes", 7);
        Assertions.AssertThat(spots.Count).IsEqual(7);
        Assertions.AssertThat(sizes.Count).IsEqual(7);
        foreach (Vec s in sizes)
            Assertions.AssertThat(s.Y).IsEqual(40f); // Spec §7: jumpable 40u covers.
        float best = 0f;
        foreach (Vec a in ct)
            foreach (Vec b in t)
            {
                bool blocked = false;
                for (int i = 0; i < 7 && !blocked; i++)
                {
                    float hx = sizes[i].X / 2f, hz = sizes[i].Z / 2f;
                    blocked = SegmentRect(a.X, a.Z, b.X, b.Z,
                        spots[i].X - hx, spots[i].Z - hz, spots[i].X + hx, spots[i].Z + hz);
                }
                if (!blocked)
                    best = Math.Max(best, Dist(a, b));
            }
        Within("max clear sightline vs poolday 1249", best, 1249f);
    }

    private static bool SegmentRect(float ax, float az, float bx, float bz,
        float x0, float z0, float x1, float z1)
    {
        float dx = bx - ax, dz = bz - az;
        float tmin = 0f, tmax = 1f;
        float[] p = { ax, az }, d = { dx, dz }, mn = { x0, z0 }, mx = { x1, z1 };
        for (int i = 0; i < 2; i++)
        {
            if (Math.Abs(d[i]) < 1e-9f)
            {
                if (p[i] < mn[i] || p[i] > mx[i])
                    return false;
            }
            else
            {
                float u1 = (mn[i] - p[i]) / d[i], u2 = (mx[i] - p[i]) / d[i];
                tmin = Math.Max(tmin, Math.Min(u1, u2));
                tmax = Math.Min(tmax, Math.Max(u1, u2));
                if (tmin > tmax)
                    return false;
            }
        }
        return true;
    }

    [TestCase]
    public void PickupLoadoutAndMix()
    {
        string src = MapConfigSource();
        var pickups = ParsePickups(src);
        // Spec §7: AK x2 / M4 x2 / AWP x1, mid + sides.
        Assertions.AssertThat(pickups.Count).IsEqual(5);
        int ak = 0, m4 = 0, awp = 0;
        foreach ((string id, Vec pos) in pickups)
        {
            if (id == "ak47")
            {
                ak++;
                Assertions.AssertThat(Math.Abs(pos.X) <= 200f && Math.Abs(pos.Z) <= 200f).IsTrue();
            }
            else if (id == "m4a1")
            {
                m4++;
                Assertions.AssertThat(Math.Abs(pos.X) >= 400f).IsTrue();
            }
            else if (id == "awp")
            {
                awp++;
                Assertions.AssertThat(pos.Y < 0f).IsTrue(); // Basin-floor contest point.
            }
            else
            {
                Assertions.AssertThat(false).IsTrue();
            }
        }
        Assertions.AssertThat(ak).IsEqual(2);
        Assertions.AssertThat(m4).IsEqual(2);
        Assertions.AssertThat(awp).IsEqual(1);
        // Base loadout + no buy zones on map 1.
        Assertions.AssertThat(src.Contains("\"knife\"") && src.Contains("\"deagle\"")).IsTrue();
        Assertions.AssertThat(src.Contains("new Vector3[0]")).IsTrue();
        // Per-spawn difficulty mix present on both sides.
        Assertions.AssertThat(CountInit(src, "BotDifficulty[] MixCT", "BotDifficulty[] MixT", "new BotDifficulty {")).IsEqual(7);
        Assertions.AssertThat(CountInit(src, "BotDifficulty[] MixT", "PickupEntry[] Pickups", "new BotDifficulty {")).IsEqual(7);
    }

    private static List<(string Id, Vec Pos)> ParsePickups(string src)
    {
        var pickups = new List<(string Id, Vec Pos)>();
        foreach (Match m in Regex.Matches(src,
            @"new PickupEntry\s*\{\s*WeaponId\s*=\s*""(\w+)""\s*,\s*Position\s*=\s*new Vector3\(\s*(-?[\d.]+)f?,\s*(-?[\d.]+)f?,\s*(-?[\d.]+)f?\s*\)"))
        {
            pickups.Add((m.Groups[1].Value, new Vec(
                float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture))));
        }
        return pickups;
    }

    private static int CountInit(string src, string from, string to, string token)
    {
        int a = src.IndexOf(from, StringComparison.Ordinal);
        int b = src.IndexOf(to, a + from.Length, StringComparison.Ordinal);
        string body = src.Substring(a, b - a);
        int n = 0, i = 0;
        while ((i = body.IndexOf(token, i, StringComparison.Ordinal)) >= 0)
        {
            n++;
            i += token.Length;
        }
        return n;
    }

    [TestCase]
    public void NavMeshCarvedLikeConfig()
    {
        // The baked navmesh (fy_pileta_nav.tres, built by MakeNav from
        // MapConfig) must be a carved plane, not a flat quad: triangles
        // exist, none sits inside the island/cover holes, and every spawn
        // stands on mesh (outside holes), so agents route around solids.
        string src = MapConfigSource();
        var spots = ParseVectors(src, "CoverSpots", 7);
        var sizes = ParseVectors(src, "CoverSizes", 7);
        var islMin = ParseVectors(src, "IslandMin", 1);
        var islMax = ParseVectors(src, "IslandMax", 1);
        Assertions.AssertThat(islMin.Count).IsEqual(1);
        Assertions.AssertThat(islMax.Count).IsEqual(1);
        var holes = new List<float[]>
        {
            new float[] { islMin[0].X - 40f, islMin[0].Z - 40f, islMax[0].X + 40f, islMax[0].Z + 40f },
        };
        for (int i = 0; i < 7; i++)
        {
            holes.Add(new float[]
            {
                spots[i].X - sizes[i].X / 2f - 40f, spots[i].Z - sizes[i].Z / 2f - 40f,
                spots[i].X + sizes[i].X / 2f + 40f, spots[i].Z + sizes[i].Z / 2f + 40f,
            });
        }
        string tres = File.ReadAllText(Path.Combine(RepoRoot(), "src", "maps", "fy_pileta_nav.tres"));
        Assertions.AssertThat(tres.Contains("type=\"NavigationMesh\"")).IsTrue();
        var verts = new List<float[]>();
        Match vm = Regex.Match(tres, @"vertices\s*=\s*PackedVector3Array\(([^)]+)\)");
        Assertions.AssertThat(vm.Success).IsTrue();
        string[] nums = vm.Groups[1].Value.Split(',');
        Assertions.AssertThat(nums.Length % 3).IsEqual(0);
        Assertions.AssertThat(nums.Length / 3 > 4).IsTrue(); // Carved, not a flat quad.
        for (int i = 0; i < nums.Length; i += 3)
        {
            verts.Add(new float[]
            {
                float.Parse(nums[i], CultureInfo.InvariantCulture),
                float.Parse(nums[i + 1], CultureInfo.InvariantCulture),
                float.Parse(nums[i + 2], CultureInfo.InvariantCulture),
            });
        }
        int tris = 0;
        foreach (Match pm in Regex.Matches(tres, @"PackedInt32Array\(([\d,\s]+)\)"))
        {
            string[] idx = pm.Groups[1].Value.Split(',');
            Assertions.AssertThat(idx.Length).IsEqual(3);
            float cx = 0f, cz = 0f;
            foreach (string s in idx)
            {
                int vi = int.Parse(s.Trim(), CultureInfo.InvariantCulture);
                cx += verts[vi][0];
                cz += verts[vi][2];
            }
            cx /= 3f;
            cz /= 3f;
            tris++;
            foreach (float[] h in holes)
                Assertions.AssertThat(cx > h[0] && cx < h[2] && cz > h[1] && cz < h[3]).IsFalse();
        }
        Assertions.AssertThat(tris > 2).IsTrue();
        // Spawns on mesh: inside the arena, outside every hole.
        var ct = ParseVectors(src, "SpawnsCT", 7);
        var t = ParseVectors(src, "SpawnsT", 7);
        ct.AddRange(t);
        foreach (Vec v in ct)
        {
            Assertions.AssertThat(Math.Abs(v.X) <= 1000f && Math.Abs(v.Z) <= 1000f).IsTrue();
            foreach (float[] h in holes)
                Assertions.AssertThat(v.X > h[0] && v.X < h[2] && v.Z > h[1] && v.Z < h[3]).IsFalse();
        }
        // Pickups routable: every gun sits outside the holes, so seekers
        // get complete agent paths instead of dithering on hole edges.
        foreach ((string id, Vec pos) in ParsePickups(src))
        {
            foreach (float[] h in holes)
                Assertions.AssertThat(pos.X > h[0] && pos.X < h[2] && pos.Z > h[1] && pos.Z < h[3]).IsFalse();
        }
    }

    [TestCase]
    public void MapSceneShape()
    {
        string tscn = File.ReadAllText(Path.Combine(RepoRoot(), "src", "maps", "fy_pileta.tscn"));
        // Navigation (baked mesh artifact), light, glue wiring.
        Assertions.AssertThat(tscn.Contains("NavigationRegion3D")).IsTrue();
        Assertions.AssertThat(tscn.Contains("res://src/maps/fy_pileta_nav.tres")).IsTrue();
        Assertions.AssertThat(tscn.Contains("WorldEnvironment")).IsTrue();
        Assertions.AssertThat(tscn.Contains("DirectionalLight3D")).IsTrue();
        Assertions.AssertThat(tscn.Contains("res://src/game/MatchManager.cs")).IsTrue();
        Assertions.AssertThat(tscn.Contains("res://src/player/PlayerBody.tscn")).IsTrue();
        Assertions.AssertThat(tscn.Contains("res://src/bots/Bot.tscn")).IsTrue();
        Assertions.AssertThat(tscn.Contains("res://src/maps/WeaponPickup.cs")).IsTrue();
        Assertions.AssertThat(tscn.Contains("res://src/maps/MapConfig.cs")).IsTrue();
        // Markers + pickups.
        Assertions.AssertThat(Regex.Matches(tscn, @"CTSpawn\d").Count).IsEqual(7);
        Assertions.AssertThat(Regex.Matches(tscn, @"\bTSpawn\d").Count).IsEqual(7);
        Assertions.AssertThat(Regex.Matches(tscn, @"WeaponId\s*=").Count).IsEqual(5);
        // Geometry classes: jumpable covers + tall walls.
        Assertions.AssertThat(tscn.Contains("Vector3(128, 40, 128)")).IsTrue();
        Assertions.AssertThat(tscn.Contains("256")).IsTrue();
    }

    [TestCase]
    public void SceneMarkersMatchConfig()
    {
        // Anti-drift: spawn/pickup marker transforms in the scene equal the
        // MapConfig arrays (1u tolerance; covers compare horizontal only —
        // scene centers ride at half height, config spots at the surface).
        string src = MapConfigSource();
        var ct = ParseVectors(src, "SpawnsCT", 7);
        var t = ParseVectors(src, "SpawnsT", 7);
        var spots = ParseVectors(src, "CoverSpots", 7);
        string tscn = File.ReadAllText(Path.Combine(RepoRoot(), "src", "maps", "fy_pileta.tscn"));
        var nodes = ParseSceneNodes(tscn);
        for (int i = 0; i < 7; i++)
        {
            Assertions.AssertThat(nodes.ContainsKey("CTSpawn" + i)).IsTrue();
            Assertions.AssertThat(nodes.ContainsKey("TSpawn" + i)).IsTrue();
            Assertions.AssertThat(Dist(nodes["CTSpawn" + i], ct[i]) < 1f).IsTrue();
            Assertions.AssertThat(Dist(nodes["TSpawn" + i], t[i]) < 1f).IsTrue();
            Assertions.AssertThat(nodes.ContainsKey("Cover" + i)).IsTrue();
            Vec c = nodes["Cover" + i];
            Assertions.AssertThat(Math.Abs(c.X - spots[i].X) < 1f && Math.Abs(c.Z - spots[i].Z) < 1f).IsTrue();
        }
    }

    private static Dictionary<string, Vec> ParseSceneNodes(string tscn)
    {
        var nodes = new Dictionary<string, Vec>();
        string current = null;
        foreach (string raw in tscn.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("[node ", StringComparison.Ordinal))
            {
                current = null;
                Match nm = Regex.Match(line, @"name=""([^""]+)""");
                if (nm.Success)
                    current = nm.Groups[1].Value;
            }
            else if (current != null && line.StartsWith("transform", StringComparison.Ordinal))
            {
                Match tm = Regex.Match(line,
                    @"Transform3D\([^,]+,[^,]+,[^,]+,[^,]+,[^,]+,[^,]+,[^,]+,[^,]+,[^,]+,\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)");
                if (tm.Success)
                {
                    nodes[current] = new Vec(
                        float.Parse(tm.Groups[1].Value, CultureInfo.InvariantCulture),
                        float.Parse(tm.Groups[2].Value, CultureInfo.InvariantCulture),
                        float.Parse(tm.Groups[3].Value, CultureInfo.InvariantCulture));
                    current = null;
                }
            }
        }
        return nodes;
    }
}
