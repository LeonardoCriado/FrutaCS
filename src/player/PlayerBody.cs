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

    /// <summary>Milestone-1 health: no armor model (same simplification as bots).</summary>
    public const int MaxHp = 100;

    private MovementSim _sim = new();
    private MovementParams _params;
    private float _pitch;
    private int _stepClock;
    private Vector3 _stepAnchor = Vector3.Zero;
    private FrutaCS.Weapons.WeaponSystem _weapon;

    public Node3D Head { get; private set; }
    public Camera3D Camera { get; private set; }
    public float HorizontalSpeedU { get; private set; }
    public bool IsDucking { get; private set; }
    public bool SimOnFloor => _sim.IsOnFloor;
    public int Hp { get; private set; } = MaxHp;
    public bool IsDead { get; private set; }

    /// <summary>Weapon glue under Head/Camera3D/WeaponView (null until _Ready).</summary>
    public FrutaCS.Weapons.WeaponSystem ArmedWeapon => _weapon;

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
        Head.Position = new Vector3(0f, EyeHeightU, 0f);
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
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
        else if (@event.IsActionPressed("ui_cancel") && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsDead)
            return;
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
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (IsDead || amount <= 0)
            return;
        Hp -= amount;
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
