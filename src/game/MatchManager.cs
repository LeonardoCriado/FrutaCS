using Godot;

namespace FrutaCS.Game;

/// <summary>
/// Thin scene glue over the pure <see cref="RoundManager"/> and
/// <see cref="VoteManager"/> (both engine-free and unit-tested). Owns the
/// engine-side concerns only: ticking both clocks in <c>_Process</c> and
/// translating engine kill reports into <see cref="RoundManager.OnKill"/>.
/// Spawns, map loadouts, the end-of-match vote panel and scene flow land
/// with the map (Task 8) and UI (Task 9) work; until then this node runs
/// the match clock behind an empty arena. No buy/economy anywhere.
/// </summary>
public partial class MatchManager : Node
{
    public RoundManager Rounds { get; } = new();
    public VoteManager Vote { get; } = new();

    [Export] public int PlayersPerTeam = RoundManager.DefaultPlayersPerTeam;

    public override void _Ready()
    {
        Rounds.StartMatch(PlayersPerTeam);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Rounds.Update(dt);
        Vote.Update(dt);
    }

    /// <summary>
    /// Report a death from engine code. victimTeam: 0 = CT, anything else = T.
    /// </summary>
    public void ReportKill(int victimTeam) => Rounds.OnKill(victimTeam == 0 ? Team.CT : Team.T);
}
