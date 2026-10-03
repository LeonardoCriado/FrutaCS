using Godot;

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
/// group. Map pickups (Task 7+ map work) appear as Node3D in group
/// "weapon_pickups" with an int meta "tier" (knife 0, pistol 1, rifle 2,
/// awp 3); until those entities exist HasPickup is always false and the
/// Pickup state stays dormant (brain-covered, glue-wired).
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
    private const float UnstickDetourSec = 3f;

    [Export] public int Team;
    [Export] public MovementConfig Config;
    [Export] public WeaponData Weapon; // Map loadout sidearm (map 1: deagle).
    [Export] public float ReactionSec = 0.4f;
    [Export] public float AimErrorDeg = 3f;
    [Export] public int BurstLen = 3;
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
    private NavigationAgent3D _agent;
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
    private Vector3 _unstickPoint = Vector3.Zero;
    private float _unstickSec;
    private bool _nearbyTaken = true;
    private Vector3 _nearbyPos = Vector3.Zero;
    private int _nearbyTier;

    public BotState State => _brain.CurrentState;
    public int Hp { get; private set; } = 100;
    public bool IsDead { get; private set; }

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
        };
        if (Weapon != null)
        {
            _stats = Weapon.ToStats();
            _gunSim = new WeaponSim(_stats);
            _isMelee = _stats.MeleeRangeU > 0f;
            _magAmmo = _stats.MagSize;
            _currentTier = TierOf(_stats.WeaponId);
        }
        _agent = GetNodeOrNull<NavigationAgent3D>("NavigationAgent3D");
        _patrolPoint = PickPatrolPoint();
        PaintTeamColor();
    }

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
            if (node is Node3D player && player != this)
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
        var query = PhysicsRayQueryParameters3D.Create(eye, chest);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
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
        float best = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup("weapon_pickups"))
        {
            if (node is not Node3D pickup)
                continue;
            int tier = (int)pickup.GetMeta("tier", -1);
            if (tier < 0)
                continue;
            Vector3 d = pickup.GlobalPosition - GlobalPosition;
            d.Y = 0f;
            if (d.Length() < best)
            {
                best = d.Length();
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
            if (_agent != null)
                _agent.TargetPosition = goal;
            Vector3 next = _agent != null ? _agent.GetNextPathPosition() - GlobalPosition : toTarget;
            next.Y = 0f;
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
        if (_stillSec > 5f)
        {
            _stillSec = 0f;
            _patrolPoint = PickPatrolPoint();
            _unstickPoint = PickUnstickPoint();
            _unstickSec = UnstickDetourSec;
        }
    }

    private void MaybeGrab(BotDecision decision)
    {
        if (!decision.WantPickup || _nearbyTaken)
            return;
        Vector3 d = _nearbyPos - GlobalPosition;
        d.Y = 0f;
        if (d.Length() > GrabU)
            return;
        // No pickup entities exist yet in milestone 1: latch the tier so the
        // brain stands down. Stat/visual swap arrives with map pickups.
        _currentTier = _nearbyTier;
        _nearbyTaken = true;
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
        var query = PhysicsRayQueryParameters3D.Create(eye, eye + dir * HitscanRangeU);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            Vector3 hitPos = (Vector3)hit["position"];
            float impactDist = eye.DistanceTo(hitPos);
            if ((GodotObject)hit["collider"] is Bot other && other.Team != Team && !other.IsDead)
            {
                // First surface is flesh: same staged damage rules as players.
                other.TakeDamage(_gunSim.DamageAt(impactDist, false, 0f));
            }
            // Player health arrives with the round rules (Task 7+): until
            // then the impact resolves with no one to bill. Walls stop the
            // bullet (no bot wallbang: simplification, disclosed).
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
    /// its original goal (possibly from a better angle).</summary>
    private Vector3 PickUnstickPoint()
    {
        float angle = _rng.Randf() * Mathf.Tau;
        float dist = _rng.RandfRange(200f, 350f);
        Vector3 p = GlobalPosition + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
        p.Y = 0f;
        return p;
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
    }
}
