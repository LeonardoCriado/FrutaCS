using System;
using Godot;

namespace FrutaCS.Weapons;

/// <summary>
/// Pure table-driven weapon simulation. Engine-free: only Godot math
/// structs (Vector2), no Node/runtime APIs.
/// <list type="bullet">
/// <item>Recoil is a deterministic per-bullet table lookup (1.6 feel comes
/// from the data; randomness lives only inside the spread cone).</item>
/// <item>Distance falloff mirrors 1.6 FireBullets3:
/// damage * pow(rangeModifier, distU / 500).</item>
/// <item>Wall penetration is thickness-gated: walls thicker than
/// MaxWallThicknessU stop the bullet, thinner ones keep 60% damage
/// (1.6 CHAR_TEX_WOOD modifier).</item>
/// <item>Shot timing accumulates elapsed time (double) and derives the
/// bullet index from it, so any fixed-step split (60x1/60 vs 120x1/120)
/// lands on the same bullet.</item>
/// </list>
/// Bullet indices are 1-based (bullet 1 = first trigger pull).
/// </summary>
public sealed class WeaponSim
{
    /// <summary>1.6 wood wall damage retention (FireBullets3 CHAR_TEX_WOOD).</summary>
    public const float WallDamageMult = 0.6f;

    /// <summary>Distance block over which the range modifier applies (1.6).</summary>
    public const float RangeBlockU = 500f;

    private readonly WeaponStats _stats;
    private double _elapsedSec;

    public WeaponSim(WeaponStats stats)
    {
        _stats = stats;
        _elapsedSec = 0.0;
    }

    /// <summary>Advance the fire clock. Pure accumulation: fps-independent.</summary>
    public void Tick(float delta)
    {
        if (delta > 0f)
            _elapsedSec += delta;
    }

    public void Reset() => _elapsedSec = 0.0;

    /// <summary>Seconds between shots at this weapon's cyclic rate (0 if manual).</summary>
    public float ShotIntervalSec => _stats.Rpm > 0 ? 60f / _stats.Rpm : 0f;

    /// <summary>
    /// Shots fired by elapsed time at cyclic rate, clamped to [0, MagSize].
    /// Manual weapons (Rpm 0) and empty magazines stay at 0.
    /// </summary>
    public int CurrentBulletIndex
    {
        get
        {
            float interval = ShotIntervalSec;
            if (interval <= 0f || _stats.MagSize <= 0)
                return 0;
            int n = (int)(_elapsedSec / interval);
            return Math.Clamp(n, 0, _stats.MagSize);
        }
    }

    public Vector2 CurrentRecoilOffset => RecoilOffset(CurrentBulletIndex);

    /// <summary>
    /// Cumulative aim offset (pitch-up+, yaw-right+, degrees) for a 1-based
    /// bullet index. Index &lt; 1 yields zero; past the table yields the last
    /// entry (full-mag spray held). Empty table yields zero.
    /// </summary>
    public Vector2 RecoilOffset(int bulletIndex)
    {
        Vector2[] table = _stats.RecoilTable;
        if (table == null || table.Length == 0 || bulletIndex < 1)
            return Vector2.Zero;
        return table[Math.Min(bulletIndex, table.Length) - 1];
    }

    public float SpreadDeg(Stance stance) => stance switch
    {
        Stance.Stand => _stats.SpreadStandDeg,
        Stance.Move => _stats.SpreadMoveDeg,
        Stance.Air => _stats.SpreadAirDeg,
        Stance.Duck => _stats.SpreadDuckDeg,
        _ => _stats.SpreadStandDeg,
    };

    /// <summary>
    /// Body damage at distance, optionally through one wall of wallU thickness.
    /// Truncates like the 1.6 int damage path. Melee weapons (MeleeRangeU &gt; 0)
    /// deal full damage inside reach, nothing beyond, and never penetrate.
    /// </summary>
    public int DamageAt(float distU, bool wall, float wallU)
        => DamageAt(distU, wall ? 1 : 0, wallU, false);

    /// <summary>
    /// Secondary-attack path (knife fast slash reads <see cref="WeaponStats.SecondaryDamage"/>).
    /// Rifles/AWP leave SecondaryDamage at 0, so secondary fire deals nothing.
    /// </summary>
    public int DamageAt(float distU, bool wall, float wallU, bool secondaryAttack)
        => DamageAt(distU, wall ? 1 : 0, wallU, secondaryAttack);

    /// <summary>
    /// Staged wall penetration: each wall crossed keeps
    /// <see cref="WallDamageMult"/> (1.6 CHAR_TEX_WOOD), gated per wall by
    /// MaxWallThicknessU. More walls than <see cref="WeaponStats.PenetrationStages"/>
    /// stops the bullet (1.6 FireBullets3 stage count). Melee never penetrates.
    /// wallU is the thickest wall crossed (conservative single-value gate).
    /// </summary>
    public int DamageAt(float distU, int walls, float wallU)
        => DamageAt(distU, walls, wallU, false);

    public int DamageAt(float distU, int walls, float wallU, bool secondaryAttack)
    {
        if (distU < 0f)
            distU = 0f;
        if (walls < 0)
            walls = 0;
        float baseDamage = secondaryAttack ? _stats.SecondaryDamage : _stats.Damage;
        if (_stats.MeleeRangeU > 0f)
        {
            if (walls > 0 || distU > _stats.MeleeRangeU)
                return 0;
            return (int)baseDamage;
        }
        float dmg = baseDamage * MathF.Pow(_stats.RangeModifier, distU / RangeBlockU);
        if (walls > 0)
        {
            if (wallU < 0f)
                wallU = 0f;
            if (_stats.MaxWallThicknessU <= 0f || wallU > _stats.MaxWallThicknessU)
                return 0;
            if (_stats.PenetrationStages <= 0 || walls > _stats.PenetrationStages)
                return 0;
            dmg *= MathF.Pow(WallDamageMult, walls);
        }
        return Math.Max(0, (int)dmg);
    }
}
