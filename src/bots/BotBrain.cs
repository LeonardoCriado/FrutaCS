using System;
using Godot;

namespace FrutaCS.Bots;

/// <summary>Bot finite-state machine states (spec §4: patrol, pursue, bursts, pickup).</summary>
public enum BotState
{
    Patrol,
    Chase,
    Attack,
    Pickup,
}

/// <summary>
/// What the node glue saw this tick. Engine-free: only Vector3 math.
/// HasEnemy means line-of-sight is clear (the glue raycasts); the brain
/// additionally gates cone + range, so tests can place enemies behind or
/// beyond sight. HeardShot is trusted as already inside hearing radius
/// (the glue owns hearing). Weapon tiers are opaque ints ordered worst to
/// best; the glue maps weapon ids (knife 0, deagle 1, rifle 2, awp 3).
/// PatrolPoint is glue-provided (random reachable point); the brain just
/// steers to it while patrolling.
/// </summary>
public readonly record struct BotPerception(
    Vector3 SelfPos,
    Vector3 Forward,
    bool HasEnemy,
    Vector3 EnemyPos,
    bool HeardShot,
    Vector3 HeardPos,
    bool HasPickup,
    Vector3 PickupPos,
    int PickupTier,
    int CurrentTier,
    Vector3 PatrolPoint
);

/// <summary>What the bot wants this tick. The glue resolves MoveTarget via
/// NavigationAgent3D (run speed), fires subject to its own cooldown/burst
/// counters, and equips on proximity when WantPickup.</summary>
public readonly record struct BotDecision(
    BotState State,
    Vector3 MoveTarget,
    bool WantFire,
    bool WantPickup
);

/// <summary>
/// Pure state machine over <see cref="BotPerception"/>. Instance state per
/// bot (current state, last known enemy, timers). No Node/SceneTree APIs.
/// Rules:
/// <list type="bullet">
/// <item>Patrol: better gun nearby -> Pickup; enemy in cone+range -> Chase;
/// heard shot -> Chase (investigate); else keep patrolling.</item>
/// <item>Chase: close into AttackRange with sight -> Attack; no stimulus
/// (sight or shot) for LoseSightSec -> Patrol. Gun detours never interrupt
/// a fight (pickup only from Patrol).</item>
/// <item>Attack: hold on the enemy; WantFire only after ReactionSec of
/// continuous engagement. Lost sight or out of range -> Chase (re-acquire)
/// until LoseSightSec without stimulus expires -> Patrol.</item>
/// <item>Pickup: walk to the gun; sighting an enemy interrupts -> Chase;
/// gun gone -> Patrol.</item>
/// </list>
/// </summary>
public sealed class BotBrain
{
    private BotState _state = BotState.Patrol;
    private Vector3 _lastKnownEnemy = Vector3.Zero;
    private bool _hasLastKnown;
    private float _timeSinceSeen;
    private float _reactionElapsed;

    public BotState CurrentState => _state;

    /// <summary>Round reset: back to Patrol with no memory of the last round.</summary>
    public void Reset()
    {
        _state = BotState.Patrol;
        _lastKnownEnemy = Vector3.Zero;
        _hasLastKnown = false;
        _timeSinceSeen = 0f;
        _reactionElapsed = 0f;
    }

    public static bool SeesEnemy(in BotPerception p, in BotParams prm, out float distU)
    {
        distU = 0f;
        if (!p.HasEnemy)
            return false;
        Vector3 toEnemy = p.EnemyPos - p.SelfPos;
        toEnemy.Y = 0f;
        distU = toEnemy.Length();
        if (distU > prm.SightRangeU)
            return false;
        Vector3 fwd = new(p.Forward.X, 0f, p.Forward.Z);
        if (fwd.Length() < 0.0001f || distU < 0.0001f)
            return true; // On top of the enemy (or no facing): point blank.
        float cosHalf = MathF.Cos(MathF.PI * prm.SightFovDeg / 360f);
        float cosAngle = fwd.Normalized().Dot(toEnemy.Normalized());
        return cosAngle >= cosHalf;
    }

    public BotDecision Update(in BotPerception p, in BotParams prm, float delta)
    {
        bool seen = SeesEnemy(in p, in prm, out float enemyDist);
        if (seen)
        {
            _lastKnownEnemy = p.EnemyPos;
            _hasLastKnown = true;
            _timeSinceSeen = 0f;
        }
        else if (p.HeardShot)
        {
            // Hearing is stimulus too: a shot resets the same timer the
            // Patrol-return rule expires on, and its position becomes the
            // investigation point after the glue's 1 s latch expires.
            // Without this, Patrol --shot--> Chase returns to Patrol on
            // the very next tick whenever the timer already ran out.
            _lastKnownEnemy = p.HeardPos;
            _hasLastKnown = true;
            _timeSinceSeen = 0f;
        }
        else
        {
            _timeSinceSeen += MathF.Max(0f, delta);
        }

        bool betterGun = p.HasPickup && p.PickupTier > p.CurrentTier;

        switch (_state)
        {
            case BotState.Patrol:
                if (betterGun)
                    return Enter(BotState.Pickup, new BotDecision(BotState.Pickup, p.PickupPos, false, true));
                if (seen)
                    return Engage(p.EnemyPos, enemyDist, prm.AttackRangeU);
                if (p.HeardShot)
                    return Enter(BotState.Chase, new BotDecision(BotState.Chase, p.HeardPos, false, false));
                return new BotDecision(BotState.Patrol, p.PatrolPoint, false, false);

            case BotState.Chase:
                if (seen && enemyDist <= prm.AttackRangeU)
                    return Enter(BotState.Attack, new BotDecision(BotState.Attack, p.EnemyPos, false, false));
                if (_timeSinceSeen >= prm.LoseSightSec)
                    return Enter(BotState.Patrol, new BotDecision(BotState.Patrol, p.PatrolPoint, false, false));
                Vector3 chaseTarget = seen ? p.EnemyPos : p.HeardShot ? p.HeardPos
                    : _hasLastKnown ? _lastKnownEnemy : p.PatrolPoint;
                return new BotDecision(BotState.Chase, chaseTarget, false, false);

            case BotState.Attack:
                if (seen && enemyDist <= prm.AttackRangeU)
                {
                    _reactionElapsed += MathF.Max(0f, delta);
                    bool fire = _reactionElapsed >= prm.ReactionSec;
                    return new BotDecision(BotState.Attack, p.EnemyPos, fire, false);
                }
                if (_timeSinceSeen >= prm.LoseSightSec)
                    return Enter(BotState.Patrol, new BotDecision(BotState.Patrol, p.PatrolPoint, false, false));
                Vector3 reacquire = seen ? p.EnemyPos
                    : _hasLastKnown ? _lastKnownEnemy : p.PatrolPoint;
                return Enter(BotState.Chase, new BotDecision(BotState.Chase, reacquire, false, false));

            case BotState.Pickup:
            default:
                // Drop the gun run the moment it stops being an upgrade
                // (e.g. grabbed a better gun en route): trekking to a
                // downgrade pins bots on geometry for nothing.
                if (!p.HasPickup || p.PickupTier <= p.CurrentTier)
                    return Enter(BotState.Patrol, new BotDecision(BotState.Patrol, p.PatrolPoint, false, false));
                // Distant sightings don't break off the run (grab first,
                // fight armed): only an immediate threat inside ThreatRadiusU
                // interrupts. Legacy ThreatRadiusU <= 0 keeps the old
                // hair-trigger (any sighting engages).
                if (seen && (prm.ThreatRadiusU <= 0f || enemyDist <= prm.ThreatRadiusU))
                    return Engage(p.EnemyPos, enemyDist, prm.AttackRangeU);
                return new BotDecision(BotState.Pickup, p.PickupPos, false, true);
        }
    }

    /// <summary>Visible enemy: fight up close, chase at distance.</summary>
    private BotDecision Engage(Vector3 enemyPos, float enemyDist, float attackRangeU)
    {
        if (enemyDist <= attackRangeU)
            return Enter(BotState.Attack, new BotDecision(BotState.Attack, enemyPos, false, false));
        return Enter(BotState.Chase, new BotDecision(BotState.Chase, enemyPos, false, false));
    }

    private BotDecision Enter(BotState state, BotDecision decision)
    {
        _state = state;
        if (state == BotState.Attack)
            _reactionElapsed = 0f; // Every engagement starts trigger-shy.
        return decision;
    }
}
