using System;
using System.Collections.Generic;
using System.IO;
using GdUnit4;
using Godot;

using FrutaCS.Bots;
using FrutaCS.Player;

namespace FrutaCS.Tests;

/// <summary>
/// Task 6 brain checks. The brain is engine-free (only Vector3 math), so
/// state transitions run under plain `dotnet test`. The scene-shape test
/// parses `src/bots/Bot.tscn` as text (same pattern as
/// PlayerWeaponGlueTests): C# scene nodes cannot be instantiated without
/// the engine runtime.
/// </summary>
[TestSuite]
public class BotBrainTests
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

    private static BotPerception PatrolPerception(Vector3 selfPos, Vector3 patrolPoint) => new(
        selfPos, Vector3.Forward,
        false, Vector3.Zero,
        false, Vector3.Zero,
        false, Vector3.Zero, 0, 1,
        patrolPoint);

    private static BotPerception EnemyPerception(Vector3 enemyPos) => new(
        Vector3.Zero, Vector3.Forward,
        true, enemyPos,
        false, Vector3.Zero,
        false, Vector3.Zero, 0, 1,
        new Vector3(500f, 0f, 500f));

    [TestCase]
    public void SeesEnemyTransitionsToChase()
    {
        // 1200 u ahead: inside sight range (1500) and cone (90 deg),
        // outside attack range (1000) -> Chase, closing, holding fire.
        var brain = new BotBrain();
        var p = BotParams.Default;
        BotDecision d = brain.Update(EnemyPerception(new Vector3(0f, 0f, -1200f)), p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Chase);
        Assertions.AssertThat(d.MoveTarget == new Vector3(0f, 0f, -1200f)).IsTrue();
        Assertions.AssertThat(d.WantFire).IsFalse();
        Assertions.AssertThat(d.WantPickup).IsFalse();
    }

    [TestCase]
    public void ReachesAttackInRange()
    {
        // 500 u ahead: inside attack range -> Attack, but no instant fire
        // (reaction gate owns the first trigger pull).
        var brain = new BotBrain();
        var p = BotParams.Default;
        BotDecision d = brain.Update(EnemyPerception(new Vector3(0f, 0f, -500f)), p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Attack);
        Assertions.AssertThat(d.WantFire).IsFalse();
    }

    [TestCase]
    public void FiresOnlyAfterReaction()
    {
        var brain = new BotBrain();
        var p = BotParams.Default with { ReactionSec = 0.5f };
        var seen = EnemyPerception(new Vector3(0f, 0f, -500f));
        BotDecision d = brain.Update(seen, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Attack);
        Assertions.AssertThat(d.WantFire).IsFalse();
        for (int i = 0; i < 10; i++)
            d = brain.Update(seen, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Attack);
        Assertions.AssertThat(d.WantFire).IsFalse(); // 11/60 s < 0.5 s.
        for (int i = 0; i < 30; i++)
            d = brain.Update(seen, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Attack);
        Assertions.AssertThat(d.WantFire).IsTrue(); // 41/60 s > 0.5 s.
    }

    [TestCase]
    public void PicksBetterWeaponNearby()
    {
        // AK (tier 2) on the floor, Deagle (tier 1) in hand -> detour.
        var brain = new BotBrain();
        var p = BotParams.Default;
        var perception = new BotPerception(
            Vector3.Zero, Vector3.Forward,
            false, Vector3.Zero,
            false, Vector3.Zero,
            true, new Vector3(200f, 0f, 0f), 2, 1,
            new Vector3(500f, 0f, 500f));
        BotDecision d = brain.Update(perception, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Pickup);
        Assertions.AssertThat(d.WantPickup).IsTrue();
        Assertions.AssertThat(d.MoveTarget == new Vector3(200f, 0f, 0f)).IsTrue();
        Assertions.AssertThat(d.WantFire).IsFalse();
    }

    [TestCase]
    public void DropsPickupRunWhenGunNoLongerUpgrade()
    {
        // Enter Pickup for the AK (tier 2 > deagle 1), then upgrade past it
        // mid-route (simulated by current tier 2): the floor gun is no
        // longer an upgrade, so the bot patrols instead of trekking to a
        // downgrade (and pinning on geometry for nothing).
        var brain = new BotBrain();
        var p = BotParams.Default;
        var pickup = new BotPerception(
            Vector3.Zero, Vector3.Forward,
            false, Vector3.Zero,
            false, Vector3.Zero,
            true, new Vector3(200f, 0f, 0f), 2, 1,
            new Vector3(500f, 0f, 500f));
        BotDecision d = brain.Update(pickup, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Pickup);
        var stale = pickup with { PickupTier = 2, CurrentTier = 2 };
        BotDecision d2 = brain.Update(stale, p, Delta);
        Assertions.AssertThat(d2.State).IsEqual(BotState.Patrol);
        Assertions.AssertThat(d2.WantPickup).IsFalse();
    }

    [TestCase]
    public void PickupIgnoresDistantEnemy()
    {
        // Gun run in progress (tier 2 floor gun, tier 1 in hand), enemy
        // visible at 800u (inside sight, outside the 400u threat radius):
        // grab first, fight armed — the run continues.
        var brain = new BotBrain();
        var p = BotParams.Default;
        var pickup = new BotPerception(
            Vector3.Zero, Vector3.Forward,
            false, Vector3.Zero,
            false, Vector3.Zero,
            true, new Vector3(200f, 0f, 0f), 2, 1,
            new Vector3(500f, 0f, 500f));
        BotDecision d = brain.Update(pickup, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Pickup);
        var spotted = pickup with { HasEnemy = true, EnemyPos = new Vector3(0f, 0f, -800f) };
        BotDecision d2 = brain.Update(spotted, p, Delta);
        Assertions.AssertThat(d2.State).IsEqual(BotState.Pickup);
        Assertions.AssertThat(d2.WantPickup).IsTrue();
    }

    [TestCase]
    public void PickupInterruptedByCloseEnemy()
    {
        // Same run, but the enemy is 300u away (inside threat radius):
        // immediate threat wins, the bot engages.
        var brain = new BotBrain();
        var p = BotParams.Default;
        var pickup = new BotPerception(
            Vector3.Zero, Vector3.Forward,
            false, Vector3.Zero,
            false, Vector3.Zero,
            true, new Vector3(200f, 0f, 0f), 2, 1,
            new Vector3(500f, 0f, 500f));
        BotDecision d = brain.Update(pickup, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Pickup);
        var spotted = pickup with { HasEnemy = true, EnemyPos = new Vector3(0f, 0f, -300f) };
        BotDecision d2 = brain.Update(spotted, p, Delta);
        Assertions.AssertThat(d2.State).IsEqual(BotState.Attack);
        Assertions.AssertThat(d2.WantPickup).IsFalse();
    }

    [TestCase]
    public void ZeroThreatRadiusKeepsLegacyInterrupt()
    {
        // ThreatRadiusU <= 0 disables the gate: any sighting engages,
        // the Task 6 hair-trigger. Guards bare-struct construction too
        // (a zero-initialized BotParams must behave like the old brain).
        var brain = new BotBrain();
        var p = BotParams.Default with { ThreatRadiusU = 0f };
        var pickup = new BotPerception(
            Vector3.Zero, Vector3.Forward,
            false, Vector3.Zero,
            false, Vector3.Zero,
            true, new Vector3(200f, 0f, 0f), 2, 1,
            new Vector3(500f, 0f, 500f));
        BotDecision d = brain.Update(pickup, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Pickup);
        var spotted = pickup with { HasEnemy = true, EnemyPos = new Vector3(0f, 0f, -800f) };
        BotDecision d2 = brain.Update(spotted, p, Delta);
        Assertions.AssertThat(d2.State).IsEqual(BotState.Attack);
    }

    [TestCase]
    public void LosesEnemyReturnsToPatrol()
    {
        var brain = new BotBrain();
        var p = BotParams.Default;
        BotDecision d = brain.Update(EnemyPerception(new Vector3(0f, 0f, -1200f)), p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Chase);
        var blind = PatrolPerception(Vector3.Zero, new Vector3(500f, 0f, 500f));
        for (int i = 0; i < 240; i++)
            d = brain.Update(blind, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Chase); // 4 s < 5 s.
        for (int i = 0; i < 120; i++)
            d = brain.Update(blind, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Patrol); // 6 s > 5 s.
        Assertions.AssertThat(d.MoveTarget == new Vector3(500f, 0f, 500f)).IsTrue();
        Assertions.AssertThat(d.WantFire).IsFalse();
    }

    [TestCase]
    public void HeardShotTransitionsToChaseAndPersists()
    {
        // 10 s of silence first: the lose-sight timer is long expired, so a
        // pre-fix brain would Patrol -> Chase -> Patrol on consecutive ticks.
        var brain = new BotBrain();
        var p = BotParams.Default;
        var blind = PatrolPerception(Vector3.Zero, new Vector3(500f, 0f, 500f));
        BotDecision d = brain.Update(blind, p, Delta);
        for (int i = 0; i < 600; i++)
            d = brain.Update(blind, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Patrol);
        var heard = new BotPerception(
            Vector3.Zero, Vector3.Forward,
            false, Vector3.Zero,
            true, new Vector3(300f, 0f, 300f),
            false, Vector3.Zero, 0, 1,
            new Vector3(500f, 0f, 500f));
        d = brain.Update(heard, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Chase);
        Assertions.AssertThat(d.MoveTarget == new Vector3(300f, 0f, 300f)).IsTrue();
        for (int i = 0; i < 30; i++)
            d = brain.Update(heard, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Chase);
        Assertions.AssertThat(d.MoveTarget == new Vector3(300f, 0f, 300f)).IsTrue();
        // Shot memory gone, still no sight: keeps investigating the heard
        // position instead of snapping back to patrol.
        for (int i = 0; i < 60; i++)
            d = brain.Update(blind, p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Chase);
        Assertions.AssertThat(d.MoveTarget == new Vector3(300f, 0f, 300f)).IsTrue();
        Assertions.AssertThat(d.WantFire).IsFalse();
    }

    [TestCase]
    public void EnemyBehindStaysPatrol()
    {
        // 500 u behind the facing (-Z): outside the 90 deg cone -> ignored.
        var brain = new BotBrain();
        var p = BotParams.Default;
        BotDecision d = brain.Update(EnemyPerception(new Vector3(0f, 0f, 500f)), p, Delta);
        Assertions.AssertThat(d.State).IsEqual(BotState.Patrol);
        Assertions.AssertThat(d.WantFire).IsFalse();
    }

    [TestCase]
    public void BotSceneShape()
    {
        string tscn = File.ReadAllText(Path.Combine(RepoRoot(), "src", "bots", "Bot.tscn"));
        Assertions.AssertThat(tscn.Contains("res://src/bots/Bot.cs")).IsTrue();
        Assertions.AssertThat(tscn.Contains("NavigationAgent3D")).IsTrue();
        Assertions.AssertThat(tscn.Contains("height = 72")).IsTrue();
        Assertions.AssertThat(tscn.Contains("radius = 16")).IsTrue();
        Assertions.AssertThat(tscn.Contains("0, 36, 0")).IsTrue();
    }

    [TestCase]
    public void SpreadAcrossArena()
    {
        // 13 brains + run-speed sims on a flat empty arena (direct steering
        // stands in for NavigationAgent3D, which has nothing to avoid here):
        // within 20 s they occupy >= 3 of 4 quadrants, none stuck > 5 s.
        const int Bots = 13;
        const int Ticks = 20 * 60;
        const float ArenaHalfU = 850f;
        var rng = new Random(1234);
        var p = BotParams.Default;

        Vector3 RandomPoint()
        {
            return new Vector3(
                (float)(rng.NextDouble() * 2.0 - 1.0) * ArenaHalfU,
                0f,
                (float)(rng.NextDouble() * 2.0 - 1.0) * ArenaHalfU);
        }

        var brains = new BotBrain[Bots];
        var sims = new MovementSim[Bots];
        var pos = new Vector3[Bots];
        var patrol = new Vector3[Bots];
        var lastDir = new Vector3[Bots];
        for (int i = 0; i < Bots; i++)
        {
            brains[i] = new BotBrain();
            sims[i] = new MovementSim();
            pos[i] = RandomPoint();
            patrol[i] = RandomPoint();
            lastDir[i] = Vector3.Forward;
        }
        var moveParams = MovementParams.Default;
        var snapshots = new List<Vector3[]>();
        snapshots.Add((Vector3[])pos.Clone());

        for (int t = 0; t < Ticks; t++)
        {
            for (int i = 0; i < Bots; i++)
            {
                Vector3 toTarget = patrol[i] - pos[i];
                toTarget.Y = 0f;
                if (toTarget.Length() < 64f)
                {
                    patrol[i] = RandomPoint();
                    toTarget = patrol[i] - pos[i];
                    toTarget.Y = 0f;
                }
                var perception = new BotPerception(
                    pos[i], lastDir[i],
                    false, Vector3.Zero,
                    false, Vector3.Zero,
                    false, Vector3.Zero, 0, 1,
                    patrol[i]);
                BotDecision d = brains[i].Update(perception, p, Delta);
                Assertions.AssertThat(d.State).IsEqual(BotState.Patrol);
                Vector3 wish = toTarget.Length() > 1f ? toTarget.Normalized() : Vector3.Zero;
                if (wish != Vector3.Zero)
                    lastDir[i] = wish;
                var input = new MovementInput(wish, false, false);
                sims[i].Tick(in input, in moveParams, Delta);
                pos[i] += new Vector3(sims[i].Velocity.X, 0f, sims[i].Velocity.Z) * Delta;
            }
            if ((t + 1) % 300 == 0)
                snapshots.Add((Vector3[])pos.Clone());
        }

        var zones = new HashSet<int>();
        for (int i = 0; i < Bots; i++)
        {
            int zone = (pos[i].X > 0f ? 2 : 0) + (pos[i].Z > 0f ? 1 : 0);
            zones.Add(zone);
        }
        Assertions.AssertThat(zones.Count >= 3).IsTrue();
        for (int w = 1; w < snapshots.Count; w++)
        {
            for (int i = 0; i < Bots; i++)
            {
                Vector3 a = snapshots[w - 1][i];
                Vector3 b = snapshots[w][i];
                float moved = new Vector3(b.X - a.X, 0f, b.Z - a.Z).Length();
                Assertions.AssertThat(moved > 50f).IsTrue();
            }
        }
    }
}
