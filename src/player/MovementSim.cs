using System;
using Godot;

namespace FrutaCS.Player;

/// <summary>
/// Per-tick player intent. WishDir is a horizontal direction; its length
/// scales the wish speed (0 = no input, 1 = full run).
/// </summary>
public readonly record struct MovementInput(Vector3 WishDir, bool JumpPressed);

/// <summary>
/// Pure Quake/GoldSrc-style movement simulation. Engine-free: only
/// Godot.Vector3 math, no Node/SceneTree APIs. Ground plane is y = 0.
/// Accelerations are multiplied by delta exactly once (fixed-step safe).
/// </summary>
public sealed class MovementSim
{
    private const float StopSpeed = 100f; // sv_stopspeed: keeps low-speed friction snappy.

    private float _height;

    public Vector3 Velocity { get; private set; } = Vector3.Zero;
    public bool IsOnFloor { get; private set; } = true;

    public void Tick(in MovementInput input, in MovementParams p, float delta)
    {
        Vector3 wishDir = new(input.WishDir.X, 0f, input.WishDir.Z);
        float wishLen = wishDir.Length();
        Vector3 wishDirN = Vector3.Zero;
        float wishSpeed = 0f;
        if (wishLen > 0.0001f)
        {
            wishDirN = wishDir / wishLen;
            wishSpeed = p.RunSpeed * MathF.Min(1f, wishLen);
        }

        Vector3 vel = Velocity;
        if (IsOnFloor)
        {
            if (input.JumpPressed)
            {
                // Jump tick still steers: air-accelerate applies before
                // leaving the floor (Task 5 carried fix for the 1-tick
                // horizontal freeze; wishdir here is typically the bhop
                // strafe direction held through the jump).
                vel = Accelerate(vel, wishDirN, wishSpeed, p.AirAccelerate, delta);
                vel.Y = p.JumpVelocity;
                IsOnFloor = false;
            }
            else
            {
                vel = ApplyFriction(vel, p, delta);
                vel = Accelerate(vel, wishDirN, wishSpeed, p.Accelerate, delta);
                vel.Y = 0f;
            }
        }
        else
        {
            // No air-speed cap: strafing with air-accelerate keeps gaining (bhop).
            vel = Accelerate(vel, wishDirN, wishSpeed, p.AirAccelerate, delta);
            vel.Y -= p.Gravity * delta;
        }

        Velocity = vel;
        _height += vel.Y * delta;
        if (_height <= 0f)
        {
            _height = 0f;
            if (vel.Y <= 0f)
            {
                Velocity = new Vector3(vel.X, 0f, vel.Z);
                IsOnFloor = true;
            }
        }
    }

    private static Vector3 ApplyFriction(Vector3 vel, in MovementParams p, float delta)
    {
        Vector3 h = new(vel.X, 0f, vel.Z);
        float speed = h.Length();
        if (speed <= 0.0001f)
            return new Vector3(0f, vel.Y, 0f);
        float control = MathF.Max(speed, StopSpeed);
        float drop = control * p.Friction * delta;
        float newSpeed = MathF.Max(0f, speed - drop);
        float scale = newSpeed / speed;
        return new Vector3(h.X * scale, vel.Y, h.Z * scale);
    }

    private static Vector3 Accelerate(Vector3 vel, Vector3 wishDirN, float wishSpeed, float accel, float delta)
    {
        if (wishSpeed <= 0f)
            return vel;
        float current = vel.X * wishDirN.X + vel.Z * wishDirN.Z;
        float add = wishSpeed - current;
        if (add <= 0f)
            return vel;
        float push = MathF.Min(accel * wishSpeed * delta, add);
        return new Vector3(vel.X + wishDirN.X * push, vel.Y, vel.Z + wishDirN.Z * push);
    }
}
