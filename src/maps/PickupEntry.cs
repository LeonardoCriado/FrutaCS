using Godot;

namespace FrutaCS.Maps;

/// <summary>
/// One weapon pickup spot on the map (spec §7: map 1 scatters AK-47,
/// M4A1 and one AWP over mid + side lanes). The scene places
/// <see cref="WeaponPickup"/> nodes at <see cref="Position"/> carrying
/// <see cref="WeaponId"/>; tiers follow <see cref="FrutaCS.Bots.Bot.TierOf"/>
/// (knife 0, deagle 1, rifle 2, awp 3) so the bot glue ranks them.
/// </summary>
[GlobalClass]
public partial class PickupEntry : Resource
{
    [Export] public string WeaponId = "ak47";
    [Export] public Vector3 Position;
}
