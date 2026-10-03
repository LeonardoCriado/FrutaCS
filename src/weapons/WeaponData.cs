using Godot;

namespace FrutaCS.Weapons;

/// <summary>
/// Player stance for stance-dependent spread. Order matches the
/// 1.6 spread branches: grounded-standing, moving, airborne, ducking.
/// </summary>
public enum Stance
{
    Stand,
    Move,
    Air,
    Duck,
}

/// <summary>
/// Engine-free weapon constants. WeaponSim takes this struct so the
/// simulation stays constructible under plain `dotnet test` (no engine).
/// <see cref="WeaponData"/> (the Godot Resource) converts via ToStats().
/// </summary>
public readonly record struct WeaponStats(
    string WeaponId,
    float Damage,
    float SecondaryDamage,
    float HeadshotMult,
    float ArmorPenetration,
    int Rpm,
    int MagSize,
    int ReserveAmmo,
    float SpreadStandDeg,
    float SpreadMoveDeg,
    float SpreadAirDeg,
    float SpreadDuckDeg,
    float RangeModifier,
    int PenetrationStages,
    float MaxWallThicknessU,
    float MeleeRangeU,
    Vector2[] RecoilTable
);

/// <summary>
/// Per-weapon data table, editable as a Godot Resource
/// (<c>data/weapons/*.tres</c>). Values are Counter-Strike 1.6 reference
/// values; each .tres file carries its source as a top comment.
/// Engine-free: only Godot math structs (Vector2), no Node/runtime APIs.
/// </summary>
[GlobalClass]
public partial class WeaponData : Resource
{
    [Export] public string WeaponId = "";
    [Export] public float Damage;
    [Export] public float SecondaryDamage;
    [Export] public float HeadshotMult = 4f;
    [Export] public float ArmorPenetration = 0.5f;
    [Export] public int Rpm;
    [Export] public int MagSize;
    [Export] public int ReserveAmmo;
    [Export] public float SpreadStandDeg;
    [Export] public float SpreadMoveDeg;
    [Export] public float SpreadAirDeg;
    [Export] public float SpreadDuckDeg;
    [Export] public float RangeModifier = 1f;
    [Export] public int PenetrationStages;
    [Export] public float MaxWallThicknessU;
    [Export] public float MeleeRangeU;
    [Export] public Vector2[] RecoilTable = [];

    public WeaponStats ToStats() => new(
        WeaponId,
        Damage,
        SecondaryDamage,
        HeadshotMult,
        ArmorPenetration,
        Rpm,
        MagSize,
        ReserveAmmo,
        SpreadStandDeg,
        SpreadMoveDeg,
        SpreadAirDeg,
        SpreadDuckDeg,
        RangeModifier,
        PenetrationStages,
        MaxWallThicknessU,
        MeleeRangeU,
        (Vector2[])RecoilTable.Clone()
    );
}
