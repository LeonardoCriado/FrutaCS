using Godot;

namespace FrutaCS.Maps;

/// <summary>
/// Map 1 (fy_pileta) data, per spec §7: symmetric ~2000x2000u arena,
/// 7 spawns per team on opposed arcs, disputed mid + two side lanes,
/// jumpable 40u covers (45u jump apex clears with margin; GoldSrc-faithful,
/// matches the tennis reference median exactly), 128u+ perimeter walls. Spawn/pickup numbers are
/// tuned against docs/reference/mediciones.md (poolday row: CT-T centroid
/// 962u, enemy min/mean/max 635/1047/1436u, clear sightline 1249u — all
/// within ±10%; same-team spacing 131.8u vs iceworld 126u); see
/// tests/FrutaCS.Tests/MapConfigTests.cs for the cross-check.
/// Acquisition on map 1: base loadout knife + Deagle, AK-47 x2 / M4A1 x2 /
/// AWP x1 as pickups (mid AKs on the bridge, side M4s, AWP on the basin
/// floor). No buy zones on map 1 (empty array reserves the slot).
/// fy_pileta.tscn places markers from these arrays; MatchManager spawns
/// the player + 13 bots through them and re-arms per round from them.
/// </summary>
[GlobalClass]
public partial class MapConfig : Resource
{
    [Export] public string MapId = "fy_pileta";

    /// <summary>Playable footprint: x/z in [-1000, 1000], deck top at y = 0.</summary>
    [Export] public Vector3 ArenaMin = new(-1000f, 0f, -1000f);
    [Export] public Vector3 ArenaMax = new(1000f, 0f, 1000f);

    /// <summary>Perimeter wall height (spec: 128u+ walls).</summary>
    [Export] public float WallHeightU = 256f;

    /// <summary>CT spawns, south arc (7). T is the point mirror.</summary>
    [Export]
    public Vector3[] SpawnsCT = new Vector3[]
    {
        new Vector3(-380f, 0f, -668f),
        new Vector3(-253.3f, 0f, -485.8f),
        new Vector3(-126.7f, 0f, -376.4f),
        new Vector3(0f, 0f, -340f),
        new Vector3(126.7f, 0f, -376.4f),
        new Vector3(253.3f, 0f, -485.8f),
        new Vector3(380f, 0f, -668f),
    };

    /// <summary>T spawns, north arc: exact point mirror of SpawnsCT.</summary>
    [Export]
    public Vector3[] SpawnsT = new Vector3[]
    {
        new Vector3(380f, 0f, 668f),
        new Vector3(253.3f, 0f, 485.8f),
        new Vector3(126.7f, 0f, 376.4f),
        new Vector3(0f, 0f, 340f),
        new Vector3(-126.7f, 0f, 376.4f),
        new Vector3(-253.3f, 0f, 485.8f),
        new Vector3(-380f, 0f, 668f),
    };

    /// <summary>
    /// Per-spawn difficulty mix, CT (spec §6: the map defines the mix).
    /// Easy on the flanks, hardest at CT index 3 (mid). Symmetric copy
    /// on MixT keeps the matchup fair.
    /// </summary>
    [Export]
    public BotDifficulty[] MixCT = new BotDifficulty[]
    {
        new BotDifficulty { ReactionSec = 0.55f, AimErrorDeg = 4.5f, BurstLen = 2 },
        new BotDifficulty { ReactionSec = 0.45f, AimErrorDeg = 3.5f, BurstLen = 3 },
        new BotDifficulty { ReactionSec = 0.4f, AimErrorDeg = 3f, BurstLen = 3 },
        new BotDifficulty { ReactionSec = 0.3f, AimErrorDeg = 2f, BurstLen = 4 },
        new BotDifficulty { ReactionSec = 0.4f, AimErrorDeg = 3f, BurstLen = 3 },
        new BotDifficulty { ReactionSec = 0.45f, AimErrorDeg = 3.5f, BurstLen = 3 },
        new BotDifficulty { ReactionSec = 0.55f, AimErrorDeg = 4.5f, BurstLen = 2 },
    };

    /// <summary>Per-spawn difficulty mix, T (mirrors MixCT).</summary>
    [Export]
    public BotDifficulty[] MixT = new BotDifficulty[]
    {
        new BotDifficulty { ReactionSec = 0.55f, AimErrorDeg = 4.5f, BurstLen = 2 },
        new BotDifficulty { ReactionSec = 0.45f, AimErrorDeg = 3.5f, BurstLen = 3 },
        new BotDifficulty { ReactionSec = 0.4f, AimErrorDeg = 3f, BurstLen = 3 },
        new BotDifficulty { ReactionSec = 0.3f, AimErrorDeg = 2f, BurstLen = 4 },
        new BotDifficulty { ReactionSec = 0.4f, AimErrorDeg = 3f, BurstLen = 3 },
        new BotDifficulty { ReactionSec = 0.45f, AimErrorDeg = 3.5f, BurstLen = 3 },
        new BotDifficulty { ReactionSec = 0.55f, AimErrorDeg = 4.5f, BurstLen = 2 },
    };

    /// <summary>
    /// Weapon pickups: AK x2 (mid, on the bridge past the island hole),
    /// M4 x2 (flank lanes near the corner spawns, so flank bots arm before
    /// mid contact instead of funneling into the grinder unarmed), AWP x1
    /// (basin floor east of the island hole). Every pickup sits OUTSIDE
    /// the navmesh holes (MapConfigTests pins this): agents must be able
    /// to route to the guns, or seekers dither on hole edges forever.
    /// </summary>
    [Export]
    public PickupEntry[] Pickups = new PickupEntry[]
    {
        new PickupEntry { WeaponId = "ak47", Position = new Vector3(0f, 4f, -160f) },
        new PickupEntry { WeaponId = "ak47", Position = new Vector3(0f, 4f, 160f) },
        new PickupEntry { WeaponId = "m4a1", Position = new Vector3(560f, 4f, -400f) },
        new PickupEntry { WeaponId = "m4a1", Position = new Vector3(-560f, 4f, 400f) },
        new PickupEntry { WeaponId = "awp", Position = new Vector3(170f, -92f, 0f) },
    };

    /// <summary>
    /// Low-cover centers (feet on the supporting surface). All 40u tall
    /// (spec §7 low jumpable covers); C1 is 80x80 on the mid island, the rest 128x128.
    /// </summary>
    [Export]
    public Vector3[] CoverSpots = new Vector3[]
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(400f, 0f, 0f),
        new Vector3(-400f, 0f, 0f),
        new Vector3(560f, 0f, 260f),
        new Vector3(-560f, 0f, -260f),
        new Vector3(560f, 0f, -260f),
        new Vector3(-560f, 0f, 260f),
    };

    /// <summary>Cover footprints (full sizes, heights all 40u).</summary>
    [Export]
    public Vector3[] CoverSizes = new Vector3[]
    {
        new Vector3(80f, 40f, 80f),
        new Vector3(128f, 40f, 128f),
        new Vector3(128f, 40f, 128f),
        new Vector3(128f, 40f, 128f),
        new Vector3(128f, 40f, 128f),
        new Vector3(128f, 40f, 128f),
        new Vector3(128f, 40f, 128f),
    };

    /// <summary>Empty pool basin opening (deck hole): x in [-320, 320], z in [-160, 160].</summary>
    [Export] public Vector3 PoolRectMin = new(-320f, 0f, -160f);
    [Export] public Vector3 PoolRectMax = new(320f, 0f, -160f + 320f);

    /// <summary>Basin floor top (walkable via 40-degree ramps).</summary>
    [Export] public float BasinFloorYU = -96f;

    /// <summary>
    /// Mid island footprint (deck-height block in the basin middle, carries
    /// Cover0). The navmesh bake cuts it out as an obstacle (with Cover0).
    /// </summary>
    [Export] public Vector3 IslandMin = new Vector3(-120f, 0f, -100f);
    [Export] public Vector3 IslandMax = new Vector3(120f, 0f, 100f);

    /// <summary>Round-reset loadout (spec §7). Spawn grants the sidearm
    /// (knife needs weapon switching, which milestone 1 has no slot for).</summary>
    [Export] public string[] BaseLoadoutIds = new string[] { "knife", "deagle" };

    /// <summary>Buy zones: none on map 1 (spec §7). Slot reserved per spec §6.</summary>
    [Export] public Vector3[] BuyZones = new Vector3[0];
}
