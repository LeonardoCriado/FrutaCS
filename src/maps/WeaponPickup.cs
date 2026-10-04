using Godot;

using FrutaCS.Bots;
using FrutaCS.Player;
using FrutaCS.Weapons;

namespace FrutaCS.Maps;

/// <summary>
/// Floor weapon pickup (spec §7 acquisition). Lives in group
/// <c>weapon_pickups</c> with int meta <c>tier</c> (and string meta
/// <c>weapon_id</c>, bool meta <c>taken</c>) — exactly what
/// <see cref="Bot"/> scans. A human walking into the Area3D equips the
/// weapon on the spot; bots claim it through <see cref="Grab"/> from
/// their proximity poll (which also swaps their stats, closing the
/// Task 6 deferred swap). Taken pickups hide until the next round reset
/// (<see cref="ResetPickup"/>, driven by the match glue).
/// </summary>
[GlobalClass]
public partial class WeaponPickup : Area3D
{
    [Export] public string WeaponId = "ak47";

    /// <summary>Body-entered grants only inside this horizontal range.
    /// Guards a real engine quirk: teleporting out of a disabled Area3D
    /// misses the exit, and re-enabling monitoring can refire entered for
    /// the stale overlap (grabbing a gun from across the map on round
    /// reset). Bots are immune (their poll already range-checks).</summary>
    public const float ClaimRadiusU = 64f;

    public bool Taken { get; private set; }
    public int Tier => Bot.TierOf(WeaponId);

    private MeshInstance3D _mesh;
    private CollisionShape3D _shape;
    private float _restY;

    public override void _Ready()
    {
        AddToGroup("weapon_pickups");
        SetMeta("tier", Tier);
        SetMeta("weapon_id", WeaponId);
        SetMeta("taken", false);
        _mesh = GetNodeOrNull<MeshInstance3D>("Mesh");
        _shape = GetNodeOrNull<CollisionShape3D>("Shape");
        _restY = Position.Y;
        PaintTierColor();
        BodyEntered += OnBodyEntered;
    }

    /// <summary>
    /// Claim the gun. Returns the weapon id, or "" when already taken.
    /// Idempotent: only the first claimant (human or bot) is armed.
    /// </summary>
    public string Grab()
    {
        if (Taken)
            return "";
        Taken = true;
        SetMeta("taken", true);
        SetDeferred("monitoring", false);
        if (_shape != null)
            _shape.SetDeferred("disabled", true);
        if (_mesh != null)
            _mesh.Visible = false;
        return WeaponId;
    }

    /// <summary>Round reset: the gun is back on the floor.</summary>
    public void ResetPickup()
    {
        Taken = false;
        SetMeta("taken", false);
        SetDeferred("monitoring", true);
        if (_shape != null)
            _shape.SetDeferred("disabled", false);
        if (_mesh != null)
            _mesh.Visible = true;
        Position = new Vector3(Position.X, _restY, Position.Z);
    }

    private void OnBodyEntered(Node3D body)
    {
        if (Taken)
            return;
        if (body is not PlayerBody player || player.IsDead)
            return;
        // Full 3D range (same guard as ClaimRadiusU): the AWP sits a level
        // below the deck and must not be claimable through the floor.
        if (body.GlobalPosition.DistanceTo(GlobalPosition) > ClaimRadiusU)
            return; // Stale entered (see ClaimRadiusU): never bill it.
        WeaponData data = GD.Load<WeaponData>($"res://data/weapons/{WeaponId}.tres");
        if (data == null)
            return;
        if (Grab() == "")
            return;
        player.ArmedWeapon?.Equip(data);
    }

    private void PaintTierColor()
    {
        if (_mesh == null)
            return;
        Color color = WeaponId switch
        {
            "awp" => new Color(0.95f, 0.75f, 0.25f),
            "ak47" => new Color(0.85f, 0.45f, 0.2f),
            "m4a1" => new Color(0.25f, 0.6f, 0.9f),
            _ => new Color(0.7f, 0.7f, 0.7f),
        };
        _mesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = color });
    }
}
