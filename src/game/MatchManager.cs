using System;
using System.Collections.Generic;
using Godot;

using FrutaCS.Bots;
using FrutaCS.Maps;
using FrutaCS.Player;

namespace FrutaCS.Game;

/// <summary>
/// Thin scene glue over the pure <see cref="RoundManager"/> and
/// <see cref="VoteManager"/> (both engine-free and unit-tested). Owns the
/// engine-side concerns only: ticking both clocks in <c>_Process</c>,
/// spawning the player + 13 bots through the <see cref="MapConfig"/>
/// (player takes SpawnsCT[0], CT bots the other six, T bots all seven),
/// applying the per-spawn difficulty mix before the bots wake up, and
/// resetting the round on <see cref="RoundManager.RoundStarted"/> —
/// fighters back to spawns with the base sidearm, floor guns respawned.
/// Deaths arrive via the <c>match_manager</c> group
/// (<see cref="OnFighterDown"/>) and forward into
/// <see cref="RoundManager.OnKill"/>; lethal hits also report
/// (killer, victim) via <see cref="OnFighterFrag"/> for the scoreboard.
/// On <see cref="MatchPhase.Finished"/> it opens the end-of-match map
/// vote (current map offered as rematch); bot ballots trickle in so the
/// vote closes even if the human never votes. No buy/economy anywhere.
/// </summary>
public partial class MatchManager : Node
{
    private static readonly Vector3 MapCenter = Vector3.Zero;

    /// <summary>Scoreboard names for the 13 bots (6 CT, then 7 T).</summary>
    private static readonly string[] BotNames = new string[]
    {
        "Naranja", "Limon", "Manzana", "Pera", "Uva", "Sandia",
        "Melon", "Kiwi", "Mango", "Frutilla", "Banana", "Cereza", "Durazno",
    };

    public RoundManager Rounds { get; } = new();
    public VoteManager Vote { get; } = new();

    [Export] public int PlayersPerTeam = RoundManager.DefaultPlayersPerTeam;
    [Export] public MapConfig Map;
    [Export] public PackedScene PlayerScene;
    [Export] public PackedScene BotScene;

    private readonly List<Node3D> _fighters = new();
    private readonly Dictionary<Node3D, Vector3> _spawns = new();
    private readonly Dictionary<Node3D, int> _frags = new();
    private readonly Dictionary<Node3D, int> _deaths = new();
    private readonly HashSet<Node3D> _botVoted = new();
    private float _botVoteDelaySec;
    private PlayerBody _player;
    private Node3D _roster;

    public IReadOnlyList<Node3D> Fighters => _fighters;

    /// <summary>Scoreboard frags for a tracked fighter (0 when unknown).</summary>
    public int FragsOf(Node3D fighter) => _frags.TryGetValue(fighter, out int n) ? n : 0;

    /// <summary>Scoreboard deaths for a tracked fighter (0 when unknown).</summary>
    public int DeathsOf(Node3D fighter) => _deaths.TryGetValue(fighter, out int n) ? n : 0;

    public override void _Ready()
    {
        AddToGroup("match_manager");
        Map ??= new MapConfig();
        // Main-thread weapon warmup BEFORE any physics tick can grab:
        // first-load peer setup must not race the physics callbacks.
        FrutaCS.Weapons.WeaponData.PreloadAll();
        _roster = new Node3D { Name = "Fighters" };
        AddChild(_roster);
        // Subscribe BEFORE StartMatch: BeginRound fires RoundStarted
        // synchronously (including the first round), and a late
        // subscription would silently miss it.
        Rounds.RoundStarted += ResetRound;
        Rounds.PhaseChanged += OnPhaseChanged;
        Rounds.StartMatch(PlayersPerTeam);
        SpawnAll();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Rounds.Update(dt);
        Vote.Update(dt);
        TrickleBotVotes(dt);
    }

    /// <summary>
    /// Report a death from engine code (fighters call this via group on
    /// death). victimTeam: 0 = CT, anything else = T.
    /// </summary>
    public void ReportKill(int victimTeam) => Rounds.OnKill(victimTeam == 0 ? Team.CT : Team.T);

    /// <summary>Group entry point for dying fighters: counts the death
    /// for the scoreboard and forwards the team score into
    /// <see cref="RoundManager.OnKill"/>.</summary>
    public void OnFighterDown(int victimTeam, Node _)
    {
        if (_ is Node3D victim && _deaths.ContainsKey(victim))
            _deaths[victim]++;
        ReportKill(victimTeam);
    }

    /// <summary>
    /// Group entry point for kill credit: shooters (player via
    /// WeaponSystem, bots via their own fire) report (killer, victim)
    /// after a lethal hit. Feeds the scoreboard frag column only; the
    /// team score still flows through <see cref="OnFighterDown"/>.
    /// Unknown killers (never a tracked fighter) are ignored.
    /// </summary>
    public void OnFighterFrag(Node killer, Node victim)
    {
        if (killer is Node3D k && _frags.ContainsKey(k))
            _frags[k]++;
    }

    private void SpawnAll()
    {
        if (PlayerScene == null || BotScene == null)
        {
            GD.PrintErr("[MatchManager] PlayerScene/BotScene not wired; spawning skipped.");
            return;
        }
        var sidearm = FrutaCS.Weapons.WeaponData.Get("deagle");
        var player = PlayerScene.Instantiate<PlayerBody>();
        player.Name = "Vos";
        player.Team = 0;
        _player = player;
        _roster.AddChild(player);
        player.GlobalPosition = Map.SpawnsCT[0];
        FaceCenter(player, Map.SpawnsCT[0]);
        if (sidearm != null)
            player.ArmedWeapon?.Equip(sidearm);
        Track(player, Map.SpawnsCT[0]);

        for (int i = 0; i < 6; i++)
            SpawnBot(0, Map.SpawnsCT[i + 1], Map.MixCT[i + 1], BotNames[i]);
        for (int i = 0; i < 7; i++)
            SpawnBot(1, Map.SpawnsT[i], Map.MixT[i], BotNames[6 + i]);
    }

    private void SpawnBot(int team, Vector3 pos, BotDifficulty mix, string botName)
    {
        var bot = BotScene.Instantiate<Bot>();
        bot.Name = botName;
        bot.Team = team;
        if (mix != null)
            bot.ApplyMix(mix.ToBotParams());
        _roster.AddChild(bot);
        bot.GlobalPosition = pos;
        FaceCenter(bot, pos);
        Track(bot, pos);
    }

    private void Track(Node3D fighter, Vector3 spawn)
    {
        _fighters.Add(fighter);
        _spawns[fighter] = spawn;
        _frags.TryAdd(fighter, 0);
        _deaths.TryAdd(fighter, 0);
    }

    /// <summary>
    /// Round reset per the map config: everyone back to their spawn with
    /// the base sidearm at full ammo, floor guns back out. Runs on every
    /// <see cref="RoundManager.RoundStarted"/>, including golden rounds.
    /// </summary>
    public void ResetRound()
    {
        var sidearm = FrutaCS.Weapons.WeaponData.Get("deagle");
        foreach (Node3D fighter in _fighters)
        {
            Vector3 spawn = _spawns[fighter];
            if (fighter is PlayerBody player)
            {
                player.Respawn(spawn);
                FaceCenter(player, spawn);
                if (sidearm != null)
                    player.ArmedWeapon?.Equip(sidearm);
            }
            else if (fighter is Bot bot)
            {
                bot.Respawn(spawn, MapCenter);
            }
        }
        foreach (Node node in GetTree().GetNodesInGroup("weapon_pickups"))
        {
            if (node is WeaponPickup pickup)
                pickup.ResetPickup();
        }
    }

    private static void FaceCenter(Node3D fighter, Vector3 pos)
    {
        Vector3 face = MapCenter - pos;
        face.Y = 0f;
        if (face.Length() > 1f)
            fighter.Rotation = new Vector3(0f, Mathf.Atan2(-face.X, -face.Z), 0f);
    }

    /// <summary>
    /// End of match: open the map vote (milestone 1 offers the current
    /// map back as a rematch — a one-map pool). Bot ballots trickle in
    /// over <see cref="TrickleBotVotes"/> so the panel shows live counts
    /// and closes even if the human never votes.
    /// </summary>
    private void OnPhaseChanged(MatchPhase phase)
    {
        if (phase != MatchPhase.Finished)
            return;
        _botVoted.Clear();
        _botVoteDelaySec = 6f;
        Vote.StartVote(
            new List<string> { Map.MapId },
            Math.Max(1, _fighters.Count),
            VoteManager.DefaultDurationSec);
    }

    /// <summary>
    /// Bots vote like the human does (one ballot each, random option),
    /// staggered: a grace delay first so the human can open the panel
    /// and vote before the majority lands, then a slow trickle.
    /// The local player is excluded (votes through the HUD instead).
    /// </summary>
    private void TrickleBotVotes(float dt)
    {
        if (!Vote.IsOpen || Vote.Options.Count == 0)
            return;
        if (_botVoteDelaySec > 0f)
        {
            _botVoteDelaySec -= dt;
            return;
        }
        foreach (Node3D fighter in _fighters)
        {
            if (fighter == (Node3D)_player || _botVoted.Contains(fighter))
                continue;
            if (GD.Randf() >= dt * 0.3f)
                continue;
            string option = Vote.Options[(int)(GD.Randi() % (uint)Vote.Options.Count)];
            Vote.CastVote(fighter.Name, option);
            _botVoted.Add(fighter);
        }
    }
}
