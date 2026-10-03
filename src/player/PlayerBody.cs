using Godot;

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
/// Walk (Shift) scales the wish-dir length by WalkSpeed/RunSpeed — the sim
/// maps wish length to wish speed, so no MovementInput change was needed.
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

    private MovementSim _sim = new();
    private MovementParams _params;
    private float _pitch;
    private FrutaCS.Weapons.WeaponSystem _weapon;

    public Node3D Head { get; private set; }
    public Camera3D Camera { get; private set; }
    public float HorizontalSpeedU { get; private set; }
    public bool IsDucking { get; private set; }
    public bool SimOnFloor => _sim.IsOnFloor;

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
        if (!InputMap.HasAction(InputActions.Fire))
        {
            InputMap.AddAction(InputActions.Fire);
            var mb = new InputEventMouseButton { ButtonIndex = MouseButton.Left };
            InputMap.ActionAddEvent(InputActions.Fire, mb);
        }
        if (!InputMap.HasAction(InputActions.Secondary))
        {
            InputMap.AddAction(InputActions.Secondary);
            var mb = new InputEventMouseButton { ButtonIndex = MouseButton.Right };
            InputMap.ActionAddEvent(InputActions.Secondary, mb);
        }
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

    public override void _Ready()
    {
        EnsureInputActions();
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
        float dt = (float)delta; // Fixed 1/60 s: physics_ticks_per_second=60.
        Vector2 keys = Input.GetVector(
            InputActions.MoveLeft, InputActions.MoveRight,
            InputActions.MoveForward, InputActions.MoveBack);
        Basis yaw = new(Vector3.Up, Rotation.Y);
        Vector3 wishDir = (yaw * new Vector3(keys.X, 0f, keys.Y));
        if (wishDir.Length() > 1f)
            wishDir = wishDir.Normalized();

        IsDucking = Input.IsActionPressed(InputActions.Duck);
        bool walkHeld = Input.IsActionPressed(InputActions.Walk) || IsDucking;
        if (walkHeld && _params.RunSpeed > 0f)
            wishDir *= _params.WalkSpeed / _params.RunSpeed;

        bool jumpPressed = Input.IsActionJustPressed(InputActions.Jump);
        var input = new MovementInput(wishDir, jumpPressed);
        _sim.Tick(in input, in _params, dt);

        Velocity = _sim.Velocity;
        MoveAndSlide();

        Vector3 h = new(_sim.Velocity.X, 0f, _sim.Velocity.Z);
        HorizontalSpeedU = h.Length();
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
}
