using System.Collections.Generic;
using Godot;

using FrutaCS.Player;

namespace FrutaCS.Weapons;

/// <summary>
/// Single impact mark for debugging/verification (wall-pattern checks).
/// </summary>
public readonly record struct ShotMark(Vector3 Position, int Damage);

/// <summary>
/// Playable weapon glue over the pure <see cref="WeaponSim"/>.
/// Hitscan raycast from the camera: shot direction = punch-free aim-basis
/// forward rotated by the cumulative table recoil (<see cref="WeaponSim.RecoilOffset"/>) plus
/// a uniform-disc sample inside the cone of the SAME spread value that drives
/// the crosshair (<see cref="CurrentSpreadDeg"/> — a lying crosshair is a defect).
/// Wall penetration loops the ray (up to PenetrationStages walls, thickest wall
/// gates) and resolves with the staged <see cref="WeaponSim.DamageAt"/> overload;
/// knife secondary attack resolves with the SecondaryDamage overload.
/// Non-static colliders (e.g. CharacterBody3D) count as flesh, StaticBody3D as wall.
/// View punch is per-shot table delta added to the camera pitch/yaw by PlayerBody
/// and recovered exponentially here. Sounds and viewmodel are placeholders:
/// one shared material, procedural per-weapon bang.
/// </summary>
public partial class WeaponSystem : Node3D
{
    private const float HitscanRangeU = 4000f;
    private const float ReloadSec = 2.5f; // Placeholder; AK47_RELOAD_TIME 2.45 (see ak47.tres).
    private const float KnifePrimaryIntervalSec = 0.4f; // Swing cadence (see knife.tres).
    private const float KnifeSecondaryIntervalSec = 1.1f; // Stab cadence (see knife.tres).
    private const float UnscopedAwpPenaltyDeg = 4.5749f; // atan(0.08); unscoped AWP spread (see awp.tres).
    private const float MoveThresholdU = 10f; // AWP slow-move branch (see awp.tres).
    private const float ScopedFovDeg = 25f;
    private const float PunchRecoverRate = 10f;
    private const float CrosshairBaseGapPx = 6f;
    private const float CrosshairPxPerDeg = 3f;
    private const float CrosshairLineLenPx = 9f;

    [Export] public WeaponData Weapon;
    [Export] public Camera3D Camera;

    private WeaponSim _sim;
    private WeaponStats _stats;
    private bool _isMelee;
    private PlayerBody _body;
    private AudioStreamPlayer3D _fireSound;
    private AudioStreamWav _primaryStream;
    private AudioStreamWav _secondaryStream;
    private readonly RandomNumberGenerator _rng = new();
    private CanvasLayer _crossLayer;
    private Line2D _lineTop;
    private Line2D _lineBottom;
    private Line2D _lineLeft;
    private Line2D _lineRight;

    private int _magAmmo;
    private int _reserveAmmo;
    private float _cooldownSec;
    private bool _reloading;
    private float _reloadTimerSec;
    private int _shotsInBurst;
    private Vector2 _prevRecoil = Vector2.Zero;
    private bool _prevFireHeld;
    private bool _prevSecondaryHeld;
    private bool _scoped;
    private float _baseFov = 90f;

    public readonly List<ShotMark> ImpactLog = new();
    public int ImpactCount() => ImpactLog.Count;
    public Vector3 GetImpactPos(int i) => ImpactLog[i].Position;
    public int GetImpactDamage(int i) => ImpactLog[i].Damage;
    public Vector2 ViewPunchDeg { get; private set; } = Vector2.Zero;
    public float ScopeSensitivityScale => _scoped ? 0.4f : 1f;
    public bool IsScoped => _scoped;
    public bool IsReloading => _reloading;
    public int MagAmmo => _magAmmo;
    public int ReserveAmmo => _reserveAmmo;
    public string WeaponId => Weapon != null ? Weapon.WeaponId : "";

    public Stance CurrentStance
    {
        get
        {
            if (_body == null)
                return Stance.Stand;
            if (!_body.SimOnFloor)
                return Stance.Air;
            if (_body.IsDucking)
                return Stance.Duck;
            if (_body.HorizontalSpeedU > MoveThresholdU)
                return Stance.Move;
            return Stance.Stand;
        }
    }

    /// <summary>
    /// The exact spread cone (degrees) applied to shots this frame; the
    /// crosshair gap derives from this same value.
    /// </summary>
    public float CurrentSpreadDeg
    {
        get
        {
            if (_sim == null)
                return 0f;
            float spread = _sim.SpreadDeg(CurrentStance);
            if (!_isMelee && WeaponId == "awp" && !_scoped)
                spread += UnscopedAwpPenaltyDeg;
            return spread;
        }
    }

    public override void _Ready()
    {
        PlayerBody.EnsureInputActions();
        _rng.Randomize();
        _body = FindPlayerBody();
        if (Camera == null)
            Camera = FindAncestorCamera();
        if (Camera != null)
            _baseFov = Camera.Fov;
        if (Weapon != null)
        {
            _stats = Weapon.ToStats();
            _sim = new WeaponSim(_stats);
            _isMelee = _stats.MeleeRangeU > 0f;
            _magAmmo = _stats.MagSize;
            _reserveAmmo = _stats.ReserveAmmo;
            BuildPlaceholderSound();
            AdjustPlaceholderViewmodel();
        }
        else
        {
            GD.PrintErr("[WeaponSystem] No Weapon assigned; firing disabled.");
        }
        _fireSound = GetNodeOrNull<AudioStreamPlayer3D>("FireSound");
        BuildCrosshair();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_cooldownSec > 0f)
            _cooldownSec -= dt;
        if (_reloading)
        {
            _reloadTimerSec -= dt;
            if (_reloadTimerSec <= 0f)
                FinishReload();
        }

        bool fireHeld = Input.IsActionPressed(InputActions.Fire);
        bool secondaryHeld = Input.IsActionPressed(InputActions.Secondary);
        if (_sim != null && !_reloading)
        {
            if (_isMelee)
            {
                if (fireHeld && !_prevFireHeld)
                    TryFireMelee(false);
                else if (secondaryHeld && !_prevSecondaryHeld)
                    TryFireMelee(true);
            }
            else
            {
                if (fireHeld)
                    TryFireGun();
                else if (_shotsInBurst > 0)
                    EndBurst();
                if (secondaryHeld && !_prevSecondaryHeld)
                    SecondaryGun();
                if (Input.IsActionJustPressed(InputActions.Reload))
                    StartReload();
            }
        }
        _prevFireHeld = fireHeld;
        _prevSecondaryHeld = secondaryHeld;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        ViewPunchDeg *= Mathf.Exp(-PunchRecoverRate * dt);
        if (Camera != null)
        {
            float target = _scoped ? ScopedFovDeg : _baseFov;
            Camera.Fov = Mathf.Lerp(Camera.Fov, target, 1f - Mathf.Exp(-12f * dt));
        }
        UpdateCrosshair();
    }

    // -- Firing -----------------------------------------------------------

    private void TryFireGun()
    {
        if (_cooldownSec > 0f || _magAmmo <= 0 || _stats.Rpm <= 0)
            return;
        _cooldownSec = 60f / _stats.Rpm;
        _magAmmo--;
        FireHitscan(HitscanRangeU, false);
    }

    private void TryFireMelee(bool secondary)
    {
        if (_cooldownSec > 0f)
            return;
        _cooldownSec = secondary ? KnifeSecondaryIntervalSec : KnifePrimaryIntervalSec;
        FireHitscan(_stats.MeleeRangeU, secondary);
    }

    private void SecondaryGun()
    {
        if (WeaponId == "awp")
            _scoped = !_scoped;
    }

    private void StartReload()
    {
        if (_magAmmo >= _stats.MagSize || _reserveAmmo <= 0)
            return;
        _scoped = false;
        EndBurst(); // Spray does not carry across magazines.
        _reloading = true;
        _reloadTimerSec = ReloadSec;
    }

    private void FinishReload()
    {
        _reloading = false;
        int need = _stats.MagSize - _magAmmo;
        int take = Mathf.Min(need, _reserveAmmo);
        _magAmmo += take;
        _reserveAmmo -= take;
    }

    private void EndBurst()
    {
        _shotsInBurst = 0;
        _prevRecoil = Vector2.Zero;
        _sim.Reset();
    }

    private void FireHitscan(float rangeU, bool secondary)
    {
        if (Camera == null)
            return;
        _shotsInBurst++;
        Vector2 recoil = _sim.RecoilOffset(_shotsInBurst);
        ViewPunchDeg += recoil - _prevRecoil;
        _prevRecoil = recoil;

        float spreadDeg = CurrentSpreadDeg;
        float r = spreadDeg * Mathf.Sqrt(_rng.Randf());
        float t = _rng.Randf() * Mathf.Tau;
        float pitchDeg = recoil.X + r * Mathf.Sin(t);
        float yawDeg = recoil.Y + r * Mathf.Cos(t);
        // Ballistics use the punch-free aim basis (see GetAimBasis): the
        // cumulative table already encodes the full spray, so the visual
        // punch applied to the camera must not feed back into the ray.
        Basis aim = _body != null ? _body.GetAimBasis() : Camera.GlobalBasis;
        Vector3 dir = -aim.Z;
        dir = dir.Rotated(aim.X.Normalized(), Mathf.DegToRad(pitchDeg));
        dir = dir.Rotated(aim.Y.Normalized(), -Mathf.DegToRad(yawDeg));
        Vector3 origin = Camera.GlobalTransform.Origin;

        Vector3 pos = origin;
        int walls = 0;
        float thickest = 0f;
        // The visible impact is always the first surface struck; the loop
        // below only resolves how many walls the bullet crosses for the
        // staged damage computation.
        Vector3 firstEnd = origin + dir * rangeU;
        float firstDist = rangeU;
        bool firstRecorded = false;
        int maxSegs = _stats.PenetrationStages + 1;
        for (int i = 0; i < maxSegs; i++)
        {
            Godot.Collections.Dictionary hit = QueryRay(pos, origin + dir * rangeU);
            if (hit.Count == 0)
                break;
            Vector3 hitPos = (Vector3)hit["position"];
            GodotObject collider = (GodotObject)hit["collider"];
            if (!firstRecorded)
            {
                firstRecorded = true;
                firstEnd = hitPos;
                firstDist = origin.DistanceTo(hitPos);
            }
            if (collider is not StaticBody3D)
                break;
            float thickness = MeasureWall(hitPos, dir);
            thickest = Mathf.Max(thickest, thickness);
            walls++;
            pos = hitPos + dir * 1f;
            if (walls > _stats.PenetrationStages || thickness > _stats.MaxWallThicknessU)
                break;
        }

        int damage = _sim.DamageAt(firstDist, walls, thickest, secondary);
        ImpactLog.Add(new ShotMark(firstEnd, damage));
        if (ImpactLog.Count > 64)
            ImpactLog.RemoveAt(0);
        PlayBang(secondary);
    }

    private Godot.Collections.Dictionary QueryRay(Vector3 from, Vector3 to)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        if (_body != null)
        {
            var exclude = new Godot.Collections.Array<Rid> { _body.GetRid() };
            query.Exclude = exclude;
        }
        return GetWorld3D().DirectSpaceState.IntersectRay(query);
    }

    private float MeasureWall(Vector3 entry, Vector3 dir)
    {
        float probe = _stats.MaxWallThicknessU + 1f;
        Godot.Collections.Dictionary exit = QueryRay(entry + dir * probe, entry);
        if (exit.Count == 0)
            return _stats.MaxWallThicknessU; // No exit found: treat as stopping wall.
        Vector3 exitPos = (Vector3)exit["position"];
        return probe - (entry + dir * probe).DistanceTo(exitPos);
    }

    // -- Placeholder presentation -----------------------------------------

    private void BuildPlaceholderSound()
    {
        _primaryStream = WeaponId switch
        {
            "awp" => MakeBang(80f, 0.35f, 11),
            "deagle" => MakeBang(120f, 0.2f, 12),
            "m4a1" => MakeBang(170f, 0.12f, 13),
            "knife" => MakeBang(900f, 0.12f, 14),
            _ => MakeBang(150f, 0.14f, 10), // ak47 default.
        };
        _secondaryStream = WeaponId == "knife" ? MakeBang(200f, 0.15f, 15) : null;
    }

    private static AudioStreamWav MakeBang(float baseFreqHz, float durSec, int seed)
    {
        const int mixRate = 22050;
        int frames = Mathf.Max(1, (int)(mixRate * durSec));
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)seed;
        byte[] data = new byte[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            float time = (float)i / mixRate;
            float noise = rng.RandfRange(-1f, 1f) * Mathf.Exp(-time * 30f);
            float thump = Mathf.Sin(Mathf.Tau * baseFreqHz * time) * Mathf.Exp(-time * 18f);
            float s = Mathf.Clamp(noise * 0.7f + thump * 0.6f, -1f, 1f);
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

    private void PlayBang(bool secondary)
    {
        if (_fireSound == null)
            return;
        _fireSound.Stream = secondary && _secondaryStream != null ? _secondaryStream : _primaryStream;
        _fireSound.Play();
    }

    private void AdjustPlaceholderViewmodel()
    {
        var vm = GetNodeOrNull<Node3D>("Viewmodel");
        if (vm == null)
            return;
        vm.Scale = WeaponId switch
        {
            "awp" => new Vector3(1f, 1f, 1.35f),
            "deagle" => new Vector3(0.8f, 0.8f, 0.6f),
            "knife" => new Vector3(0.7f, 0.5f, 0.5f),
            _ => Vector3.One, // Rifles keep the base silhouette.
        };
    }

    private void BuildCrosshair()
    {
        _crossLayer = new CanvasLayer();
        AddChild(_crossLayer);
        _lineTop = MakeLine();
        _lineBottom = MakeLine();
        _lineLeft = MakeLine();
        _lineRight = MakeLine();
    }

    private Line2D MakeLine()
    {
        var line = new Line2D
        {
            Width = 2f,
            DefaultColor = new Color(1f, 1f, 1f),
            Antialiased = true,
        };
        line.AddPoint(Vector2.Zero);
        line.AddPoint(Vector2.Zero);
        _crossLayer.AddChild(line);
        return line;
    }

    private void UpdateCrosshair()
    {
        bool show = _sim != null && !_scoped && _crossLayer != null;
        _crossLayer.Visible = show;
        if (!show)
            return;
        Vector2 center = GetViewport().GetVisibleRect().Size * 0.5f;
        float gap = CrosshairBaseGapPx + CurrentSpreadDeg * CrosshairPxPerDeg;
        float len = CrosshairLineLenPx;
        _lineTop.Points = new[] { center + new Vector2(0f, -gap - len), center + new Vector2(0f, -gap) };
        _lineBottom.Points = new[] { center + new Vector2(0f, gap), center + new Vector2(0f, gap + len) };
        _lineLeft.Points = new[] { center + new Vector2(-gap - len, 0f), center + new Vector2(-gap, 0f) };
        _lineRight.Points = new[] { center + new Vector2(gap, 0f), center + new Vector2(gap + len, 0f) };
    }

    private PlayerBody FindPlayerBody()
    {
        Node n = GetParent();
        while (n != null)
        {
            if (n is PlayerBody body)
                return body;
            n = n.GetParent();
        }
        return null;
    }

    private Camera3D FindAncestorCamera()
    {
        Node n = GetParent();
        while (n != null)
        {
            if (n is Camera3D cam)
                return cam;
            n = n.GetParent();
        }
        return null;
    }
}
