using System;
using System.Collections.Generic;
using System.IO;
using GdUnit4;

using FrutaCS.Game;

namespace FrutaCS.Tests;

/// <summary>
/// Task 7 round/match-clock checks. RoundManager is engine-free, so the
/// full flow (elimination wins, round-timer tiebreak, overtime, golden
/// round) runs under plain `dotnet test`. Scene-shape assertion parses
/// `src/game/Game.tscn` as text (same pattern as BotBrainTests).
/// Rules (spec §6): 10-minute match, most rounds wins, tie → sudden-death
/// golden round, 2-minute round timer with most-alive tiebreak (draw when
/// equal), no buy/economy anywhere. An in-flight round always counts when
/// the match clock expires mid-round (overtime until it resolves).
/// </summary>
[TestSuite]
public class RoundManagerTests
{
    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FrutaCS.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir == null)
            throw new InvalidOperationException("repo root (FrutaCS.sln) not found");
        return dir;
    }

    private static void WinRound(RoundManager m, Team winner)
    {
        Team victim = winner == Team.CT ? Team.T : Team.CT;
        for (int i = 0; i < 7; i++)
            m.OnKill(victim);
    }

    [TestCase]
    public void MostRoundsWinsAtClock()
    {
        // 6-4 CT at 10:00 -> CT wins the match.
        var m = new RoundManager();
        m.StartMatch();
        for (int i = 0; i < 6; i++)
            WinRound(m, Team.CT);
        for (int i = 0; i < 4; i++)
            WinRound(m, Team.T);
        Assertions.AssertThat(m.ScoreCT).IsEqual(6);
        Assertions.AssertThat(m.ScoreT).IsEqual(4);
        m.Update(600f); // Clock expires mid-round -> overtime, round plays out.
        m.Update(120f); // Overtime round timer expires 7v7 -> draw, match closes.
        Assertions.AssertThat(m.Phase).IsEqual(MatchPhase.Finished);
        Assertions.AssertThat(m.Winner).IsEqual(MatchWinner.CT);
        Assertions.AssertThat(m.ScoreCT).IsEqual(6);
        Assertions.AssertThat(m.ScoreT).IsEqual(4);
    }

    [TestCase]
    public void TieTriggersGoldenRound()
    {
        // 5-5 at 10:00 -> sudden-death golden round, winner takes the match.
        var m = new RoundManager();
        m.StartMatch();
        for (int i = 0; i < 5; i++)
            WinRound(m, Team.CT);
        for (int i = 0; i < 5; i++)
            WinRound(m, Team.T);
        m.Update(600f);
        m.Update(120f);
        Assertions.AssertThat(m.ScoreCT).IsEqual(5);
        Assertions.AssertThat(m.ScoreT).IsEqual(5);
        Assertions.AssertThat(m.Phase).IsEqual(MatchPhase.GoldenRound);
        WinRound(m, Team.CT);
        Assertions.AssertThat(m.Phase).IsEqual(MatchPhase.Finished);
        Assertions.AssertThat(m.Winner).IsEqual(MatchWinner.CT);
        Assertions.AssertThat(m.ScoreCT).IsEqual(6);
    }

    [TestCase]
    public void RoundTimerMostAliveWins()
    {
        // Round timer expires with 4 CT vs 2 T alive -> CT wins the round.
        var m = new RoundManager();
        m.StartMatch();
        for (int i = 0; i < 3; i++)
            m.OnKill(Team.CT); // 7 -> 4 alive.
        for (int i = 0; i < 5; i++)
            m.OnKill(Team.T); // 7 -> 2 alive.
        Assertions.AssertThat(m.AliveCT).IsEqual(4);
        Assertions.AssertThat(m.AliveT).IsEqual(2);
        m.Update(120f);
        Assertions.AssertThat(m.ScoreCT).IsEqual(1);
        Assertions.AssertThat(m.ScoreT).IsEqual(0);
        Assertions.AssertThat(m.Phase).IsEqual(MatchPhase.Live);
    }

    [TestCase]
    public void InFlightRoundCounts()
    {
        // Deciding kill at 9:59.9 lands before the 10:00 close and counts.
        var m = new RoundManager();
        m.StartMatch();
        m.Update(599.9f);
        Assertions.AssertThat(m.TimeLeft > 0f && m.TimeLeft < 1f).IsTrue();
        WinRound(m, Team.CT); // Round win 0.1 s before the clock expires.
        Assertions.AssertThat(m.ScoreCT).IsEqual(1);
        m.Update(0.2f); // Clock expires mid-round -> overtime.
        m.Update(120f); // Overtime round resolves -> match closes 1-0.
        Assertions.AssertThat(m.Phase).IsEqual(MatchPhase.Finished);
        Assertions.AssertThat(m.Winner).IsEqual(MatchWinner.CT);
        Assertions.AssertThat(m.ScoreCT).IsEqual(1);
    }

    [TestCase]
    public void PhaseChangedFiresOnTransitions()
    {
        var m = new RoundManager();
        var seen = new List<MatchPhase>();
        m.PhaseChanged += seen.Add;
        m.StartMatch(1); // 1v1: a single kill decides a round.
        Assertions.AssertThat(seen.Count).IsEqual(1);
        Assertions.AssertThat(seen[0]).IsEqual(MatchPhase.Live);
        m.OnKill(Team.T); // Round win, match clock still live: no phase change.
        Assertions.AssertThat(seen.Count).IsEqual(1);
        m.Update(600f);
        m.Update(120f); // 1-0 -> match closes.
        Assertions.AssertThat(m.Phase).IsEqual(MatchPhase.Finished);
        Assertions.AssertThat(seen.Count).IsEqual(2);
        Assertions.AssertThat(seen[1]).IsEqual(MatchPhase.Finished);
    }

    [TestCase]
    public void GameSceneShape()
    {
        string tscn = File.ReadAllText(Path.Combine(RepoRoot(), "src", "game", "Game.tscn"));
        Assertions.AssertThat(tscn.Contains("res://src/game/MatchManager.cs")).IsTrue();
    }
}
