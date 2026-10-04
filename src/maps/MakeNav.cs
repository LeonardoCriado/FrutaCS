using System.Collections.Generic;
using Godot;

namespace FrutaCS.Maps;

/// <summary>
/// MAP BUILD TOOL (not gameplay): bakes `fy_pileta_nav.tres` from
/// <see cref="MapConfig"/> (single source of truth) and saves it as text.
/// Re-run after ANY layout change (spawns don't matter; covers, island,
/// pool rect, ramps, pickups do):
///   godot --headless --path . res://src/maps/makenav.tscn
/// with Godot .NET 4.7.2 (mono build; the .tres format is versioned).
/// The bake is a heightfield plane (NOT flat): deck/bridge/island-top at
/// y=0, basin floor at −96, ramps sloped between, so queries resolve in
/// true 3D and the probe can verify descent routes. Rectangular holes are
/// cut around the island and every low cover (agent margin included):
/// bots steer AROUND solids via NavigationServer3D instead of pushing
/// into faces and grinding on friction. Grid edges include all ramp/floor
/// joints so no polygon straddles a height kink. Winding matches the
/// original flat bake (upward-facing); slopes tilt normals up to 40u.
/// </summary>
public partial class MakeNav : Node
{
    private const float MarginU = 40f;

    private static float HeightAt(MapConfig cfg, float x, float z)
    {
        // Deck default. At band overlaps (basin corners) the topmost
        // (least negative) formula wins: both ramp solids exist there and
        // bodies walk the higher surface.
        if (!(x >= -320f && x <= 320f && z >= -160f && z <= 160f))
            return 0f; // Deck.
        if (x >= -80f && x <= 80f && z >= -240f && z <= 240f)
            return 0f; // Bridge deck (walkable, over the basin).
        if (x >= -120f && x <= 120f && z >= -100f && z <= 100f)
            return 0f; // Island top (walkable from deck; holed anyway).
        float y = float.NegativeInfinity;
        bool ramp = false;
        if (x >= 206f && x <= 320f && z >= -160f && z <= 160f)
        {
            y = Mathf.Max(y, -96f * (320f - x) / 114f);
            ramp = true;
        }
        if (x >= -320f && x <= -206f && z >= -160f && z <= 160f)
        {
            y = Mathf.Max(y, -96f * (x + 320f) / 114f);
            ramp = true;
        }
        if (z >= 46f && z <= 160f && x >= -320f && x <= 320f)
        {
            y = Mathf.Max(y, -96f * (160f - z) / 114f);
            ramp = true;
        }
        if (z >= -160f && z <= -46f && x >= -320f && x <= 320f)
        {
            y = Mathf.Max(y, -96f * (z + 160f) / 114f);
            ramp = true;
        }
        if (ramp)
            return y;
        if (x >= -206f && x <= 206f && z >= -46f && z <= 46f)
            return -96f; // Basin floor.
        return 0f;
    }

    public override void _Ready()
    {
        var cfg = new MapConfig();
        var holes = new List<Vector4>();
        holes.Add(new Vector4(
            cfg.IslandMin.X - MarginU, cfg.IslandMin.Z - MarginU,
            cfg.IslandMax.X + MarginU, cfg.IslandMax.Z + MarginU));
        for (int i = 0; i < cfg.CoverSpots.Length && i < cfg.CoverSizes.Length; i++)
        {
            Vector3 c = cfg.CoverSpots[i];
            Vector3 s = cfg.CoverSizes[i];
            holes.Add(new Vector4(
                c.X - s.X / 2f - MarginU, c.Z - s.Z / 2f - MarginU,
                c.X + s.X / 2f + MarginU, c.Z + s.Z / 2f + MarginU));
        }

        var xs = new SortedSet<float> { cfg.ArenaMin.X, cfg.ArenaMax.X, -320f, -206f, 206f, 320f };
        var zs = new SortedSet<float> { cfg.ArenaMin.Z, cfg.ArenaMax.Z, -160f, -46f, 46f, 160f };
        foreach (Vector4 h in holes)
        {
            xs.Add(h.X);
            xs.Add(h.Z);
            zs.Add(h.Y);
            zs.Add(h.W);
        }
        var xlist = new List<float>(xs);
        var zlist = new List<float>(zs);
        var index = new Dictionary<Vector2, int>();
        var verts = new List<Vector3>();
        int polyCount = 0;
        var nav = new NavigationMesh();
        for (int ix = 0; ix < xlist.Count - 1; ix++)
        {
            for (int iz = 0; iz < zlist.Count - 1; iz++)
            {
                float x0 = xlist[ix], x1 = xlist[ix + 1];
                float z0 = zlist[iz], z1 = zlist[iz + 1];
                float cx = (x0 + x1) / 2f, cz = (z0 + z1) / 2f;
                bool inside = false;
                foreach (Vector4 h in holes)
                {
                    if (cx > h.X && cx < h.Z && cz > h.Y && cz < h.W)
                    {
                        inside = true;
                        break;
                    }
                }
                if (inside)
                    continue;
                int i00 = Vert(index, verts, x0, z0, cfg);
                int i10 = Vert(index, verts, x1, z0, cfg);
                int i11 = Vert(index, verts, x1, z1, cfg);
                int i01 = Vert(index, verts, x0, z1, cfg);
                nav.AddPolygon(new int[] { i00, i10, i11 });
                nav.AddPolygon(new int[] { i00, i11, i01 });
                polyCount += 2;
            }
        }
        nav.Vertices = verts.ToArray();
        GD.Print($"[MAKENAV] holes={holes.Count} verts={verts.Count} tris={polyCount}");
        Error err = ResourceSaver.Save(nav, "res://src/maps/fy_pileta_nav.tres");
        GD.Print($"[MAKENAV] save err={err}");
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }

    private static int Vert(Dictionary<Vector2, int> index, List<Vector3> verts, float x, float z, MapConfig cfg)
    {
        var key = new Vector2(x, z);
        if (index.TryGetValue(key, out int id))
            return id;
        id = verts.Count;
        verts.Add(new Vector3(x, HeightAt(cfg, x, z), z));
        index[key] = id;
        return id;
    }
}
