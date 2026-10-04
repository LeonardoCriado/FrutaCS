using Godot;

namespace FrutaCS.Player;

/// <summary>
/// Movement constants as an editable Godot Resource. Defaults mirror
/// <see cref="MovementParams.Default"/> (RunSpeed 250 u/s per spec §5;
/// the rest are canonical GoldSrc/CS 1.6 server values, see
/// MovementParams.cs). PlayerBody converts via <see cref="ToParams"/>
/// and drives <see cref="MovementSim"/> with the result.
/// </summary>
[GlobalClass]
public partial class MovementConfig : Resource
{
    [Export] public float RunSpeed = 250f;
    [Export] public float WalkSpeed = 130f;
    [Export] public float Accelerate = 5f;
    [Export] public float AirAccelerate = 10f;
    [Export] public float Friction = 4f;
    [Export] public float Gravity = 800f;
    [Export] public float JumpVelocity = 268.328157f; // Sqrt(2 * 800 * 45): 45u jump height.

    public MovementParams ToParams() => new()
    {
        RunSpeed = RunSpeed,
        WalkSpeed = WalkSpeed,
        Accelerate = Accelerate,
        AirAccelerate = AirAccelerate,
        Friction = Friction,
        Gravity = Gravity,
        JumpVelocity = JumpVelocity,
    };
}
