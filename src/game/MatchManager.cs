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
/// <see cref="RoundManager.OnKill"/>. No buy/economy anywhere.
/// </summary>
public partial class MatchManager : Node
{
    private static readonly Vector3 MapCenter = Vector3.Zero;

    public RoundManager Rounds { get; } = new();
    public VoteManager Vote { get; } = new();

    [Export] public int PlayersPerTeam = RoundManager.DefaultPlayersPerTeam;
    [Export] public MapConfig Map;
    [Export] public PackedScene PlayerScene;
    [Export] public PackedScene BotScene;

    private readonly List<Node3D> _fighters = new();
    private readonly Dictionary<Node3D, Vector3> _spawns = new();
    private Node3D _roster;

    public IReadOnlyList<Node3D> Fighters => _fighters;

    public override void _Ready()
    {
        AddToGroup("match_manager");
        Map ??= new MapConfig();
        _roster = new Node3D { Name = "Fighters" };
        AddChild(_roster);
        // Subscribe BEFORE StartMatch: BeginRound fires RoundStarted
        // synchronously (including the first round), and a late
        // subscription would silently miss it.
        Rounds.RoundStarted += ResetRound;
        Rounds.StartMatch(PlayersPerTeam);
        SpawnAll();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Rounds.Update(dt);
        Vote.Update(dt);
    }

    /// <summary>
    /// Report a death from engine code (fighters call this via group on
    /// death). victimTeam: 0 = CT, anything else = T.
    /// </summary>
    public void ReportKill(int victimTeam) => Rounds.OnKill(victimTeam == 0 ? Team.CT : Team.T);

    /// <summary>Group entry point for dying fighters (node arg is unused;
    /// the kill is already resolved — only the team score matters).</summary>
    public void OnFighterDown(int victimTeam, Node _)
    {
        ReportKill(victimTeam);
    }

    private void SpawnAll()
    {
        if (PlayerScene == null || BotScene == null)
        {
            GD.PrintErr("[MatchManager] PlayerScene/BotScene not wired; spawning skipped.");
            return;
        }
        var sidearm = GD.Load<FrutaCS.Weapons.WeaponData>("res://data/weapons/deagle.tres");
        var player = PlayerScene.Instantiate<PlayerBody>();
        player.Team = 0;
        _roster.AddChild(player);
        player.GlobalPosition = Map.SpawnsCT[0];
        FaceCenter(player, Map.SpawnsCT[0]);
        if (sidearm != null)
            player.ArmedWeapon?.Equip(sidearm);
        Track(player, Map.SpawnsCT[0]);

        for (int i = 0; i < 6; i++)
            SpawnBot(0, Map.SpawnsCT[i + 1], Map.MixCT[i + 1]);
        for (int i = 0; i < 7; i++)
            SpawnBot(1, Map.SpawnsT[i], Map.MixT[i]);
    }

    private void SpawnBot(int team, Vector3 pos, BotDifficulty mix)
    {
        var bot = BotScene.Instantiate<Bot>();
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
    }

    /// <summary>
    /// Round reset per the map config: everyone back to their spawn with
    /// the base sidearm at full ammo, floor guns back out. Runs on every
    /// <see cref="RoundManager.RoundStarted"/>, including golden rounds.
    /// </summary>
    public void ResetRound()
    {
        var sidearm = GD.Load<FrutaCS.Weapons.WeaponData>("res://data/weapons/deagle.tres");
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
}
