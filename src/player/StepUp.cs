using Godot;

namespace FrutaCS.Player;

/// <summary>
/// Seam step-up for CharacterBody3D glue (bots and the human share it).
/// The map is authored from boxes, so slope-flat joints meet at sharp
/// convex kinks (ramp toes, rim lips, island/bridge feet). A capsule can
/// pinch exactly on such a kink: floor below, slope ahead, edge face
/// behind — contacts cancel and motion reads zero forever. Real games
/// solve this with a step-up; GoldSrc has sv_stepsize for the same reason.
/// TryStep moves the body up-forward along one diagonal sweep (never
/// through a ceiling or wall — the sweep itself is collision-tested), so
/// tall walls and cover faces are never climbed: the sweep hits anything
/// the capsule couldn't already walk into. Step height 24u over a 20u
/// stride clears seams and lips; real covers stay 64u (jumped, never
/// stepped). A pure vertical pop is not enough: the body would fall back
/// into the same pinch. Engine-side only (TestMove needs a physics body);
/// pure sims untouched.
/// </summary>
public static class StepUp
{
    public const float HeightU = 24f;
    public const float StrideU = 20f;

    /// <summary>
    /// Step up-forward over a seam. Returns true when moved; the caller's
    /// normal motion next tick walks off the new position.
    /// </summary>
    public static bool TryStep(CharacterBody3D body, Vector3 wishDir, float intendedSpeedU)
    {
        Vector3 flat = new(wishDir.X, 0f, wishDir.Z);
        if (flat.Length() < 0.5f || intendedSpeedU <= 0f)
            return false;
        flat = flat.Normalized();
        Vector3 motion = Vector3.Up * HeightU + flat * StrideU;
        if (body.TestMove(body.GlobalTransform, motion))
            return false; // Ceiling, tall wall or cover face: never through.
        body.GlobalPosition += motion;
        return true;
    }
}
