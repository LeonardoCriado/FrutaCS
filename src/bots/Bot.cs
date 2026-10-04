using Godot;

using FrutaCS.Maps;
using FrutaCS.Player;
using FrutaCS.Weapons;

namespace FrutaCS.Bots;

/// <summary>
/// AI-controlled CharacterBody3D driven by the pure <see cref="BotBrain"/>.
/// Feet at the origin; capsule 72u tall like <see cref="PlayerBody"/>.
/// Owns the engine-side concerns the brain never touches:
/// <list type="bullet">
/// <item><see cref="NavigationAgent3D"/> steering, resolved to a run-speed
/// <see cref="MovementInput"/> through <see cref="MovementSim"/> (250 u/s,
/// same write-back contract as the player).</item>
/// <item>Hearing: <see cref="OnHeardShot"/> latches shots inside
/// HearingRadiusU for 1 s; the brain treats a latched shot as trusted.</item>
/// <item>Fire-time aim error: a uniform-disc sample of AimErrorDeg applied
/// to the shot ray; damage resolves through <see cref="WeaponSim.DamageAt"/>,
/// so bots die by the exact same rules players do.</item>
/// <item>Burst control (BurstLen shots, then a 0.6 s pause), magazine +
/// infinite-reserve reload (2.5 s placeholder, same as WeaponSystem).</item>
/// </list>
/// Enemy scan covers the opposite-team "bots" group plus the "players"
/// group. Map pickups are <see cref="Maps.WeaponPickup"/> nodes in group
/// "weapon_pickups" with an int meta "tier" (knife 0, pistol 1, rifle 2,
/// awp 3) and a bool meta "taken"; grabs claim the entity (stats swap to
/// the new gun) so rivals find it taken.
/// </summary>
public partial class Bot : CharacterBody3D
{
    private const float EyeHeightU = 64f;
    private const float ChestHeightU = 36f;
    private const float HitscanRangeU = 4000f;
    private const float ReloadSec = 2.5f; // Placeholder, same as WeaponSystem.
    private const float BurstPauseSec = 0.6f;
    private const float HeardMemorySec = 1f;
    private const float PatrolReachU = 64f;
    private const float AttackHoldU = 250f;
    private const float ArriveU = 48f;
    private const float GrabU = 56f;
    private const float PatrolHalfU = 850f;
    private const float MaxHp = 100f;
    private const float KnifeIntervalSec = 0.4f;
    private const float UnstickStillSec = 2.5f;
    private const float UnstickDetourSec = 2f;

    [Export] public int Team;
    [Export] public MovementConfig Config;
    [Export] public WeaponData Weapon; // Map loadout sidearm (map 1: deagle).
    [Export] public float ReactionSec = 0.4f;
    [Export] public float AimErrorDeg = 3f;
    [Export] public int BurstLen = 3;
    [Export] public float ThreatRadiusU = 400f;
    [Export] public float SightRangeU = 1500f;
    [Export] public float SightFovDeg = 90f;
    [Export] public float AttackRangeU = 1000f;
    [Export] public float LoseSightSec = 5f;
    [Export] public float HearingRadiusU = 800f;

    private readonly BotBrain _brain = new();
    private readonly MovementSim _sim = new();
    private MovementParams _moveParams;
    private BotParams _params;
    private WeaponSim _gunSim;
    private WeaponStats _stats;
    private bool _isMelee;
    private int _currentTier = 1;
    private string _weaponId = "deagle";

    /// <summary>Weapon currently equipped (base sidearm or grabbed pickup).</summary>
    public string CurrentWeaponId => _weaponId;
    private readonly RandomNumberGenerator _rng = new();

    private Vector3 _patrolPoint;
    private Vector3 _heardPos = Vector3.Zero;
    private float _heardAge = float.MaxValue;
    private Node3D _target;
    private Vector3 _targetPos = Vector3.Zero;
    private bool _targetVisible;
    private float _cooldownSec;
    private float _burstPauseSec;
    private int _shotsInBurst;
    private int _magAmmo;
    private bool _reloading;
    private float _reloadTimerSec;
    private float _stillSec;
    private int _stepClock;
    private Vector3 _stepAnchor = Vector3.Zero;
    private Vector3[] _pathPts = [];
    private Vector3 _pathGoal = new(float.PositiveInfinity, 0f, 0f);
    private int _pathIdx;
    private int _pathClock = 99;
    private bool _pathDirty = true;
    private readonly PhysicsRayQueryParameters3D _query = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero);
    private Vector3 _unstickPoint = Vector3.Zero;
    private float _unstickSec;
    private bool _nearbyTaken = true;
    private Node3D _nearbyNode;
    private Vector3 _nearbyPos = Vector3.Zero;
    private int _nearbyTier;

    public BotState State => _brain.CurrentState;
    public int Hp { get; private set; } = 100;
    public bool IsDead { get; private set; }

    /// <summary>
    /// Per-spawn difficulty mix from the map layer (spec §6). Call after
    /// Instantiate, before AddChild: _Ready bakes these into _params.
    /// </summary>
    public void ApplyMix(BotParams mix)
    {
        ReactionSec = mix.ReactionSec;
        AimErrorDeg = mix.AimErrorDeg;
        BurstLen = mix.BurstLen;
        ThreatRadiusU = mix.ThreatRadiusU;
    }

    /// <summary>Round reset: full health, base sidearm, clean brain, at spawn.</summary>
    public void Respawn(Vector3 pos, Vector3 faceTarget)
    {
        IsDead = false;
        Hp = 100;
        GlobalPosition = pos;
        Vector3 face = faceTarget - pos;
        face.Y = 0f;
        if (face.Length() > 1f)
            Rotation = new Vector3(0f, Mathf.Atan2(-face.X, -face.Z), 0f);
        ResetLoadout();
        _brain.Reset();
        _patrolPoint = PickPatrolPoint();
        _pathDirty = true; // Teleported: any cached path is garbage.
        _heardAge = float.MaxValue;
        _cooldownSec = 0f;
        _burstPauseSec = 0f;
        _shotsInBurst = 0;
        _reloading = false;
        _stillSec = 0f;
        _unstickSec = 0f;
        var shape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        if (shape != null)
            shape.SetDeferred("disabled", false);
        var body = GetNodeOrNull<MeshInstance3D>("Body");
        if (body != null)
            body.Show();
        SetPhysicsProcess(true);
        _sim.SyncFromEngine(Vector3.Zero, true);
        Velocity = Vector3.Zero;
    }

    /// <summary>Back to the map base sidearm (map 1: deagle, the scene default).</summary>
    public void ResetLoadout()
    {
        if (Weapon == null)
            return;
        SetLoadout(Weapon.ToStats(), Weapon.WeaponId);
    }

    /// <summary>Shared equip path: stats, sim, magazine, tier and id follow.</summary>
    private void SetLoadout(WeaponStats stats, string weaponId)
    {
        _stats = stats;
        _gunSim = new WeaponSim(_stats);
        _isMelee = _stats.MeleeRangeU > 0f;
        _magAmmo = _stats.MagSize;
        _currentTier = TierOf(weaponId);
        _weaponId = weaponId;
    }

    public static int TierOf(string weaponId) => weaponId switch
    {
        "knife" => 0,
        "deagle" => 1,
        "ak47" => 2,
        "m4a1" => 2,
        "awp" => 3,
        _ => 1,
    };

    /// <summary>Same damage path as players: integer headless damage in,
    /// death at zero. No armor model in milestone 1 (no Health/armor system
    /// exists yet for either side).</summary>
    public void TakeDamage(int amount)
    {
        if (IsDead || amount <= 0)
            return;
        Hp -= amount;
        if (Hp <= 0)
            Die();
    }

    /// <summary>Group broadcast ("bots") from any firing bot.</summary>
    public void OnHeardShot(Vector3 pos)
    {
        if (IsDead)
            return;
        Vector3 d = pos - GlobalPosition;
        d.Y = 0f;
        if (d.Length() > _params.HearingRadiusU)
            return;
        _heardPos = pos;
        _heardAge = 0f;
    }

    public override void _Ready()
    {
        AddToGroup("bots");
        AddToGroup("team_" + Team);
        _rng.Randomize();
        _moveParams = Config != null ? Config.ToParams() : MovementParams.Default;
        _params = new BotParams
        {
            ReactionSec = ReactionSec,
            AimErrorDeg = AimErrorDeg,
            BurstLen = BurstLen,
            SightRangeU = SightRangeU,
            SightFovDeg = SightFovDeg,
            AttackRangeU = AttackRangeU,
            LoseSightSec = LoseSightSec,
            HearingRadiusU = HearingRadiusU,
            ThreatRadiusU = ThreatRadiusU,
        };
        if (Weapon != null)
        {
            SetLoadout(Weapon.ToStats(), Weapon.WeaponId);
        }
        // NOTE: the NavigationAgent3D scene node is intentionally unused:
        // steering queries NavigationServer3D directly (synchronous fresh
        // paths every tick), because the node's async path state goes
        // stale and thrashes on goal changes. The node stays for the
        // scene-shape test and future avoidance work.
        _patrolPoint = PickPatrolPoint();
        PaintTeamColor();
        _query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
    }

    /// <summary>
    /// Shared sight/fire raycast (one reusable query object: per-tick
    /// allocations of query objects pressure the .NET/Godot bridge).
    /// Callers set <see cref="_query"/> From/To first; self excluded.
    /// </summary>
    private Godot.Collections.Dictionary CastRay(PhysicsRayQueryParameters3D query) =>
        GetWorld3D().DirectSpaceState.IntersectRay(query);

    public override void _PhysicsProcess(double delta)
    {
        if (IsDead)
            return;
        float dt = (float)delta;
        TickTimers(dt);
        ScanEnemies();
        ScanPickups();
        BotPerception perception = new(
            GlobalPosition, Facing(),
            _targetVisible, _targetPos,
            _heardAge < HeardMemorySec, _heardPos,
            !_nearbyTaken, _nearbyPos, _nearbyTier, _currentTier,
            _patrolPoint);
        BotDecision decision = _brain.Update(perception, _params, dt);
        Steer(decision, dt);
        MaybeGrab(decision);
        MaybeFire(decision);
    }

    private void TickTimers(float dt)
    {
        if (_cooldownSec > 0f)
            _cooldownSec -= dt;
        if (_burstPauseSec > 0f)
            _burstPauseSec -= dt;
        _heardAge += dt;
        if (_reloading)
        {
            _reloadTimerSec -= dt;
            if (_reloadTimerSec <= 0f)
            {
                _reloading = false;
                _magAmmo = _stats.MagSize; // Infinite reserve (placeholder).
            }
        }
        Vector3 toPatrol = _patrolPoint - GlobalPosition;
        toPatrol.Y = 0f;
        if (toPatrol.Length() < PatrolReachU)
            _patrolPoint = PickPatrolPoint();
    }

    private void ScanEnemies()
    {
        _target = null;
        _targetVisible = false;
        Vector3 eye = GlobalPosition + Vector3.Up * EyeHeightU;
        float best = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup("bots"))
        {
            if (node is Bot other && other != this && other.Team != Team && !other.IsDead)
                Consider(other, other.GlobalPosition, eye, ref best);
        }
        foreach (Node node in GetTree().GetNodesInGroup("players"))
        {
            // Same-team humans are never enemies (otherwise bots pile onto
            // their own player: Attack-hold without firing, and melt).
            if (node is PlayerBody player && player.Team != Team && !player.IsDead)
                Consider(player, player.GlobalPosition, eye, ref best);
        }
    }

    private void Consider(Node3D candidate, Vector3 candidatePos, Vector3 eye, ref float best)
    {
        Vector3 d = candidatePos - GlobalPosition;
        d.Y = 0f;
        float dist = d.Length();
        if (dist >= best || dist > _params.SightRangeU)
            return;
        Vector3 chest = candidatePos + Vector3.Up * ChestHeightU;
        _query.From = eye;
        _query.To = chest;
        Godot.Collections.Dictionary hit = CastRay(_query);
        bool visible = hit.Count == 0 || (GodotObject)hit["collider"] == candidate;
        if (!visible)
            return;
        best = dist;
        _target = candidate;
        _targetPos = candidatePos;
        _targetVisible = true;
    }

    private void ScanPickups()
    {
        _nearbyTaken = true;
        _nearbyNode = null;
        float best = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup("weapon_pickups"))
        {
            if (node is not Node3D pickup)
                continue;
            if ((bool)pickup.GetMeta("taken", false))
                continue;
            int tier = (int)pickup.GetMeta("tier", -1);
            if (tier < 0)
                continue;
            // Same-level only: deck guns read through the basin floor from
            // above (and vice versa). Deck is y=0, basin floor y=-96; 72u
            // splits them while ramp midpoints still see both sides.
            if (Mathf.Abs(pickup.GlobalPosition.Y - GlobalPosition.Y) > 72f)
                continue;
            Vector3 d = pickup.GlobalPosition - GlobalPosition;
            d.Y = 0f;
            if (d.Length() < best)
            {
                best = d.Length();
                _nearbyNode = pickup;
                _nearbyPos = pickup.GlobalPosition;
                _nearbyTier = tier;
                _nearbyTaken = false;
            }
        }
    }

    private void Steer(BotDecision decision, float dt)
    {
        float stopDist = decision.State == BotState.Attack ? AttackHoldU : ArriveU;
        Vector3 brainToTarget = decision.MoveTarget - GlobalPosition;
        brainToTarget.Y = 0f;

        // Unstick detour overrides the ACTIVE goal (not just _patrolPoint):
        // a Chase/Pickup snagged on geometry ignores patrol repicks.
        Vector3 goal = decision.MoveTarget;
        if (_unstickSec > 0f)
        {
            _unstickSec -= dt;
            goal = _unstickPoint;
            Vector3 toUnstick = _unstickPoint - GlobalPosition;
            toUnstick.Y = 0f;
            if (toUnstick.Length() < ArriveU)
                _unstickSec = 0f;
        }
        Vector3 toTarget = goal - GlobalPosition;
        toTarget.Y = 0f;
        Vector3 wish = Vector3.Zero;
        if (toTarget.Length() > stopDist)
        {
            // Direct synchronous server queries (the NavigationAgent3D node
            // is bypassed: its async/threaded path state goes stale and
            // thrashes on goal flaps, steering bots back-and-forth instead
            // of progressing). Paths are cached and refreshed at ~6 Hz, on
            // significant goal moves, or when flagged dirty (teleport):
            // MapGetPath returns fresh complete paths, but allocating one
            // per bot per tick (780/s) is waste. Corner following advances
            // past reached corners with capsule-radius slack; an empty or
            // exhausted list falls back to the straight line. Both
            // endpoints are projected onto the mesh plane (the bake carries
            // real heights, but steering stays horizontal while bodies
            // resolve height via gravity/snap). Partial paths (unreachable
            // goals) end at the closest reachable point; arrival/grab radii
            // finish those.
            Vector3 flatFrom = new(GlobalPosition.X, 0f, GlobalPosition.Z);
            Vector3 flatGoal = new(goal.X, 0f, goal.Z);
            _pathClock++;
            if (_pathDirty || _pathClock >= 10 || (_pathGoal - flatGoal).Length() > 24f)
            {
                _pathPts = NavigationServer3D.MapGetPath(
                    GetWorld3D().NavigationMap, flatFrom, flatGoal, true);
                _pathGoal = flatGoal;
                _pathIdx = 1;
                _pathClock = 0;
                _pathDirty = false;
            }
            Vector3 next = toTarget;
            while (_pathIdx < _pathPts.Length)
            {
                Vector3 corner = _pathPts[_pathIdx] - GlobalPosition;
                corner.Y = 0f;
                if (corner.Length() > 24f)
                {
                    next = corner;
                    break;
                }
                _pathIdx++;
            }
            wish = next.Length() > 1f ? next.Normalized() : toTarget.Normalized();
        }
        var input = new MovementInput(wish, false, false);
        _sim.Tick(in input, in _moveParams, dt);
        Velocity = _sim.Velocity;
        MoveAndSlide();
        _sim.SyncFromEngine(Velocity, IsOnFloor());

        Vector3 face = wish;
        if ((decision.State == BotState.Chase || decision.State == BotState.Attack) && _targetVisible)
        {
            face = _targetPos - GlobalPosition;
            face.Y = 0f;
        }
        if (face.Length() > 1f)
            Rotation = new Vector3(0f, Mathf.Atan2(-face.X, -face.Z), 0f);

        // Anti-stuck: brain commands far but the body barely moves outside
        // combat -> detour the body itself, not just the patrol point
        // (acceptance: none stuck > 5 s; Task 8's real map is not flat).
        Vector3 h = new(_sim.Velocity.X, 0f, _sim.Velocity.Z);
        if (_unstickSec > 0f)
            _stillSec = 0f; // Detour in progress: it gets a clean attempt.
        else if (decision.State != BotState.Attack && brainToTarget.Length() > 128f && h.Length() < 5f)
            _stillSec += dt;
        else
            _stillSec = 0f;
        // Seam step-up (cheap, local): box-authored slope toes pinch
        // capsules exactly at feet level, and the pinch lurches (brief
        // motion bursts that defeat stillness timers). Gate on actual
        // displacement instead: strong intent + far target + crawling
        // under 8u per 30 ticks, in navigation states only (never mid-
        // fight: a combat pop would dodge). Ordinary walking covers
        // 100u+ per window; grinders get stepped every half second.
        // Patrol pins are usually impossible goals (a random point inside
        // a navmesh hole): deal a fresh point instead of stepping in
        // place; the debounce adopts it once it persists.
        _stepClock++;
        if (_stepClock >= 30)
        {
            Vector3 moved = GlobalPosition - _stepAnchor;
            moved.Y = 0f;
            if (IsOnFloor() && wish.Length() > 0.5f && brainToTarget.Length() > 64f
                && moved.Length() < 8f
                && (decision.State == BotState.Patrol || decision.State == BotState.Pickup))
            {
                if (decision.State == BotState.Patrol)
                    _patrolPoint = PickPatrolPoint();
                else
                    FrutaCS.Player.StepUp.TryStep(this, wish, _moveParams.RunSpeed);
            }
            _stepAnchor = GlobalPosition;
            _stepClock = 0;
        }
        if (_stillSec > UnstickStillSec)
        {
            _stillSec = 0f;
            _patrolPoint = PickPatrolPoint();
            _unstickPoint = PickUnstickPoint();
            _unstickSec = UnstickDetourSec;
        }
    }

    private void MaybeGrab(BotDecision decision)
    {
        if (!decision.WantPickup || _nearbyTaken || _nearbyNode == null)
            return;
        // Full 3D range: the AWP sits a level below the deck, and must not
        // be claimable through the floor (nor deck guns from the basin).
        if (_nearbyNode.GlobalPosition.DistanceTo(GlobalPosition) > GrabU)
            return;
        // Claim the entity so rivals (bots and the human) find it taken;
        // a lost race just rescans next tick.
        string weaponId = "";
        if (_nearbyNode is WeaponPickup pickup)
            weaponId = pickup.Grab();
        if (weaponId == "")
        {
            _nearbyTaken = true;
            _nearbyNode = null;
            return;
        }
        SwapTo(weaponId);
        _nearbyTaken = true;
        _nearbyNode = null;
    }

    /// <summary>
    /// Equip a grabbed floor gun (closes the Task 6 deferred swap): stats,
    /// sim, magazine and tier all follow the new weapon, like a fresh spawn.
    /// Unknown ids keep the current gun.
    /// </summary>
    private void SwapTo(string weaponId)
    {
        WeaponData data = GD.Load<WeaponData>($"res://data/weapons/{weaponId}.tres");
        if (data == null)
            return;
        SetLoadout(data.ToStats(), weaponId);
        _reloading = false;
        _shotsInBurst = 0;
        _burstPauseSec = 0f;
    }

    private void MaybeFire(BotDecision decision)
    {
        if (!decision.WantFire || !_targetVisible || _target == null || _gunSim == null)
            return;
        if (_cooldownSec > 0f || _burstPauseSec > 0f || _reloading)
            return;
        Vector3 eye = GlobalPosition + Vector3.Up * EyeHeightU;
        Vector3 chest = _targetPos + Vector3.Up * ChestHeightU;
        float distU = eye.DistanceTo(chest);
        if (_isMelee && distU > _stats.MeleeRangeU)
            return;
        if (!_isMelee)
        {
            if (_magAmmo <= 0)
            {
                _reloading = true;
                _reloadTimerSec = ReloadSec;
                return;
            }
        }
        _cooldownSec = _isMelee ? KnifeIntervalSec : 60f / Mathf.Max(1, _stats.Rpm);

        Vector3 dir = (chest - eye).Normalized();
        if (_params.AimErrorDeg > 0f)
        {
            Vector3 up = Mathf.Abs(dir.Y) > 0.99f ? Vector3.Right : Vector3.Up;
            Vector3 ax0 = dir.Cross(up).Normalized();
            Vector3 ax1 = dir.Cross(ax0).Normalized();
            float r = _params.AimErrorDeg * Mathf.Sqrt(_rng.Randf());
            float t = _rng.Randf() * Mathf.Tau;
            dir = dir.Rotated(ax0, Mathf.DegToRad(r * Mathf.Sin(t)));
            dir = dir.Rotated(ax1, Mathf.DegToRad(r * Mathf.Cos(t)));
        }
        var query = _query;
        query.From = eye;
        query.To = eye + dir * HitscanRangeU;
        Godot.Collections.Dictionary hit = CastRay(query);
        if (hit.Count > 0)
        {
            Vector3 hitPos = (Vector3)hit["position"];
            float impactDist = eye.DistanceTo(hitPos);
            if ((GodotObject)hit["collider"] is Bot other && other.Team != Team && !other.IsDead)
            {
                // First surface is flesh: same staged damage rules as players.
                other.TakeDamage(_gunSim.DamageAt(impactDist, false, 0f));
            }
            else if ((GodotObject)hit["collider"] is PlayerBody player
                && player.Team != Team && !player.IsDead)
            {
                // Bot-vs-player billing (Task 6 left it unbilled): the same
                // staged damage the player build applies to bots.
                player.TakeDamage(_gunSim.DamageAt(impactDist, false, 0f));
            }
            // Walls stop the bullet (no bot wallbang: simplification, disclosed).
        }
        if (!_isMelee)
            _magAmmo--;
        _shotsInBurst++;
        if (_shotsInBurst >= _params.BurstLen)
        {
            _shotsInBurst = 0;
            _burstPauseSec = BurstPauseSec;
        }
        GetTree().CallGroup("bots", "OnHeardShot", GlobalPosition);
    }

    private Vector3 Facing()
    {
        Vector3 fwd = -GlobalTransform.Basis.Z;
        fwd.Y = 0f;
        return fwd.Length() > 0.0001f ? fwd.Normalized() : Vector3.Forward;
    }

    private Vector3 PickPatrolPoint()
    {
        return new Vector3(
            _rng.RandfRange(-PatrolHalfU, PatrolHalfU),
            0f,
            _rng.RandfRange(-PatrolHalfU, PatrolHalfU));
    }

    /// <summary>Short sidestep for the unstick detour: near enough to finish
    /// inside <see cref="UnstickDetourSec"/>, after which the brain retries
    /// its original goal (possibly from a better angle). Candidates are
    /// verified by a chest-height raycast (at the higher endpoint, so ramps
    /// read clear while walls and covers block): the first clear line wins,
    /// so the detour itself doesn't wedge on the next face. Falls back to a
    /// blind sidestep when walled in on all sides; the next cycle retries.
    /// </summary>
    private Vector3 PickUnstickPoint()
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = RandomDetourPoint(120f, 220f);
            if (DetourLineClear(p))
                return p;
        }
        return RandomDetourPoint(120f, 220f);
    }

    private Vector3 RandomDetourPoint(float minU, float maxU)
    {
        float angle = _rng.Randf() * Mathf.Tau;
        float dist = _rng.RandfRange(minU, maxU);
        Vector3 p = GlobalPosition + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
        p.Y = 0f;
        return p;
    }

    private bool DetourLineClear(Vector3 target)
    {
        float height = Mathf.Max(GlobalPosition.Y, 0f) + ChestHeightU;
        Vector3 from = new(GlobalPosition.X, height, GlobalPosition.Z);
        Vector3 to = new(target.X, height, target.Z);
        _query.From = from;
        _query.To = to;
        Godot.Collections.Dictionary hit = CastRay(_query);
        if (hit.Count == 0)
            return true;
        // Only fighters in the way: they move, and bodies slide past each
        // other. Static geometry means pushing into another face.
        return (GodotObject)hit["collider"] is not StaticBody3D;
    }

    private void PaintTeamColor()
    {
        var body = GetNodeOrNull<MeshInstance3D>("Body");
        if (body == null)
            return;
        var mat = new StandardMaterial3D
        {
            AlbedoColor = Team == 0 ? new Color(0.2f, 0.45f, 0.9f) : new Color(0.9f, 0.35f, 0.2f),
        };
        body.SetSurfaceOverrideMaterial(0, mat);
    }

    private void Die()
    {
        IsDead = true;
        Hp = 0;
        var shape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        if (shape != null)
            shape.SetDeferred("disabled", true);
        var body = GetNodeOrNull<MeshInstance3D>("Body");
        if (body != null)
            body.Hide();
        SetPhysicsProcess(false);
        GetTree().CallGroup("match_manager", "OnFighterDown", Team, this);
    }
}
