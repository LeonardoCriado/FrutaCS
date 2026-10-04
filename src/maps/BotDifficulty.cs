using Godot;

namespace FrutaCS.Maps;

/// <summary>
/// Per-bot difficulty entry owned by the map layer (spec §6: the map
/// defines the mix). Editable Godot Resource; converts to the pure
/// <see cref="FrutaCS.Bots.BotParams"/> struct via <see cref="ToBotParams"/>.
/// Sensing ranges ride the milestone-1 defaults; the three difficulty
/// knobs (reaction, aim error, burst length) are the per-spawn mix data.
/// </summary>
[GlobalClass]
public partial class BotDifficulty : Resource
{
    [Export] public float ReactionSec = 0.4f;
    [Export] public float AimErrorDeg = 3f;
    [Export] public int BurstLen = 3;
    [Export] public float ThreatRadiusU = 400f;

    public FrutaCS.Bots.BotParams ToBotParams() => new()
    {
        ReactionSec = ReactionSec,
        AimErrorDeg = AimErrorDeg,
        BurstLen = BurstLen,
        SightRangeU = FrutaCS.Bots.BotParams.Default.SightRangeU,
        SightFovDeg = FrutaCS.Bots.BotParams.Default.SightFovDeg,
        AttackRangeU = FrutaCS.Bots.BotParams.Default.AttackRangeU,
        LoseSightSec = FrutaCS.Bots.BotParams.Default.LoseSightSec,
        HearingRadiusU = FrutaCS.Bots.BotParams.Default.HearingRadiusU,
        ThreatRadiusU = ThreatRadiusU,
    };
}
