using GdUnit4;

using FrutaCS.Game;

namespace FrutaCS.Tests;

/// <summary>
/// Task 7 map-vote checks. VoteManager is engine-free (majority +
/// timeout-fallback-to-plurality, one vote per voter); milestone 1 uses it
/// for the end-of-match map vote. Bots vote at random (exercises the
/// system); the manager only counts.
/// </summary>
[TestSuite]
public class VoteManagerTests
{
    private static readonly string[] Maps = { "dust", "mirage", "pileta" };

    [TestCase]
    public void VoteMajorityWinsImmediately()
    {
        // 4/7 for dust -> dust wins at once (> 50% of eligible voters).
        var v = new VoteManager();
        v.StartVote(Maps, eligibleVoters: 7, durationSec: 30f);
        Assertions.AssertThat(v.CastVote("p0", "dust")).IsTrue();
        Assertions.AssertThat(v.CastVote("p1", "dust")).IsTrue();
        Assertions.AssertThat(v.CastVote("p2", "mirage")).IsTrue();
        Assertions.AssertThat(v.CastVote("p3", "dust")).IsTrue();
        Assertions.AssertThat(v.IsOpen).IsTrue();
        Assertions.AssertThat(v.Winner).IsEqual(null);
        Assertions.AssertThat(v.CastVote("p4", "dust")).IsTrue();
        Assertions.AssertThat(v.IsOpen).IsFalse();
        Assertions.AssertThat(v.Winner).IsEqual("dust");
        Assertions.AssertThat(v.Tally()).IsEqual("dust");
        // Closed vote rejects further ballots.
        Assertions.AssertThat(v.CastVote("p5", "mirage")).IsFalse();
    }

    [TestCase]
    public void VoteTimeoutFallsBackToPlurality()
    {
        // No majority before the timer -> plurality wins; exact tie goes to
        // the option listed first (deterministic).
        var v = new VoteManager();
        v.StartVote(Maps, eligibleVoters: 7, durationSec: 5f);
        v.CastVote("p0", "dust");
        v.CastVote("p1", "dust");
        v.CastVote("p2", "mirage");
        v.Update(4.9f);
        Assertions.AssertThat(v.IsOpen).IsTrue();
        Assertions.AssertThat(v.Winner).IsEqual(null);
        v.Update(0.2f);
        Assertions.AssertThat(v.IsOpen).IsFalse();
        Assertions.AssertThat(v.Winner).IsEqual("dust");
        Assertions.AssertThat(v.Tally()).IsEqual("dust");

        var tie = new VoteManager();
        tie.StartVote(Maps, eligibleVoters: 7, durationSec: 5f);
        tie.CastVote("p0", "mirage");
        tie.CastVote("p1", "dust");
        tie.Update(5f);
        Assertions.AssertThat(tie.Winner).IsEqual("dust"); // Listed before mirage.
    }

    [TestCase]
    public void VoteRejectsInvalidAndCountsOnePerVoter()
    {
        var v = new VoteManager();
        var closed = new System.Collections.Generic.List<string?>();
        v.VoteClosed += closed.Add;
        v.StartVote(Maps, eligibleVoters: 7, durationSec: 30f);
        Assertions.AssertThat(v.CastVote("p0", "office")).IsFalse(); // Not an option.
        Assertions.AssertThat(v.CastVote("p0", "dust")).IsTrue();
        Assertions.AssertThat(v.VotesFor("dust")).IsEqual(1);
        v.CastVote("p0", "mirage"); // Re-vote moves the ballot, no double count.
        Assertions.AssertThat(v.VotesFor("dust")).IsEqual(0);
        Assertions.AssertThat(v.VotesFor("mirage")).IsEqual(1);
        v.Update(30f); // Timeout with a single ballot -> mirage by plurality.
        Assertions.AssertThat(v.Winner).IsEqual("mirage");
        Assertions.AssertThat(closed.Count).IsEqual(1);
        Assertions.AssertThat(closed[0]).IsEqual("mirage");
    }
}
