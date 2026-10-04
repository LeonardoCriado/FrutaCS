namespace FrutaCS.Bots;

/// <summary>
/// Difficulty + sensing constants for one bot. ReactionSec, AimErrorDeg and
/// BurstLen are the per-map difficulty knobs (spec §6): the brain consumes
/// ReactionSec, the node glue consumes AimErrorDeg (fire-time aim cone) and
/// BurstLen (shots before a pause). The remaining fields are sensing ranges
/// with milestone-1 defaults; LoseSightSec = 5 s pins the brief's
/// "no stimulus for 5 s -> Patrol" rule.
/// </summary>
public struct BotParams
{
    public float ReactionSec;
    public float AimErrorDeg;
    public int BurstLen;
    public float SightRangeU;
    public float SightFovDeg;
    public float AttackRangeU;
    public float LoseSightSec;
    public float HearingRadiusU;

    /// <summary>
    /// Pickup interruption radius (map 1 default 400u): a visible enemy
    /// inside this range breaks off a gun run (immediate threat); farther
    /// sightings don't (grab first, fight armed). Zero or negative keeps
    /// the legacy hair-trigger (any sighting interrupts).
    /// </summary>
    public float ThreatRadiusU;

    public static BotParams Default => new()
    {
        ReactionSec = 0.4f,
        AimErrorDeg = 3f,
        BurstLen = 3,
        SightRangeU = 1500f,
        SightFovDeg = 90f,
        AttackRangeU = 1000f,
        LoseSightSec = 5f,
        HearingRadiusU = 800f,
        ThreatRadiusU = 400f,
    };
}
