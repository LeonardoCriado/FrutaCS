using System.Collections.Generic;
using Godot;

using FrutaCS.Bots;
using FrutaCS.Game;
using FrutaCS.Player;
using FrutaCS.Weapons;

namespace FrutaCS.UI;

/// <summary>
/// Live match overlay: health/armor/ammo, round + match clocks, team
/// score, TAB scoreboard and the end-of-match vote panel. Every number
/// is read fresh each frame from the real <see cref="MatchManager"/>
/// (player Hp, weapon mag/reserve, both clocks, scores, per-fighter
/// frags/deaths, open vote): nothing is mocked. Scoreboard rows come
/// from <see cref="MatchManager.Fighters"/> ordered CT-first; bots show
/// "BOT" in the ping column (milestone 1 has no netcode, the local
/// player shows "--"). When the vote closes the scene reloads, which is
/// the map change (milestone 1 pools a single map, offered as rematch).
/// All art is original flat fruta styling; no third-party assets.
/// </summary>
public partial class Hud : CanvasLayer
{
    private const float MsgSec = 3.5f;
    private const float ReloadDelaySec = 3f;

    private MatchManager _match;
    private PlayerBody _player;

    private Label _score;
    private Label _roundTimer;
    private Label _matchClock;
    private Label _health;
    private Label _armor;
    private Label _weapon;
    private Label _ammo;
    private Label _centerMsg;
    private Label _deadMsg;
    private PanelContainer _board;
    private VBoxContainer _ctRows;
    private VBoxContainer _tRows;
    private PanelContainer _votePanel;
    private VBoxContainer _voteOptions;
    private Label _voteTimer;
    private Label _voteResult;
    private PanelContainer _pausePanel;
    private Label _slotsLabel;
    private ColorRect _dmgFlash;
    private Label _threatArrow;
    private AudioStreamPlayer _hurtPlayer;
    private AudioStreamWav _hurtStream;
    private Vector3 _lastThreat = Vector3.Zero;
    private float _threatLeft;
    private float _flashLeft;

    private readonly Dictionary<string, Label> _voteCounts = new();
    private int _boardSig = -1;
    private int _lastScoreCT;
    private int _lastScoreT;
    private float _msgLeft;
    private float _reloadLeft = -1f;
    private bool _voteWasOpen;

    /// <summary>Live match glue (null until _Ready resolves it).</summary>
    public MatchManager Match => _match;

    /// <summary>Local player (null until _Ready resolves it).</summary>
    public PlayerBody PlayerNode => _player;

    /// <summary>Whether the TAB scoreboard is currently shown.</summary>
    public bool ScoreboardOpen => _board != null && _board.Visible;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _score = GetNode<Label>("%ScoreLabel");
        _roundTimer = GetNode<Label>("%RoundTimerLabel");
        _matchClock = GetNode<Label>("%MatchClockLabel");
        _health = GetNode<Label>("%HealthLabel");
        _armor = GetNode<Label>("%ArmorLabel");
        _weapon = GetNode<Label>("%WeaponLabel");
        _ammo = GetNode<Label>("%AmmoLabel");
        _centerMsg = GetNode<Label>("%CenterMsg");
        _deadMsg = GetNode<Label>("%DeadMsg");
        _board = GetNode<PanelContainer>("%Scoreboard");
        _ctRows = GetNode<VBoxContainer>("%CtRows");
        _tRows = GetNode<VBoxContainer>("%TRows");
        _votePanel = GetNode<PanelContainer>("%VotePanel");
        _voteOptions = GetNode<VBoxContainer>("%VoteOptions");
        _voteTimer = GetNode<Label>("%VoteTimer");
        _voteResult = GetNode<Label>("%VoteResult");

        _board.Hide();
        _votePanel.Hide();
        _centerMsg.Hide();
        _deadMsg.Hide();

        _match = GetTree().GetFirstNodeInGroup("match_manager") as MatchManager;
        _player = GetTree().GetFirstNodeInGroup("players") as PlayerBody;
        if (_match == null)
        {
            GD.PrintErr("[Hud] No match_manager found; HUD stays empty.");
            return;
        }
        _lastScoreCT = _match.Rounds.ScoreCT;
        _lastScoreT = _match.Rounds.ScoreT;
        _match.Rounds.PhaseChanged += OnPhaseChanged;
        _match.Vote.VoteClosed += OnVoteClosed;
        OutlineHudTexts();
        BuildHelpLabel();
        BuildDamageFx();
        BuildPausePanel();
        if (_player != null)
            _player.Damaged += OnPlayerDamaged;
        ShowMsg("¡Ronda 1! ¡A la pileta!");
    }

    /// <summary>
    /// Dark outline on every HUD text: the map is bright (white deck/walls,
    /// blue pool) and plain light labels wash out against it.
    /// </summary>
    private void OutlineHudTexts()
    {
        Outline(_score);
        Outline(_roundTimer);
        Outline(_matchClock);
        Outline(_health);
        Outline(_armor);
        Outline(_weapon);
        Outline(_ammo);
        Outline(_centerMsg);
        Outline(_deadMsg);
    }

    private static void Outline(Label label, int size = 8)
    {
        if (label == null)
            return;
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        label.AddThemeConstantOverride("outline_size", size);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            TogglePause();
            GetViewport().SetInputAsHandled(); // PlayerBody must not re-release.
            return;
        }
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;
        if (GetTree().Paused)
            return;
        if (key.PhysicalKeycode == Key.Tab)
            ToggleScoreboard();
        else if (_match != null && _match.Vote.IsOpen && _votePanel.Visible)
            VoteKey(key.PhysicalKeycode);
    }

    /// <summary>ESC: freeze the offline match and offer resume/quit.</summary>
    public void TogglePause()
    {
        if (_match == null || _pausePanel == null)
            return;
        if (GetTree().Paused)
            Resume();
        else
            Pause();
    }

    /// <summary>Freeze timers/bots and show the pause panel.</summary>
    public void Pause()
    {
        _pausePanel.Show();
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Back into the match (vote keeps the mouse when open).</summary>
    public void Resume()
    {
        _pausePanel.Hide();
        GetTree().Paused = false;
        if (_match == null || !_match.Vote.IsOpen)
            Input.MouseMode = Input.MouseModeEnum.Captured;
        else
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void QuitToMenu()
    {
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().ChangeSceneToFile("res://src/ui/MainMenu.tscn");
    }

    private void BuildHelpLabel()
    {        _slotsLabel = new Label { Text = "" };
        _slotsLabel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _slotsLabel.Position = new Vector2(-260f, -52f);
        _slotsLabel.AddThemeFontSizeOverride("font_size", 16);
        _slotsLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f, 1f));
        Outline(_slotsLabel, 6);
        AddChild(_slotsLabel);
        var help = new Label
        {
            Text = "1 Rifle/AWP · 2 Deagle · 3 Cuchillo · Q última · ESC pausa",
        };
        help.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        help.Position = new Vector2(-260f, -28f);
        help.AddThemeFontSizeOverride("font_size", 14);
        help.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 1f));
        Outline(help, 6);
        AddChild(help);
    }

    private static string SlotName(string weaponId) => weaponId switch
    {
        "" => "---",
        "knife" => "CUCHILLO",
        _ => weaponId.ToUpper(),
    };

    private void UpdateSlotsLine()
    {
        if (_slotsLabel == null)
            return;
        if (_player == null)
        {
            _slotsLabel.Text = "";
            return;
        }
        string[] parts = new string[3];
        for (int i = 0; i < 3; i++)
        {
            string name = SlotName(_player.GetSlotWeaponId(i));
            parts[i] = i == _player.CurrentSlot ? $"[{i + 1} {name}]" : $"{i + 1} {name}";
        }
        _slotsLabel.Text = string.Join(" · ", parts);
    }

    /// <summary>
    /// Damage-taken feedback: full-screen red flash, threat arrow parked
    /// between the crosshair and the attacker's screen position, dull
    /// thud. Answers "who killed me from where".
    /// </summary>
    private void BuildDamageFx()
    {
        _dmgFlash = new ColorRect
        {
            Color = new Color(0.8f, 0.05f, 0.05f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _dmgFlash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_dmgFlash);
        _threatArrow = new Label
        {
            Text = "▲",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(32f, 32f),
            PivotOffset = new Vector2(16f, 16f),
        };
        _threatArrow.AddThemeFontSizeOverride("font_size", 30);
        _threatArrow.AddThemeColorOverride("font_color", new Color(1f, 0.2f, 0.15f));
        _threatArrow.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        _threatArrow.AddThemeConstantOverride("outline_size", 6);
        AddChild(_threatArrow);
        _threatArrow.Hide();
        _hurtStream = MakeThud();
        _hurtPlayer = new AudioStreamPlayer();
        AddChild(_hurtPlayer);
    }

    private static AudioStreamWav MakeThud()
    {
        const int mixRate = 22050;
        const float durSec = 0.18f;
        int frames = (int)(mixRate * durSec);
        var rng = new RandomNumberGenerator();
        rng.Seed = 77;
        byte[] data = new byte[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            float time = (float)i / mixRate;
            float tone = Mathf.Sin(Mathf.Tau * 140f * time) * Mathf.Exp(-time * 25f);
            float noise = rng.RandfRange(-1f, 1f) * Mathf.Exp(-time * 40f) * 0.25f;
            float s = Mathf.Clamp(tone * 0.7f + noise, -1f, 1f);
            short v = (short)(s * short.MaxValue);
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = mixRate,
            Stereo = false,
            Data = data,
        };
    }

    private void OnPlayerDamaged(Vector3 fromPos)
    {
        _lastThreat = fromPos;
        _threatLeft = 1.2f;
        _flashLeft = 0.4f;
        if (_hurtPlayer != null)
        {
            _hurtPlayer.Stream = _hurtStream;
            _hurtPlayer.Play();
        }
    }

    private void UpdateDamageFx(float dt)
    {
        if (_dmgFlash != null)
        {
            float alpha = 0f;
            if (_flashLeft > 0f)
            {
                _flashLeft -= dt;
                alpha = 0.45f * Mathf.Clamp(_flashLeft / 0.4f, 0f, 1f);
            }
            _dmgFlash.Color = new Color(0.8f, 0.05f, 0.05f, alpha);
        }
        if (_threatArrow == null)
            return;
        if (_threatLeft <= 0f || _player == null || _player.Camera == null)
        {
            _threatArrow.Hide();
            return;
        }
        _threatLeft -= dt;
        Camera3D cam = _player.Camera;
        Vector2 center = GetViewport().GetVisibleRect().Size * 0.5f;
        Vector2 screen = cam.UnprojectPosition(_lastThreat);
        if (cam.IsPositionBehind(_lastThreat))
            screen = center - (screen - center);
        Vector2 dir = screen - center;
        if (dir.Length() < 40f)
            dir = Vector2.Up * 40f;
        Vector2 edge = center + dir.Normalized() * 150f;
        _threatArrow.Position = edge - new Vector2(16f, 16f);
        _threatArrow.Rotation = dir.Angle() + Mathf.Pi * 0.5f;
        var c = new Color(1f, 0.2f, 0.15f, Mathf.Clamp(_threatLeft / 1.2f, 0f, 1f));
        _threatArrow.AddThemeColorOverride("font_color", c);
        _threatArrow.Show();
    }

    private void BuildPausePanel()
    {
        _pausePanel = new PanelContainer();
        _pausePanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _pausePanel.Position = new Vector2(-220f, -150f);
        _pausePanel.CustomMinimumSize = new Vector2(440f, 0f);
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        var title = new Label { Text = "PAUSA · la fruta espera" };
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 26);
        var hint = new Label { Text = "1/2/3 cambia de arma · Q última · TAB tabla" };
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.AddThemeFontSizeOverride("font_size", 14);
        var resume = new Button { Text = "SEGUIR JUGANDO (ESC)" };
        resume.Pressed += Resume;
        var quit = new Button { Text = "SALIR AL MENÚ" };
        quit.Pressed += QuitToMenu;
        vbox.AddChild(title);
        vbox.AddChild(hint);
        vbox.AddChild(resume);
        vbox.AddChild(quit);
        _pausePanel.AddChild(vbox);
        AddChild(_pausePanel);
        _pausePanel.Hide();
    }

    /// <summary>Show/hide the TAB scoreboard (rows rebuild on next frame when shown).</summary>
    public void ToggleScoreboard()
    {
        if (_board == null || _match == null)
            return;
        if (_board.Visible)
        {
            _board.Hide();
            _boardSig = -1;
        }
        else
        {
            _boardSig = -1;
            _board.Show();
        }
    }

    /// <summary>Cast the local ballot for the i-th vote option (0-based).</summary>
    public void CastVoteFor(int index)
    {
        if (_match == null || !_match.Vote.IsOpen)
            return;
        if (index < 0 || index >= _match.Vote.Options.Count)
            return;
        string voter = _player != null ? _player.Name : "local";
        _match.Vote.CastVote(voter, _match.Vote.Options[index]);
    }

    public override void _Process(double delta)
    {
        if (_match == null)
            return;
        float dt = (float)delta;
        UpdateTopBar();
        UpdateBottom();
        UpdateSlotsLine();
        UpdateDamageFx(dt);
        UpdateScoreEvents();
        UpdateMsg(dt);
        UpdateScoreboard();
        UpdateVote();
        UpdateReload(dt);
    }

    private void UpdateTopBar()
    {
        RoundManager rounds = _match.Rounds;
        int roundNo = rounds.ScoreCT + rounds.ScoreT + 1;
        _score.Text = $"Ronda {roundNo} · CT {rounds.ScoreCT} : {rounds.ScoreT} T";
        string prefix = rounds.Phase == MatchPhase.GoldenRound ? "ORO " : "";
        _roundTimer.Text = prefix + FormatTime(rounds.RoundTimeLeft);
        _matchClock.Text = "Partido " + FormatTime(rounds.TimeLeft);
    }

    private void UpdateBottom()
    {
        if (_player == null)
        {
            _health.Text = "VIDA  --";
            _ammo.Text = "MUNI  --";
            _weapon.Text = "ARMA  --";
        }
        else
        {
            _health.Text = $"VIDA  {_player.Hp}";
            WeaponSystem gun = _player.ArmedWeapon;
            if (gun == null)
            {
                _weapon.Text = "ARMA  --";
                _ammo.Text = "MUNI  --";
            }
            else if (gun.WeaponId == "knife")
            {
                _weapon.Text = "CUCHILLO";
                _ammo.Text = "";
            }
            else
            {
                _weapon.Text = "ARMA  " + gun.WeaponId.ToUpper();
                _ammo.Text = $"MUNI  {gun.MagAmmo} / {gun.ReserveAmmo}";
            }
            bool outOfRound = _player.IsDead
                && (_match.Rounds.Phase == MatchPhase.Live
                    || _match.Rounds.Phase == MatchPhase.GoldenRound);
            _deadMsg.Visible = outOfRound;
            if (outOfRound)
                _deadMsg.Text = "¡Te exprimieron! La ronda sigue sin vos...";
        }
        // Milestone 1 has no armor model on either side: the slot is
        // shown honest (never a mocked number).
        _armor.Text = "CHALECO  --";
    }

    private void UpdateScoreEvents()
    {
        int ct = _match.Rounds.ScoreCT;
        int t = _match.Rounds.ScoreT;
        if (ct == _lastScoreCT && t == _lastScoreT)
            return;
        ShowMsg(ct > _lastScoreCT ? "¡Ronda para CT! ¡La fruta manda!"
            : t > _lastScoreT ? "¡Ronda para T! ¡La fruta manda!"
            : "¡Ronda empatada! Ni la pileta se decide...");
        _lastScoreCT = ct;
        _lastScoreT = t;
    }

    private void UpdateMsg(float dt)
    {
        if (_msgLeft <= 0f)
            return;
        _msgLeft -= dt;
        if (_msgLeft <= 0f)
            _centerMsg.Hide();
    }

    private void ShowMsg(string text)
    {
        _centerMsg.Text = text;
        _centerMsg.Show();
        _msgLeft = MsgSec;
    }

    private void RebuildScoreboard()
    {
        ClearRows(_ctRows);
        ClearRows(_tRows);
        foreach (Node3D fighter in _match.Fighters)
            AddRow(FighterTeam(fighter) == 0 ? _ctRows : _tRows, fighter);
    }

    /// <summary>
    /// Change-gated scoreboard refresh: rebuilding ~70 Controls every
    /// frame while visible churns nodes and risks flicker on weak GPUs,
    /// so rows only rebuild when the underlying data moves. The
    /// signature covers everything rows render — roster size, frags and
    /// deaths per fighter (the dead `(afuera)` tag always coincides with
    /// a death-count change; names never change mid-match). Bindings stay
    /// live: any real change rebuilds on the very next frame.
    /// </summary>
    private void UpdateScoreboard()
    {
        if (!_board.Visible)
        {
            _boardSig = -1;
            return;
        }
        int sig = BoardSignature();
        if (sig == _boardSig)
            return;
        _boardSig = sig;
        RebuildScoreboard();
    }

    private int BoardSignature()
    {
        unchecked
        {
            int sig = _match.Fighters.Count * 73856093;
            foreach (Node3D fighter in _match.Fighters)
                sig = sig * 31 + _match.FragsOf(fighter) * 101 + _match.DeathsOf(fighter);
            return sig;
        }
    }

    private void AddRow(VBoxContainer box, Node3D fighter)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var name = new Label { Text = FighterLabel(fighter), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var frags = new Label { Text = _match.FragsOf(fighter).ToString(), CustomMinimumSize = new Vector2(40f, 0f) };
        var deaths = new Label { Text = _match.DeathsOf(fighter).ToString(), CustomMinimumSize = new Vector2(40f, 0f) };
        var ping = new Label { Text = fighter == (Node3D)_player ? "--" : "BOT", CustomMinimumSize = new Vector2(60f, 0f) };
        row.AddChild(name);
        row.AddChild(frags);
        row.AddChild(deaths);
        row.AddChild(ping);
        box.AddChild(row);
    }

    private static void ClearRows(VBoxContainer box)
    {
        foreach (Node child in box.GetChildren())
        {
            box.RemoveChild(child);
            child.QueueFree();
        }
    }

    private int FighterTeam(Node3D fighter)
    {
        if (fighter is PlayerBody p)
            return p.Team;
        if (fighter is Bot b)
            return b.Team;
        return 1;
    }

    private string FighterLabel(Node3D fighter)
    {
        bool dead = (fighter is PlayerBody p && p.IsDead) || (fighter is Bot b && b.IsDead);
        return dead ? fighter.Name + " (afuera)" : fighter.Name;
    }

    private void UpdateVote()
    {
        VoteManager vote = _match.Vote;
        if (vote.IsOpen && !_voteWasOpen)
            OpenVotePanel();
        _voteWasOpen = vote.IsOpen;
        if (!_votePanel.Visible)
            return;
        if (vote.IsOpen)
        {
            _voteTimer.Text = $"Cierra en {FormatTime(vote.TimeLeft)}";
            foreach (KeyValuePair<string, Label> entry in _voteCounts)
                entry.Value.Text = vote.VotesFor(entry.Key).ToString() + " votos";
        }
    }

    private void OpenVotePanel()
    {
        if (GetTree().Paused && _pausePanel != null)
        {
            _pausePanel.Hide();
            GetTree().Paused = false;
        }        foreach (Node child in _voteOptions.GetChildren())
        {
            _voteOptions.RemoveChild(child);
            child.QueueFree();
        }
        _voteCounts.Clear();
        _voteResult.Text = "";
        int i = 0;
        foreach (string option in _match.Vote.Options)
        {
            int index = i;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            var button = new Button { Text = $"[{index + 1}] Revancha en {option}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            button.Pressed += () => CastVoteFor(index);
            var count = new Label { Text = "0 votos", CustomMinimumSize = new Vector2(80f, 0f) };
            row.AddChild(button);
            row.AddChild(count);
            _voteOptions.AddChild(row);
            _voteCounts[option] = count;
            i++;
        }
        _votePanel.Show();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void VoteKey(Key key)
    {
        int[] digits = new int[] { (int)Key.Key1, (int)Key.Key2, (int)Key.Key3, (int)Key.Key4, (int)Key.Key5, (int)Key.Key6, (int)Key.Key7, (int)Key.Key8, (int)Key.Key9 };
        for (int i = 0; i < digits.Length; i++)
        {
            if ((int)key == digits[i])
            {
                CastVoteFor(i);
                return;
            }
        }
    }

    private void OnPhaseChanged(MatchPhase phase)
    {
        if (phase == MatchPhase.GoldenRound)
            ShowMsg("¡RONDA DE ORO! Muerte súbita en la pileta...");
        else if (phase == MatchPhase.Finished)
        {
            string winner = _match.Rounds.Winner switch
            {
                MatchWinner.CT => "¡Gana CT! ¡La fruta manda!",
                MatchWinner.T => "¡Gana T! ¡La fruta manda!",
                _ => "¡Empate! Ni la pileta se decide...",
            };
            ShowMsg(winner);
        }
    }

    private void OnVoteClosed(string winner)
    {
        if (winner == null)
            _voteResult.Text = "Sin votos: sigue la pileta como está.";
        else
            _voteResult.Text = $"¡Próxima parada: {winner}! Volviendo a la pileta...";
        _reloadLeft = ReloadDelaySec;
    }

    private void UpdateReload(float dt)
    {
        if (_reloadLeft < 0f)
            return;
        _reloadLeft -= dt;
        if (_reloadLeft <= 0f)
        {
            _reloadLeft = -1f;
            GetTree().Paused = false;
            GetTree().ReloadCurrentScene();
        }
    }

    private static string FormatTime(float seconds)
    {
        float clamped = Mathf.Max(0f, seconds);
        int total = Mathf.CeilToInt(clamped);
        return $"{total / 60}:{(total % 60):D2}";
    }
}
