using System;

namespace FrutaCS.Game;

/// <summary>Competing sides. CT listings always come first (scoreboard order).</summary>
public enum Team
{
    CT,
    T,
}

/// <summary>
/// Match lifecycle. Live = regulation rounds on the match clock;
/// GoldenRound = sudden-death decider after a tied clock; Finished = winner
/// known. There is no round-end phase: the next round starts immediately
/// when one resolves, so a kill landing before the clock expires always
/// counts (no score-attribution race).
/// </summary>
public enum MatchPhase
{
    Idle,
    Live,
    GoldenRound,
    Finished,
}

/// <summary>Match outcome. None while the match is running.</summary>
public enum MatchWinner
{
    None,
    CT,
    T,
}

/// <summary>
/// Pure elimination-round + match-clock logic (spec §6). Engine-free: only
/// floats/ints/enums, phase changes via the <see cref="PhaseChanged"/> C#
/// event. No Node/SceneTree APIs; no buy/economy anywhere.
/// <list type="bullet">
/// <item>Match clock: 10 minutes (<see cref="MatchDurationSec"/>). Most
/// rounds at the close wins; a tie opens a sudden-death golden round.</item>
/// <item>Round timer: 2 minutes (<see cref="RoundDurationSec"/>) anti-camper.
/// Expiry awards the round to the team with more players alive; equal alive
/// is a drawn round (no point). A regulation draw never ends the match by
/// itself; a golden-round draw replays sudden death until decided.</item>
/// <item>In-flight round: when the match clock expires mid-round, the live
/// round plays out (overtime, bounded by the round timer) and counts before
/// the match closes. Round and match expiries landing on the same tick
/// resolve the round first.</item>
/// </list>
/// </summary>
public sealed class RoundManager
{
    /// <summary>Match clock in seconds (spec §6: 10-minute match).</summary>
    public const float MatchDurationSec = 600f;

    /// <summary>Round timer in seconds (spec §6: 2-minute anti-camper).</summary>
    public const float RoundDurationSec = 120f;

    /// <summary>Milestone-1 format: 7v7 (player + 13 bots... human + bots).</summary>
    public const int DefaultPlayersPerTeam = 7;

    /// <summary>Fires on every phase transition (Idle→Live, →GoldenRound, →Finished).</summary>
    public event Action<MatchPhase>? PhaseChanged;

    /// <summary>
    /// Fires at the start of every round, including the first (BeginRound
    /// runs inside StartMatch) and the golden-round replays. The map glue
    /// (spawns, loadouts, pickup respawn) subscribes to this; RoundManager
    /// itself stays engine-free.
    /// </summary>
    public event Action? RoundStarted;

    public MatchPhase Phase { get; private set; } = MatchPhase.Idle;
    public MatchWinner Winner { get; private set; } = MatchWinner.None;
    public int ScoreCT { get; private set; }
    public int ScoreT { get; private set; }

    /// <summary>Match clock remaining, clamped at zero once expired.</summary>
    public float TimeLeft { get; private set; }

    /// <summary>Current round timer remaining.</summary>
    public float RoundTimeLeft { get; private set; }
    public int AliveCT { get; private set; }
    public int AliveT { get; private set; }

    private int _playersPerTeam = DefaultPlayersPerTeam;
    private bool _matchExpired;

    /// <summary>Reset scores/clocks and open the first round.</summary>
    public void StartMatch(int playersPerTeam = DefaultPlayersPerTeam)
    {
        _playersPerTeam = Math.Max(1, playersPerTeam);
        ScoreCT = 0;
        ScoreT = 0;
        Winner = MatchWinner.None;
        TimeLeft = MatchDurationSec;
        _matchExpired = false;
        SetPhase(MatchPhase.Live);
        BeginRound();
    }

    /// <summary>
    /// Report a death on <paramref name="victimTeam"/>. Eliminating a side
    /// awards the round immediately (in-flight results count even when the
    /// match clock is about to expire). Ignored outside a live round.
    /// </summary>
    public void OnKill(Team victimTeam)
    {
        if (Phase != MatchPhase.Live && Phase != MatchPhase.GoldenRound)
            return;
        if (victimTeam == Team.CT)
        {
            if (AliveCT > 0)
                AliveCT--;
        }
        else if (AliveT > 0)
        {
            AliveT--;
        }

        if (AliveCT == 0)
            AwardRound(Team.T);
        else if (AliveT == 0)
            AwardRound(Team.CT);
    }

    /// <summary>
    /// Advance both clocks. Time-sliced so one call may resolve several
    /// round timers (tick-size independent, same philosophy as WeaponSim).
    /// </summary>
    public void Update(float delta)
    {
        if (delta <= 0f || Phase == MatchPhase.Idle || Phase == MatchPhase.Finished)
            return;
        float remaining = delta;
        while (remaining > 0f && (Phase == MatchPhase.Live || Phase == MatchPhase.GoldenRound))
        {
            float step = RoundTimeLeft;
            if (!_matchExpired && TimeLeft < step)
                step = TimeLeft;
            if (step > remaining)
                step = remaining;
            if (step <= 0f)
                break; // Float dust guard; state below is already terminal.
            RoundTimeLeft -= step;
            if (!_matchExpired)
                TimeLeft = Math.Max(0f, TimeLeft - step);
            remaining -= step;
            if (RoundTimeLeft <= 0f)
                ResolveRoundByAlive();
            // Match expiry is latched even when the round resolves on the
            // same tick (the in-flight round counted above via AwardRound);
            // the next resolution then closes the match.
            if (!_matchExpired && TimeLeft <= 0f)
                _matchExpired = true; // Overtime: live round plays out.
        }
    }

    /// <summary>Round timer expired: most alive takes it, equal alive draws.</summary>
    private void ResolveRoundByAlive()
    {
        if (AliveCT > AliveT)
            AwardRound(Team.CT);
        else if (AliveT > AliveCT)
            AwardRound(Team.T);
        else
            AwardRound(null);
    }

    private void AwardRound(Team? roundWinner)
    {
        if (roundWinner == Team.CT)
            ScoreCT++;
        else if (roundWinner == Team.T)
            ScoreT++;

        if (Phase == MatchPhase.GoldenRound)
        {
            // Sudden death: a drawn golden round replays until decided.
            if (roundWinner == Team.CT)
                Finish(MatchWinner.CT);
            else if (roundWinner == Team.T)
                Finish(MatchWinner.T);
            else
                BeginRound();
            return;
        }

        if (_matchExpired)
            CloseMatch();
        else
            BeginRound();
    }

    private void CloseMatch()
    {
        if (ScoreCT != ScoreT)
            Finish(ScoreCT > ScoreT ? MatchWinner.CT : MatchWinner.T);
        else
        {
            SetPhase(MatchPhase.GoldenRound);
            BeginRound();
        }
    }

    private void BeginRound()
    {
        AliveCT = _playersPerTeam;
        AliveT = _playersPerTeam;
        RoundTimeLeft = RoundDurationSec;
        RoundStarted?.Invoke();
    }

    private void Finish(MatchWinner winner)
    {
        Winner = winner;
        SetPhase(MatchPhase.Finished);
    }

    private void SetPhase(MatchPhase phase)
    {
        Phase = phase;
        PhaseChanged?.Invoke(phase);
    }
}
