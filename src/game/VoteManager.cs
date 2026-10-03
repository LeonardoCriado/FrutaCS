using System;
using System.Collections.Generic;

namespace FrutaCS.Game;

/// <summary>
/// Generic vote logic (milestone 1 = end-of-match map vote; reusable for
/// kick/restart later). Engine-free: closure via the <see cref="VoteClosed"/>
/// C# event. Rules: an option exceeding 50% of eligible voters wins
/// immediately (majority); otherwise the timer expiry falls back to
/// plurality (most ballots). Exact plurality ties break to the option
/// listed first (deterministic). One ballot per voter id; re-voting moves
/// the ballot without double counting. Ballots for unknown options and
/// ballots after close are rejected.
/// </summary>
public sealed class VoteManager
{
    /// <summary>Placeholder duration when the caller passes none.</summary>
    public const float DefaultDurationSec = 30f;

    /// <summary>Fires once per vote with the winning option (null: no ballots).</summary>
    public event Action<string?>? VoteClosed;

    public bool IsOpen { get; private set; }

    /// <summary>Winning option, null until the vote closes.</summary>
    public string? Winner { get; private set; }

    /// <summary>Vote timer remaining.</summary>
    public float TimeLeft { get; private set; }

    private readonly List<string> _options = new();
    private readonly Dictionary<string, string> _ballots = new();
    private readonly Dictionary<string, int> _counts = new();
    private int _eligibleVoters;

    /// <summary>Open a vote over <paramref name="options"/> (order = tie-break priority).</summary>
    public void StartVote(IReadOnlyList<string> options, int eligibleVoters, float durationSec = DefaultDurationSec)
    {
        if (options == null || options.Count == 0)
            throw new ArgumentException("Vote needs at least one option.", nameof(options));
        _options.Clear();
        _options.AddRange(options);
        _counts.Clear();
        foreach (string option in _options)
            _counts.TryAdd(option, 0);
        _ballots.Clear();
        _eligibleVoters = Math.Max(1, eligibleVoters);
        TimeLeft = Math.Max(0.01f, durationSec);
        Winner = null;
        IsOpen = true;
    }

    /// <summary>
    /// Cast (or move) one voter's ballot. Majority closes the vote at once.
    /// Returns false when the vote is closed or the option is unknown.
    /// </summary>
    public bool CastVote(string voterId, string option)
    {
        if (!IsOpen || voterId == null || !_counts.ContainsKey(option))
            return false;
        if (_ballots.TryGetValue(voterId, out string? previous))
        {
            if (previous == option)
                return true;
            _counts[previous]--;
        }
        _ballots[voterId] = option;
        _counts[option]++;
        string? majority = MajorityLeader();
        if (majority != null)
            Close(majority);
        return true;
    }

    /// <summary>
    /// Current result: the closed winner, else the live majority leader
    /// (null while the vote is still open without a majority).
    /// </summary>
    public string? Tally() => Winner ?? (IsOpen ? MajorityLeader() : PluralityLeader());

    /// <summary>Ballots currently held by <paramref name="option"/>.</summary>
    public int VotesFor(string option) => _counts.TryGetValue(option, out int n) ? n : 0;

    /// <summary>Advance the vote timer; expiry closes by plurality fallback.</summary>
    public void Update(float delta)
    {
        if (!IsOpen || delta <= 0f)
            return;
        TimeLeft -= delta;
        if (TimeLeft <= 0f)
        {
            TimeLeft = 0f;
            Close(PluralityLeader());
        }
    }

    private string? MajorityLeader()
    {
        foreach (string option in _options)
        {
            if (_counts[option] * 2 > _eligibleVoters)
                return option;
        }
        return null;
    }

    private string? PluralityLeader()
    {
        string? best = null;
        int bestVotes = 0;
        foreach (string option in _options)
        {
            if (_counts[option] > bestVotes)
            {
                best = option;
                bestVotes = _counts[option];
            }
        }
        return best;
    }

    private void Close(string? winner)
    {
        Winner = winner;
        IsOpen = false;
        VoteClosed?.Invoke(winner);
    }
}
