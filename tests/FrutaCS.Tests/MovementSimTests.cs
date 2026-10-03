using GdUnit4;
using Godot;

using FrutaCS.Player;

namespace FrutaCS.Tests;

[TestSuite]
public class MovementSimTests
{
    private const float Delta = 1f / 60f;

    private static MovementSim FreshSim() => new();

    [TestCase]
    public void RunReaches250Ups()
    {
        var sim = FreshSim();
        var p = MovementParams.Default;
        var input = new MovementInput(Vector3.Forward, false);
        for (int i = 0; i < 120; i++)
            sim.Tick(in input, in p, Delta);
        float speed = new Vector3(sim.Velocity.X, 0f, sim.Velocity.Z).Length();
        Assertions.AssertThat(sim.IsOnFloor).IsTrue();
        Assertions.AssertThat(speed > 245f && speed < 255f).IsTrue();
    }

    [TestCase]
    public void AirStrafeGainsSpeedWithoutCap()
    {
        var sim = FreshSim();
        var p = MovementParams.Default;
        // Build full run speed on the ground first.
        var run = new MovementInput(Vector3.Forward, false);
        for (int i = 0; i < 60; i++)
            sim.Tick(in run, in p, Delta);
        // Leave the floor with a jump, keeping horizontal velocity.
        var jump = new MovementInput(Vector3.Zero, true);
        sim.Tick(in jump, in p, Delta);
        Assertions.AssertThat(sim.IsOnFloor).IsFalse();
        // Strafe perpendicular to motion while airborne: Quake-style
        // air-accelerate with no cap must keep adding speed.
        var strafe = new MovementInput(Vector3.Right, false);
        for (int i = 0; i < 30; i++)
            sim.Tick(in strafe, in p, Delta);
        float speed = new Vector3(sim.Velocity.X, 0f, sim.Velocity.Z).Length();
        Assertions.AssertThat(sim.IsOnFloor).IsFalse();
        Assertions.AssertThat(speed > 300f).IsTrue();
    }

    [TestCase]
    public void FrictionStopsInPlace()
    {
        var sim = FreshSim();
        var p = MovementParams.Default;
        var run = new MovementInput(Vector3.Forward, false);
        for (int i = 0; i < 60; i++)
            sim.Tick(in run, in p, Delta);
        var idle = new MovementInput(Vector3.Zero, false);
        for (int i = 0; i < 120; i++)
            sim.Tick(in idle, in p, Delta);
        float speed = new Vector3(sim.Velocity.X, 0f, sim.Velocity.Z).Length();
        Assertions.AssertThat(sim.IsOnFloor).IsTrue();
        Assertions.AssertThat(speed < 5f).IsTrue();
    }

    [TestCase]
    public void JumpLeavesFloor()
    {
        var sim = FreshSim();
        var p = MovementParams.Default;
        Assertions.AssertThat(sim.IsOnFloor).IsTrue();
        var jump = new MovementInput(Vector3.Zero, true);
        sim.Tick(in jump, in p, Delta);
        Assertions.AssertThat(sim.IsOnFloor).IsFalse();
        Assertions.AssertThat(sim.Velocity.Y > 0f).IsTrue();
    }
}
