using System.Globalization;
using Godot;

using FrutaCS.Bots;
using FrutaCS.Player;
using FrutaCS.Weapons;

namespace FrutaCS.Ranges;

/// <summary>
/// CS2-workshop-style weapon and aim range: instant picker over every
/// milestone-1 gun (keys 1..5 or the overlay buttons), infinite reserve
/// with the real 2.5 s reload, and frozen <see cref="Bot"/> capsules as
/// aim dummies (stationary, or strafing on toggle) that respawn shortly
/// after dying. Headshots, damage numbers, hitmarker, tracers, spread
/// and recoil all come from the normal <see cref="WeaponSystem"/> path:
/// this scene holds no gameplay rules, no rounds and no MatchManager.
/// </summary>
public partial class WeaponRange : Node3D
{
    /// <summary>Picker order; keys 1..5 map to these ids (index + 1).</summary>
    private static readonly string[] WeaponIds = { "ak47", "m4a1", "awp", "deagle", "knife" };

    private static readonly string[] WeaponLabels = { "AK-47", "M4A1", "AWP", "DEAGLE", "CUCHILLO" };

    private const int InfiniteReserve = 9999;
    private const float DummyRespawnSec = 1.5f;
    private const float StrafeSpeedU = 140f;
    private const float StrafeAmplitudeU = 200f;

    /// <summary>Dummy anchors (feet on the deck), spread in front of spawn.</summary>
    private static readonly Vector3[] DummyHomes =
    {
        new(-300f, 0f, -350f), new(300f, 0f, -350f),
        new(-450f, 0f, -700f), new(450f, 0f, -700f),
        new(0f, 0f, -1050f),
        new(-300f, 0f, -1400f), new(300f, 0f, -1400f),
    };

    private sealed class Dummy
    {
        public Bot Node = null!;
        public Vector3 Home;
        public int StrafeDir = 1;
        public float RespawnLeft;
    }

    private readonly System.Collections.Generic.List<Dummy> _dummies = new();
    private PlayerBody _player = null!;
    private WeaponSystem _weapon = null!;
    private bool _strafe;
    private int _active = -1;

    private CanvasLayer _ui = null!;
    private Label _statsLabel = null!;
    private Label _ammoLabel = null!;
    private Label _modeLabel = null!;
    private readonly System.Collections.Generic.List<Button> _weaponButtons = new();

    public override void _Ready()
    {
        WeaponData.PreloadAll();
        _player = GetNode<PlayerBody>("PlayerBody");
        // The picker owns weapon choice here: silence the 1/2/3/Q slot
        // handling so both paths don't fight over the same keys.
        _player.SceneOwnsWeaponSwitching = true;
        _weapon = _player.ArmedWeapon;
        SpawnDummies();
        BuildUi();
        SelectWeapon(0);
    }

    // -- Input ------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;
        if (key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode <= Key.Key5)
            SelectWeapon((int)key.PhysicalKeycode - (int)Key.Key1);
        else if (key.PhysicalKeycode == Key.G)
            ToggleStrafe();
        else if (key.PhysicalKeycode == Key.F)
            ResetDummies();
        // ESC/click mouse recapture stay in PlayerBody (works in the range).
    }

    // -- Frame ------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_weapon == null)
            return;
        _ammoLabel.Text = _weapon.WeaponId == "knife"
            ? "MUNICIÓN ∞"
            : "MUNICIÓN " + _weapon.MagAmmo + " / ∞";
    }

    public override void _PhysicsProcess(double delta)
    {
        TickDummies((float)delta);
        TopUpReserve();
    }

    /// <summary>
    /// Workshop ammo: the reserve never runs out (reload still takes the
    /// real 2.5 s, so reload timing stays practiceable).
    /// </summary>
    private void TopUpReserve()
    {
        if (_weapon == null || _weapon.WeaponId == "knife")
            return; // Melee: no magazine, nothing to top up.
        if (_weapon.ReserveAmmo >= InfiniteReserve)
            return; // Already topped (avoids touching ammo every tick).
        _weapon.SetAmmo(_weapon.MagAmmo, InfiniteReserve);
    }

    // -- Weapon picker -----------------------------------------------------

    private void SelectWeapon(int index)
    {
        if (index < 0 || index >= WeaponIds.Length || _weapon == null)
            return;
        WeaponData data = WeaponData.Get(WeaponIds[index]);
        if (data == null)
            return;
        _active = index;
        _weapon.Equip(data);
        _weapon.SetAmmo(_weapon.MagAmmo, InfiniteReserve);
        RefreshUi();
    }

    // -- Dummies ----------------------------------------------------------

    private void SpawnDummies()
    {
        PackedScene botScene = GD.Load<PackedScene>("res://src/bots/Bot.tscn");
        for (int i = 0; i < DummyHomes.Length; i++)
        {
            Bot bot = botScene.Instantiate<Bot>();
            bot.Name = "Blanco " + (i + 1);
            bot.Team = 1; // Enemy side for the CT player: shots bill damage.
            AddChild(bot);
            bot.GlobalPosition = DummyHomes[i];
            // Frozen target: the range owns strafing, the brain never runs.
            bot.SetPhysicsProcess(false);
            _dummies.Add(new Dummy
            {
                Node = bot,
                Home = DummyHomes[i],
                RespawnLeft = DummyRespawnSec,
            });
        }
    }

    private void TickDummies(float dt)
    {
        foreach (Dummy d in _dummies)
        {
            Bot bot = d.Node;
            if (bot.IsDead)
            {
                d.RespawnLeft -= dt;
                if (d.RespawnLeft <= 0f)
                {
                    bot.Respawn(d.Home, _player.GlobalPosition);
                    bot.SetPhysicsProcess(false);
                    d.StrafeDir = 1;
                }
                continue;
            }
            if (!_strafe)
                continue;
            Vector3 pos = bot.GlobalPosition;
            pos.X += d.StrafeDir * StrafeSpeedU * dt;
            if (Mathf.Abs(pos.X - d.Home.X) >= StrafeAmplitudeU)
            {
                pos.X = d.Home.X + d.StrafeDir * StrafeAmplitudeU;
                d.StrafeDir *= -1;
            }
            bot.GlobalPosition = pos;
        }
    }

    private void ResetDummies()
    {
        foreach (Dummy d in _dummies)
        {
            d.Node.Respawn(d.Home, _player.GlobalPosition);
            d.Node.SetPhysicsProcess(false);
            d.StrafeDir = 1;
            d.RespawnLeft = DummyRespawnSec;
        }
    }

    private void ToggleStrafe()
    {
        _strafe = !_strafe;
        RefreshUi();
    }

    // -- Overlay HUD (built in code: no .tscn UI needed) -------------------

    private void BuildUi()
    {
        _ui = new CanvasLayer { Layer = 2 };
        AddChild(_ui);

        var panel = new PanelContainer { Position = new Vector2(16f, 16f) };
        _ui.AddChild(panel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        panel.AddChild(col);

        var title = new Label
        {
            Text = "RANGO DE ARMAS · FrutaCS",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        col.AddChild(title);

        for (int i = 0; i < WeaponIds.Length; i++)
        {
            int index = i; // Captured per-button index for the lambda.
            var button = new Button
            {
                Text = (i + 1) + "  " + WeaponLabels[i],
                CustomMinimumSize = new Vector2(200f, 30f),
            };
            button.Pressed += () => SelectWeapon(index);
            col.AddChild(button);
            _weaponButtons.Add(button);
        }

        _statsLabel = new Label();
        col.AddChild(_statsLabel);
        _ammoLabel = new Label();
        col.AddChild(_ammoLabel);
        _modeLabel = new Label();
        col.AddChild(_modeLabel);
        col.AddChild(new Label
        {
            Text = "1-5 arma · R recargar · G strafe · F reponer",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        col.AddChild(new Label
        {
            Text = "ESC libera el mouse · clic recaptura",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
    }

    private void RefreshUi()
    {
        for (int i = 0; i < _weaponButtons.Count; i++)
        {
            bool active = i == _active;
            _weaponButtons[i].Text = (active ? "> " : "") + (i + 1) + "  " + WeaponLabels[i];
            _weaponButtons[i].AddThemeColorOverride("font_color",
                active ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 1f, 1f));
        }
        WeaponData data = _active >= 0 ? WeaponData.Get(WeaponIds[_active]) : null;
        _statsLabel.Text = data == null
            ? ""
            : WeaponLabels[_active] + " · DAÑO " + data.Damage.ToString("0", CultureInfo.InvariantCulture)
              + " · " + data.Rpm + " RPM · CARGADOR " + data.MagSize
              + " · DISPERSIÓN " + data.SpreadStandDeg.ToString("0.00", CultureInfo.InvariantCulture) + "°";
        _modeLabel.Text = _strafe ? "[G] DUMMIES: STRAFE" : "[G] DUMMIES: QUIETOS";
    }
}
