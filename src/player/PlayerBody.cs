using Godot;

using FrutaCS.UI;

namespace FrutaCS.Player;

/// <summary>Shared input action names, registered at runtime (see EnsureInputActions).</summary>
public static class InputActions
{
    public const string MoveForward = "move_forward";
    public const string MoveBack = "move_back";
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string Jump = "jump";
    public const string Walk = "walk";
    public const string Duck = "duck";
    public const string Fire = "fire";
    public const string Secondary = "weapon_secondary";
    public const string Reload = "reload";
    public const string WeaponSlot1 = "weapon_slot_1";
    public const string WeaponSlot2 = "weapon_slot_2";
    public const string WeaponSlot3 = "weapon_slot_3";
    public const string WeaponLast = "weapon_last";
}

/// <summary>
/// First-person CharacterBody3D driven by the pure <see cref="MovementSim"/>.
/// Feet at the origin; capsule 72u tall, camera (Head) at 64u.
/// Drives the sim from _PhysicsProcess at the fixed 60 Hz physics tick
/// (project.godot physics_ticks_per_second=60, so `delta` is 1/60).
/// Walk (Shift) sets the explicit <see cref="MovementInput"/> Walk flag so the
/// sim uses WalkSpeed; duck is stance + eye height only and does not touch speed.
/// After MoveAndSlide the engine result (floor contact, wall-clipped velocity)
/// is written back into the sim via SyncFromEngine — engine state is authoritative.
/// Owns mouse look (yaw on the body, pitch on the camera) and adds the
/// weapon's recoverable view punch on top of the pitch.
/// </summary>
public partial class PlayerBody : CharacterBody3D
{
    private const float EyeHeightU = 64f;
    private const float DuckEyeHeightU = 44f;
    private const float PitchLimitRad = 1.5533f; // 89 deg.

    [Export] public MovementConfig Config;
    [Export] public float MouseSensitivity = 0.0022f;

    /// <summary>Side: 0 = CT, anything else = T (bots read this for billing).</summary>
    [Export] public int Team;

    /// <summary>
    /// Dev/workshop scenes (e.g. the weapon range) own weapon selection:
    /// when true, the 1/2/3/Q slot handling here stays quiet so the
    /// scene's own picker keys don't fight this inventory. Off in the
    /// match, where the 3-slot rules are the real ones.
    /// </summary>
    [Export] public bool SceneOwnsWeaponSwitching;

    /// <summary>Milestone-1 health: no armor model (same simplification as bots).</summary>
    public const int MaxHp = 100;

    private MovementSim _sim = new();
    private MovementParams _params;
    private float _pitch;
    private int _stepClock;
    private Vector3 _stepAnchor = Vector3.Zero;
    private FrutaCS.Weapons.WeaponSystem _weapon;

    /// <summary>
    /// 3-slot inventory: 0 = primary (rifle/AWP pickup), 1 = secondary
    /// (deagle base), 2 = knife. Single WeaponSystem node swaps data;
    /// per-slot ammo is saved/restored so switching never refills.
    /// </summary>
    private readonly FrutaCS.Weapons.WeaponData[] _slots = new FrutaCS.Weapons.WeaponData[3];
    private readonly int[] _savedMag = new int[3];
    private readonly int[] _savedReserve = new int[3];
    private readonly bool[] _hasAmmo = new bool[3];
    private int _currentSlot = -1;
    private int _lastSlot = -1;

    /// <summary>Active inventory slot (0 primary, 1 secondary, 2 knife; -1 none).</summary>
    public int CurrentSlot => _currentSlot;

    public Node3D Head { get; private set; }
    public Camera3D Camera { get; private set; }
    public float HorizontalSpeedU { get; private set; }
    public bool IsDucking { get; private set; }
    public bool SimOnFloor => _sim.IsOnFloor;
    public int Hp { get; private set; } = MaxHp;
    public bool IsDead { get; private set; }

    /// <summary>Weapon glue under Head/Camera3D/WeaponView (null until _Ready).</summary>
    public FrutaCS.Weapons.WeaponSystem ArmedWeapon => _weapon;

    /// <summary>Fired on every billed hit (attacker world pos). HUD drives
    /// the red flash + threat arrow from this.</summary>
    public event System.Action<Vector3> Damaged;

    /// <summary>
    /// Aim basis from yaw + pitch WITHOUT the weapon's view punch.
    /// Ballistics use this so the visual punch never feeds back into the
    /// spray (the cumulative RecoilTable already encodes the full pattern).
    /// </summary>
    public Basis GetAimBasis()
    {
        Basis yawBasis = new(Vector3.Up, Rotation.Y);
        Vector3 fwd = yawBasis * Vector3.Forward.Rotated(Vector3.Right, _pitch);
        Vector3 right = yawBasis * Vector3.Right;
        Vector3 back = -fwd;
        return new Basis(right, back.Cross(right).Normalized(), back);
    }

    public static void EnsureInputActions()
    {
        AddKeyAction(InputActions.MoveForward, Key.W, Key.Up);
        AddKeyAction(InputActions.MoveBack, Key.S, Key.Down);
        AddKeyAction(InputActions.MoveLeft, Key.A, Key.Left);
        AddKeyAction(InputActions.MoveRight, Key.D, Key.Right);
        AddKeyAction(InputActions.Jump, Key.Space);
        AddKeyAction(InputActions.Walk, Key.Shift);
        AddKeyAction(InputActions.Duck, Key.Ctrl, Key.C);
        AddKeyAction(InputActions.Reload, Key.R);
        AddMouseAction(InputActions.Fire, MouseButton.Left);
        AddMouseAction(InputActions.Secondary, MouseButton.Right);
        AddKeyAction(InputActions.WeaponSlot1, Key.Key1);
        AddKeyAction(InputActions.WeaponSlot2, Key.Key2);
        AddKeyAction(InputActions.WeaponSlot3, Key.Key3);
        AddKeyAction(InputActions.WeaponLast, Key.Q);
    }

    private static void AddKeyAction(string action, params Key[] keys)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);
        // Idempotent: never stack defaults over project/user binds on re-ready.
        if (InputMap.ActionGetEvents(action).Count > 0)
            return;
        foreach (Key k in keys)
        {
            var ev = new InputEventKey { PhysicalKeycode = k };
            InputMap.ActionAddEvent(action, ev);
        }
    }

    private static void AddMouseAction(string action, MouseButton button)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);
        // Same idempotent guard as keys: a pre-defined but empty action
        // still gets its default bind; existing binds are never stacked.
        if (InputMap.ActionGetEvents(action).Count > 0)
            return;
        var mb = new InputEventMouseButton { ButtonIndex = button };
        InputMap.ActionAddEvent(action, mb);
    }

    public override void _Ready()
    {
        EnsureInputActions();
        MouseSensitivity = GameSettings.MouseSensitivity;
        AddToGroup("players"); // Lets Bot AI sense the player as an enemy.
        _params = Config != null ? Config.ToParams() : MovementParams.Default;
        Head = GetNode<Node3D>("Head");
        Camera = GetNode<Camera3D>("Head/Camera3D");
        _weapon = GetNodeOrNull<FrutaCS.Weapons.WeaponSystem>("Head/Camera3D/WeaponView");
        // TestRange (no MatchManager): keep the scene-default gun usable.
        if (_weapon != null && _weapon.Weapon != null)
        {
            int slot = SlotFor(_weapon.Weapon.WeaponId);
            _slots[slot] = _weapon.Weapon;
            _weapon.GetAmmo(out _savedMag[slot], out _savedReserve[slot]);
            _hasAmmo[slot] = true;
            _currentSlot = slot;
            _lastSlot = slot;
        }
        Head.Position = new Vector3(0f, EyeHeightU, 0f);
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Slot for a weapon id: 0 primary, 1 secondary, 2 knife.</summary>
    public static int SlotFor(string weaponId) => weaponId switch
    {
        "knife" => 2,
        "deagle" => 1,
        _ => 0,
    };

    /// <summary>
    /// Round spawn: base sidearm + knife, current = sidearm. Clears any
    /// primary pickup from the previous round.
    /// </summary>
    public void ResetBaseLoadout(FrutaCS.Weapons.WeaponData sidearm, FrutaCS.Weapons.WeaponData knife)
    {
        for (int i = 0; i < 3; i++)
        {
            _slots[i] = null;
            _hasAmmo[i] = false;
        }
        _currentSlot = -1;
        _lastSlot = -1;
        if (knife != null)
        {
            _slots[2] = knife;
            _hasAmmo[2] = false; // Equip sets melee (no ammo) state.
        }
        if (sidearm != null)
        {
            _slots[1] = sidearm;
            _hasAmmo[1] = false;
            SwitchTo(1);
        }
        else if (knife != null)
        {
            SwitchTo(2);
        }
    }

    /// <summary>
    /// Floor pickup: store in its slot and auto-switch to it. Re-picking
    /// the same gun refills it (new gun off the floor).
    /// </summary>
    public void GiveWeapon(FrutaCS.Weapons.WeaponData data, bool autoSwitch = true)
    {
        if (data == null || _weapon == null)
            return;
        int slot = SlotFor(data.WeaponId);
        SaveCurrentAmmo();
        _slots[slot] = data;
        _hasAmmo[slot] = false; // Fresh gun: full mag/reserve from Equip.
        if (autoSwitch)
            SwitchTo(slot);
    }

    /// <summary>Switch to a slot; false when empty or already active.</summary>
    public bool SwitchTo(int slot)
    {
        if (slot < 0 || slot > 2 || _weapon == null)
            return false;
        if (_slots[slot] == null || slot == _currentSlot)
            return false;
        SaveCurrentAmmo();
        _lastSlot = _currentSlot;
        _currentSlot = slot;
        _weapon.Equip(_slots[slot]);
        if (_hasAmmo[slot])
            _weapon.SetAmmo(_savedMag[slot], _savedReserve[slot]);
        else
        {
            _weapon.GetAmmo(out _savedMag[slot], out _savedReserve[slot]);
            _hasAmmo[slot] = true;
        }
        return true;
    }

    /// <summary>Q: back to the previous gun.</summary>
    public void SwitchToLast()
    {
        if (_lastSlot >= 0 && _slots[_lastSlot] != null)
            SwitchTo(_lastSlot);
    }

    /// <summary>Weapon id in a slot, or "" when empty (HUD slots line).</summary>
    public string GetSlotWeaponId(int slot)
        => slot >= 0 && slot < 3 && _slots[slot] != null ? _slots[slot].WeaponId : "";

    private void SaveCurrentAmmo()
    {
        if (_weapon == null || _currentSlot < 0 || _slots[_currentSlot] == null)
            return;
        _weapon.GetAmmo(out _savedMag[_currentSlot], out _savedReserve[_currentSlot]);
        _hasAmmo[_currentSlot] = true;
    }

    private void HandleWeaponSwitch()
    {
        // Dev scenes (weapon range) own the picker; the match owns the slots.
        if (SceneOwnsWeaponSwitching)
            return;
        if (Input.IsActionJustPressed(InputActions.WeaponSlot1))
            SwitchTo(0);
        else if (Input.IsActionJustPressed(InputActions.WeaponSlot2))
            SwitchTo(1);
        else if (Input.IsActionJustPressed(InputActions.WeaponSlot3))
            SwitchTo(2);
        else if (Input.IsActionJustPressed(InputActions.WeaponLast))
            SwitchToLast();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float sens = MouseSensitivity * GetScopeSensitivityScale();
            RotateY(-motion.Relative.X * sens);
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * sens, -PitchLimitRad, PitchLimitRad);
            ApplyCameraRotation();
        }
        else if (@event.IsActionPressed("ui_cancel") && !GetTree().Paused
            && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // TestRange (no Hud/pause): ESC just frees the mouse.
            // In-match ESC is consumed by Hud (pause) and never reaches here.
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (@event is InputEventMouseButton mb && mb.Pressed
            && mb.ButtonIndex == MouseButton.Left && !GetTree().Paused
            && Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            // Focus loss (Alt-Tab, vote edge): click recaptures the aim.
            // GUI-consumed clicks (buttons) never reach _UnhandledInput.
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsDead)
            return;
        HandleWeaponSwitch();
        float dt = (float)delta; // Fixed 1/60 s: physics_ticks_per_second=60.
        Vector2 keys = Input.GetVector(
            InputActions.MoveLeft, InputActions.MoveRight,
            InputActions.MoveForward, InputActions.MoveBack);
        Basis yaw = new(Vector3.Up, Rotation.Y);
        Vector3 wishDir = (yaw * new Vector3(keys.X, 0f, keys.Y));
        if (wishDir.Length() > 1f)
            wishDir = wishDir.Normalized();

        IsDucking = Input.IsActionPressed(InputActions.Duck);
        bool walkHeld = Input.IsActionPressed(InputActions.Walk);

        bool jumpPressed = Input.IsActionJustPressed(InputActions.Jump);
        var input = new MovementInput(wishDir, jumpPressed, walkHeld);
        _sim.Tick(in input, in _params, dt);

        Velocity = _sim.Velocity;
        MoveAndSlide();
        _sim.SyncFromEngine(Velocity, IsOnFloor());

        Vector3 h = new(_sim.Velocity.X, 0f, _sim.Velocity.Z);
        HorizontalSpeedU = h.Length();
        // Seam step-up (same box-joint pinches as bots): gate on actual
        // displacement, which pinch lurches can't fake. Ordinary walking
        // covers 100u+ per 30-tick window.
        _stepClock++;
        if (_stepClock >= 30)
        {
            Vector3 moved = GlobalPosition - _stepAnchor;
            moved.Y = 0f;
            float baseSpeed = walkHeld ? _params.WalkSpeed : _params.RunSpeed;
            float intended = baseSpeed * Mathf.Min(1f, wishDir.Length());
            if (IsOnFloor() && wishDir.Length() > 0.5f && moved.Length() < 8f)
                StepUp.TryStep(this, wishDir, intended);
            _stepAnchor = GlobalPosition;
            _stepClock = 0;
        }
        Head.Position = new Vector3(0f, IsDucking ? DuckEyeHeightU : EyeHeightU, 0f);
        ApplyCameraRotation();
    }

    private void ApplyCameraRotation()
    {
        Vector2 punch = GetViewPunchDeg();
        Camera.Rotation = new Vector3(
            _pitch + Mathf.DegToRad(punch.X),
            Mathf.DegToRad(punch.Y),
            0f);
    }

    private Vector2 GetViewPunchDeg() => _weapon != null ? _weapon.ViewPunchDeg : Vector2.Zero;

    private float GetScopeSensitivityScale() => _weapon != null ? _weapon.ScopeSensitivityScale : 1f;

    /// <summary>
    /// Same damage path as bots: integer headless damage in, death at zero.
    /// Bot fire bills the player through this (Task 8 closes the Task 6
    /// unbilled gap); death reports to the match glue, which owns the round.
    /// fromPos is the attacker's position (threat direction); default keeps
    /// backward compatibility with callers that have no position.
    /// </summary>
    public void TakeDamage(int amount, Vector3 fromPos = default)
    {
        if (IsDead || amount <= 0)
            return;
        Hp -= amount;
        if (fromPos != Vector3.Zero)
            Damaged?.Invoke(fromPos);
        if (Hp <= 0)
            Die();
    }

    /// <summary>Round reset: back to full health at the given spawn.</summary>
    public void Respawn(Vector3 pos)
    {
        IsDead = false;
        Hp = MaxHp;
        GlobalPosition = pos;
        _sim.SyncFromEngine(Vector3.Zero, true);
        Velocity = Vector3.Zero;
    }

    private void Die()
    {
        IsDead = true;
        Hp = 0;
        Velocity = Vector3.Zero;
        GetTree().CallGroup("match_manager", "OnFighterDown", Team, this);
    }
}
