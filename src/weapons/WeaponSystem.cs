using System.Collections.Generic;
using Godot;

using FrutaCS.Bots;
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
/// and recovered exponentially here. Sounds are procedural; viewmodels are
/// asset-free low-poly builds (see <see cref="WeaponViewmodel"/>).
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
    private CanvasLayer _hitLayer;
    private readonly Line2D[] _hitLines = new Line2D[4];
    private float _hitmarkLeft;
    private bool _hitmarkKill;
    private AudioStreamPlayer _hitPlayer;
    private AudioStreamWav _hitStream;
    private AudioStreamWav _killStream;
    private SphereMesh _sparkMesh;
    private Marker3D _muzzle;
    private BoxMesh _beamMesh;
    private Node3D _viewmodel;
    private MeshInstance3D _muzzleFlash;
    private OmniLight3D _muzzleLight;
    private float _flashLeft;
    private float _flashMax = 0.055f;
    private float _kick;
    private float _equipT = 1f;
    private float _bobPhase;
    private Vector2 _sway;
    private float _weaponTime;
    private float _meleeLeft;
    private float _meleeDuration = 0.2f;
    private bool _meleeStab;
    private WeaponPose _vmPose;

    /// <summary>Hit at/above this height over the victim's feet counts as head.</summary>
    private const float HeadMinU = 54f;

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

    /// <summary>
    /// Read current ammo (for per-slot preservation across switches).
    /// </summary>
    public void GetAmmo(out int mag, out int reserve)
    {
        mag = _magAmmo;
        reserve = _reserveAmmo;
    }

    /// <summary>
    /// Restore ammo after <see cref="Equip"/> (switching keeps each slot's
    /// remaining rounds; pickups/resets start full via Equip alone).
    /// </summary>
    public void SetAmmo(int mag, int reserve)
    {
        if (_stats.MagSize <= 0)
            mag = 0;
        else
            mag = Mathf.Clamp(mag, 0, _stats.MagSize);
        reserve = _stats.ReserveAmmo < 0 ? -1 : Mathf.Max(0, reserve);
        _magAmmo = mag;
        _reserveAmmo = reserve;
    }

    /// <summary>
    /// Swap to another weapon at runtime (pickups, round re-arm). Rebuilds
    /// stats/simulation/ammo from the data, clears the spray state (a fresh
    /// gun has no recoil debt), unscopes and unpouches the reload.
    /// </summary>
    public void Equip(WeaponData data)
    {
        if (data == null)
            return;
        Weapon = data;
        _stats = data.ToStats();
        _sim = new WeaponSim(_stats);
        _isMelee = _stats.MeleeRangeU > 0f;
        _magAmmo = _stats.MagSize;
        _reserveAmmo = _stats.ReserveAmmo;
        _shotsInBurst = 0;
        _prevRecoil = Vector2.Zero;
        ViewPunchDeg = Vector2.Zero;
        _scoped = false;
        _reloading = false;
        BuildPlaceholderSound();
        BuildViewmodel();
        _kick = 0f;
        _meleeLeft = 0f;
        _flashLeft = 0f;
    }

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
        _fireSound = GetNodeOrNull<AudioStreamPlayer3D>("FireSound");
        _viewmodel = GetNodeOrNull<Node3D>("Viewmodel");
        _muzzle = GetNodeOrNull<Marker3D>("Viewmodel/Muzzle");
        BuildMuzzleFlash();
        BuildCrosshair();
        BuildHitmarker();
        BuildHitSound();
        _sparkMesh = new SphereMesh { Radius = 3f, Height = 6f };
        _beamMesh = new BoxMesh { Size = new Vector3(0.03f, 0.03f, 1f) };
        if (Weapon != null)
        {
            _stats = Weapon.ToStats();
            _sim = new WeaponSim(_stats);
            _isMelee = _stats.MeleeRangeU > 0f;
            _magAmmo = _stats.MagSize;
            _reserveAmmo = _stats.ReserveAmmo;
            BuildPlaceholderSound();
            BuildViewmodel();
        }
        else
        {
            GD.PrintErr("[WeaponSystem] No Weapon assigned; firing disabled.");
        }
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
        UpdateHitmarker(dt);
        UpdateFx(dt);
        UpdateViewmodel(dt);
        UpdateMuzzleFlash(dt);
    }

    // -- Firing -----------------------------------------------------------

    private void TryFireGun()
    {
        if (_cooldownSec > 0f || _stats.Rpm <= 0)
            return;
        if (_magAmmo <= 0)
        {
            // CS-like: pulling an empty trigger starts the reload.
            // (StartReload no-ops at 0 reserve.)
            StartReload();
            _cooldownSec = 0.25f; // Don't spin on the empty trigger.
            return;
        }
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
        GodotObject firstCollider = null;
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
                firstCollider = collider;
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

        // Head brings the 1.6 multiplier (guns only): where you hit matters.
        bool head = !secondary && !_isMelee && firstCollider is Node3D victim
            && firstEnd.Y - victim.GlobalPosition.Y >= HeadMinU;
        int damage = head
            ? _sim.DamageAt(firstDist, walls, thickest, secondary, true)
            : _sim.DamageAt(firstDist, walls, thickest, secondary);
        ImpactLog.Add(new ShotMark(firstEnd, damage));
        if (ImpactLog.Count > 64)
            ImpactLog.RemoveAt(0);
        if (_isMelee)
        {
            StartMeleeSwing(secondary);
        }
        else
        {
            _kick = Mathf.Min(1.5f, _kick + (WeaponId switch
            {
                "awp" => 1.1f,
                "deagle" => 0.8f,
                "m4a1" => 0.4f,
                _ => 0.5f,
            }));
            SpawnMuzzleFlash();
            SpawnTracer(firstEnd);
        }
        int result = BillFlesh(firstCollider, damage);
        if (result > 0)
        {
            ShowHitmarker(result == 2);
            PlayHitSound(result == 2);
            SpawnFleshFx(firstEnd, damage, result == 2, head);
        }
        else if (firstRecorded)
        {
            SpawnWallSpark(firstEnd);
        }
        PlayBang(secondary);
    }

    /// <summary>
    /// First surface is a fighter: bill damage on enemies only (no
    /// friendly fire in milestone 1; teammates stop the bullet). Melee
    /// stabs arrive here too (same FireHitscan path, wall stages are 0).
    /// Returns 0 = no flesh billed, 1 = hit, 2 = kill (drives hitmarker).
    /// </summary>
    private int BillFlesh(GodotObject collider, int damage)
    {
        if (collider == null || damage <= 0)
            return 0;
        int myTeam = _body != null ? _body.Team : 0;
        if (collider is PlayerBody player)
        {
            if (player != _body && !player.IsDead && player.Team != myTeam)
            {
                player.TakeDamage(damage);
                if (player.IsDead && _body != null)
                    GetTree().CallGroup("match_manager", "OnFighterFrag", _body, player);
                return player.IsDead ? 2 : 1;
            }
        }
        else if (collider is Bot bot)
        {
            if (!bot.IsDead && bot.Team != myTeam)
            {
                bot.TakeDamage(damage);
                if (bot.IsDead && _body != null)
                    GetTree().CallGroup("match_manager", "OnFighterFrag", _body, bot);
                return bot.IsDead ? 2 : 1;
            }
        }
        return 0;
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

    private void BuildViewmodel()
    {
        if (_viewmodel == null)
            return;
        // Team colors the sleeve of the holding hands (CT blue / T orange).
        int team = _body != null ? _body.Team : 0;
        _vmPose = WeaponViewmodel.Build(_viewmodel, _muzzle, WeaponId, team);
        _viewmodel.Position = _vmPose.Position;
        _viewmodel.RotationDegrees = _vmPose.RotationDegrees;
        _viewmodel.Scale = Vector3.One * _vmPose.Scale;
        _equipT = 0f;
        _viewmodel.Visible = !_scoped;
    }

    private void StartMeleeSwing(bool stab)
    {
        _meleeStab = stab;
        _meleeDuration = stab ? 0.22f : 0.18f;
        _meleeLeft = _meleeDuration;
    }

    private void UpdateViewmodel(float dt)
    {
        if (_viewmodel == null)
            return;
        _viewmodel.Visible = !_scoped && _sim != null;
        if (!_viewmodel.Visible)
            return;

        _weaponTime += dt;
        float speed = _body != null ? _body.HorizontalSpeedU : 0f;
        float speedT = Mathf.Clamp(speed / 250f, 0f, 1f);
        bool grounded = _body == null || _body.SimOnFloor;
        if (grounded && speed > 5f)
            _bobPhase += dt * (6f + 4f * speedT);
        float bobScale = grounded ? 1f : 0.25f;
        float bobX = Mathf.Sin(_bobPhase) * 0.010f * speedT * bobScale;
        float bobY = Mathf.Abs(Mathf.Cos(_bobPhase)) * 0.008f * speedT * bobScale;
        float breath = Mathf.Sin(_weaponTime * 1.5f) * 0.0015f;

        Vector2 mouse = Input.MouseMode == Input.MouseModeEnum.Captured
            ? Input.GetLastMouseVelocity()
            : Vector2.Zero;
        Vector2 swayTarget = new(
            Mathf.Clamp(-mouse.X * 0.00002f, -0.012f, 0.012f),
            Mathf.Clamp(mouse.Y * 0.00002f, -0.012f, 0.012f));
        _sway = _sway.Lerp(swayTarget, 1f - Mathf.Exp(-9f * dt));

        float reloadDip = 0f;
        if (_reloading)
        {
            float t = 1f - Mathf.Clamp(_reloadTimerSec / ReloadSec, 0f, 1f);
            reloadDip = Mathf.Sin(Mathf.Pi * t);
        }

        float swing = 0f;
        if (_meleeLeft > 0f)
        {
            _meleeLeft = Mathf.Max(0f, _meleeLeft - dt);
            float t = 1f - _meleeLeft / _meleeDuration;
            swing = Mathf.Sin(Mathf.Pi * Mathf.Clamp(t, 0f, 1f));
        }

        _equipT = Mathf.Min(1f, _equipT + dt / 0.28f);
        float equip = Mathf.Pow(1f - _equipT, 3f);
        _kick = Mathf.Lerp(_kick, 0f, 1f - Mathf.Exp(-16f * dt));

        Vector3 pos = _vmPose.Position;
        pos.X += bobX + _sway.X - (swing > 0f && !_meleeStab ? swing * 0.10f : 0f);
        pos.Y += -bobY + breath + _sway.Y - reloadDip * 0.10f - equip * 0.18f;
        pos.Z += _kick * 0.035f - (swing > 0f && _meleeStab ? swing * 0.10f : 0f);

        Vector3 rot = _vmPose.RotationDegrees;
        rot.X += _kick * 2.2f - reloadDip * 22f - equip * 18f
            - (swing > 0f && _meleeStab ? swing * 10f : 0f);
        rot.Y += _sway.X * 250f - (swing > 0f && !_meleeStab ? swing * 18f : 0f);
        rot.Z += _sway.Y * 180f + reloadDip * 10f - equip * 6f
            + (swing > 0f && !_meleeStab ? swing * 30f : 0f);

        _viewmodel.Position = pos;
        _viewmodel.RotationDegrees = rot;
    }

    private void BuildMuzzleFlash()
    {
        if (_muzzle == null)
            return;

        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = true,
            AlbedoTexture = MakeFlashTexture(),
            AlbedoColor = new Color(1f, 0.82f, 0.45f),
        };
        var quad = new QuadMesh { Size = new Vector2(0.20f, 0.20f) };
        _muzzleFlash = new MeshInstance3D
        {
            Name = "MuzzleFlash",
            Mesh = quad,
            MaterialOverride = mat,
            Position = new Vector3(0f, 0f, -0.01f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = WeaponViewmodel.ViewmodelLayer,
            Visible = false,
        };
        _muzzle.AddChild(_muzzleFlash);

        _muzzleLight = new OmniLight3D
        {
            Name = "MuzzleFlashLight",
            LightColor = new Color(1f, 0.75f, 0.35f),
            LightEnergy = 0f,
            OmniRange = 2.4f,
            ShadowEnabled = false,
            Visible = false,
        };
        _muzzle.AddChild(_muzzleLight);
    }

    private void SpawnMuzzleFlash()
    {
        if (_muzzleFlash == null)
            return;
        _flashLeft = _flashMax;
        _muzzleFlash.Visible = true;
        _muzzleFlash.Rotation = new Vector3(0f, 0f, _rng.RandfRange(0f, Mathf.Tau));
        float scale = _rng.RandfRange(0.85f, 1.2f);
        _muzzleFlash.Scale = new Vector3(scale, scale, scale);
        if (_muzzleLight != null)
        {
            _muzzleLight.Visible = true;
            _muzzleLight.LightEnergy = _rng.RandfRange(2.4f, 3.2f);
        }
    }

    private void UpdateMuzzleFlash(float dt)
    {
        if (_flashLeft <= 0f)
            return;
        _flashLeft -= dt;
        float t = Mathf.Clamp(_flashLeft / _flashMax, 0f, 1f);
        if (_muzzleFlash != null)
            _muzzleFlash.Visible = _flashLeft > 0f;
        if (_muzzleLight != null)
        {
            _muzzleLight.Visible = _flashLeft > 0f;
            _muzzleLight.LightEnergy = 3f * t;
        }
    }

    /// <summary>Procedural 4-point star: no texture assets needed for the flash.</summary>
    private static ImageTexture MakeFlashTexture()
    {
        const int size = 64;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = (x + 0.5f) / size * 2f - 1f;
                float py = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(px * px + py * py);
                float angle = Mathf.Atan2(py, px);
                float spikes = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2f)), 10f)
                    * Mathf.Max(0f, 1f - r * 1.4f);
                float core = Mathf.Max(0f, 1f - r * 1.7f);
                core *= core;
                float alpha = Mathf.Clamp(core + spikes * 0.7f, 0f, 1f);
                if (alpha <= 0.01f)
                {
                    image.SetPixel(x, y, new Color(1f, 1f, 1f, 0f));
                    continue;
                }
                float heat = Mathf.Clamp(1f - r * 0.9f, 0f, 1f);
                image.SetPixel(x, y, new Color(
                    1f,
                    Mathf.Lerp(0.55f, 0.95f, heat),
                    Mathf.Lerp(0.1f, 0.65f, heat),
                    alpha));
            }
        }
        return ImageTexture.CreateFromImage(image);
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

    // -- Hit feedback (hitmarker + hit/kill sound + impact fx) -----------

    private const float HitmarkSec = 0.3f;
    private const float HitmarkGapPx = 10f;
    private const float HitmarkLenPx = 12f;

    private sealed class Spark
    {
        public MeshInstance3D Node;
        public StandardMaterial3D Mat;
        public float Ttl;
        public float Max;
    }

    private sealed class FloatText
    {
        public Label3D Label;
        public float Ttl;
        public float Max;
    }

    private readonly System.Collections.Generic.List<Spark> _sparks = new();
    private readonly System.Collections.Generic.List<FloatText> _texts = new();

    private void BuildHitmarker()
    {
        _hitLayer = new CanvasLayer { Layer = 10 };
        AddChild(_hitLayer);
        for (int i = 0; i < 4; i++)
        {
            var line = new Line2D
            {
                Width = 2f,
                DefaultColor = new Color(1f, 1f, 1f),
                Antialiased = true,
            };
            line.AddPoint(Vector2.Zero);
            line.AddPoint(Vector2.Zero);
            _hitLayer.AddChild(line);
            _hitLines[i] = line;
        }
        _hitLayer.Visible = false;
    }

    private void ShowHitmarker(bool kill)
    {
        _hitmarkLeft = HitmarkSec;
        _hitmarkKill = kill;
    }

    private void UpdateHitmarker(float dt)
    {
        if (_hitLayer == null)
            return;
        if (_hitmarkLeft <= 0f)
        {
            _hitLayer.Visible = false;
            return;
        }
        _hitmarkLeft -= dt;
        _hitLayer.Visible = true;
        Color color = _hitmarkKill ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 1f, 1f);
        Vector2 center = GetViewport().GetVisibleRect().Size * 0.5f;
        float g = HitmarkGapPx;
        float l = HitmarkLenPx;
        Vector2[] starts = new Vector2[]
        {
            center + new Vector2(-g - l, -g - l),
            center + new Vector2(g, -g - l),
            center + new Vector2(-g - l, g),
            center + new Vector2(g, g),
        };
        Vector2[] ends = new Vector2[]
        {
            center + new Vector2(-g, -g),
            center + new Vector2(g + l, -g),
            center + new Vector2(-g, g + l),
            center + new Vector2(g + l, g + l),
        };
        for (int i = 0; i < 4; i++)
        {
            _hitLines[i].DefaultColor = color;
            _hitLines[i].Points = new[] { starts[i], ends[i] };
        }
    }

    private void BuildHitSound()
    {
        _hitStream = MakeClick(1250f, 0.07f, 21);
        _killStream = MakeClick(700f, 0.14f, 22);
        _hitPlayer = new AudioStreamPlayer();
        AddChild(_hitPlayer);
    }

    private static AudioStreamWav MakeClick(float freqHz, float durSec, int seed)
    {
        const int mixRate = 22050;
        int frames = Mathf.Max(1, (int)(mixRate * durSec));
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)seed;
        byte[] data = new byte[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            float time = (float)i / mixRate;
            float tone = Mathf.Sin(Mathf.Tau * freqHz * time) * Mathf.Exp(-time * 60f);
            float noise = rng.RandfRange(-1f, 1f) * Mathf.Exp(-time * 80f) * 0.3f;
            float s = Mathf.Clamp(tone * 0.6f + noise, -1f, 1f);
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

    private void PlayHitSound(bool kill)
    {
        if (_hitPlayer == null)
            return;
        _hitPlayer.Stream = kill ? _killStream : _hitStream;
        _hitPlayer.Play();
    }

    /// <summary>Yellow-white puff where a bullet stops on a wall.</summary>
    private void SpawnWallSpark(Vector3 pos)
    {
        SpawnSpark(pos, new Color(1f, 0.85f, 0.4f), 2.5f, 0.3f);
    }

    /// <summary>
    /// Red puff + floating damage number on billed flesh hits.
    /// White body, orange head, red kill ("!").
    /// </summary>
    private void SpawnFleshFx(Vector3 pos, int damage, bool kill, bool head)
    {
        SpawnSpark(pos, new Color(0.9f, 0.15f, 0.1f), 4f, 0.4f);
        Node scene = GetTree().CurrentScene;
        if (scene == null)
            return;
        Color color = kill
            ? new Color(1f, 0.3f, 0.2f)
            : head ? new Color(1f, 0.65f, 0.2f) : new Color(1f, 1f, 1f);
        var label = new Label3D
        {
            FontSize = 64,
            PixelSize = 0.35f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Modulate = color,
            OutlineSize = 12,
            OutlineModulate = new Color(0f, 0f, 0f),
        };
        label.Text = kill ? damage.ToString() + "!" : damage.ToString();
        scene.AddChild(label);
        label.GlobalPosition = pos + Vector3.Up * 16f;
        _texts.Add(new FloatText { Label = label, Ttl = 0.7f, Max = 0.7f });
        if (_texts.Count > 32)
        {
            _texts[0].Label.QueueFree();
            _texts.RemoveAt(0);
        }
    }

    private void SpawnSpark(Vector3 pos, Color color, float radius, float ttl)
    {
        Node scene = GetTree().CurrentScene;
        if (scene == null || _sparkMesh == null)
            return;
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var node = new MeshInstance3D
        {
            Mesh = _sparkMesh,
        };
        node.SetSurfaceOverrideMaterial(0, mat);
        scene.AddChild(node);
        node.GlobalPosition = pos;
        node.Scale = Vector3.One * (radius / 3f);
        _sparks.Add(new Spark { Node = node, Mat = mat, Ttl = ttl, Max = ttl });
        if (_sparks.Count > 32)
        {
            _sparks[0].Node.QueueFree();
            _sparks.RemoveAt(0);
        }
    }

    private void UpdateFx(float dt)
    {
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            Spark s = _sparks[i];
            s.Ttl -= dt;
            if (s.Ttl <= 0f || !IsInstanceValid(s.Node))
            {
                if (IsInstanceValid(s.Node))
                    s.Node.QueueFree();
                _sparks.RemoveAt(i);
            }
            else
            {
                s.Mat.AlbedoColor = new Color(
                    s.Mat.AlbedoColor.R, s.Mat.AlbedoColor.G, s.Mat.AlbedoColor.B,
                    Mathf.Clamp(s.Ttl / s.Max, 0f, 1f));
            }
        }
        for (int i = _texts.Count - 1; i >= 0; i--)
        {
            FloatText t = _texts[i];
            t.Ttl -= dt;
            if (t.Ttl <= 0f || !IsInstanceValid(t.Label))
            {
                if (IsInstanceValid(t.Label))
                    t.Label.QueueFree();
                _texts.RemoveAt(i);
            }
            else
            {
                t.Label.GlobalPosition += Vector3.Up * 30f * dt;
                Color c = t.Label.Modulate;
                c.A = Mathf.Clamp(t.Ttl / t.Max, 0f, 1f);
                t.Label.Modulate = c;
            }
        }
    }

    /// <summary>
    /// Beam from the muzzle to the bullet's end: shows the real spray path
    /// (recoil + spread), so misses are readable instead of luck.
    /// </summary>
    private void SpawnTracer(Vector3 end)
    {
        Node scene = GetTree().CurrentScene;
        if (scene == null || _beamMesh == null)
            return;
        Vector3 from = _muzzle != null
            ? _muzzle.GlobalPosition
            : Camera != null ? Camera.GlobalTransform.Origin : end;
        float dist = from.DistanceTo(end);
        if (dist < 4f)
            return;
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.95f, 0.6f, 0.7f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        var node = new MeshInstance3D { Mesh = _beamMesh };
        node.SetSurfaceOverrideMaterial(0, mat);
        scene.AddChild(node);
        node.GlobalPosition = (from + end) * 0.5f;
        node.LookAt(end, Vector3.Up);
        node.Scale = new Vector3(1f, 1f, dist);
        _sparks.Add(new Spark { Node = node, Mat = mat, Ttl = 0.07f, Max = 0.07f });
        if (_sparks.Count > 32)
        {
            _sparks[0].Node.QueueFree();
            _sparks.RemoveAt(0);
        }
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
