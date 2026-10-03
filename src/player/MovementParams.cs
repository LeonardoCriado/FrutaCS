using Godot;

namespace FrutaCS.Player;

/// <summary>
/// Tunable movement constants (units/second based, GoldSrc scale).
/// Spec §5 pins run speed (250 u/s); the remaining defaults are the
/// canonical GoldSrc/CS 1.6 server values (sv_friction 4, sv_gravity 800,
/// sv_accelerate 5, sv_airaccelerate 10, 45u jump height). Task 5 exposes
/// these through a MovementConfig resource.
/// </summary>
public struct MovementParams
{
    public float RunSpeed;
    public float WalkSpeed;
    public float Accelerate;
    public float AirAccelerate;
    public float Friction;
    public float Gravity;
    public float JumpVelocity;

    public static MovementParams Default => new()
    {
        RunSpeed = 250f,
        WalkSpeed = 130f,
        Accelerate = 5f,
        AirAccelerate = 10f,
        Friction = 4f,
        Gravity = 800f,
        JumpVelocity = 268.328157f, // Sqrt(2 * 800 * 45): 45u jump height.
    };
}
