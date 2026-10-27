using Godot;

namespace FrutaCS.Bots;

/// <summary>
/// Asset-free low-poly humanoid for bots: head (with cap and shades),
/// torso, arms with gloves, legs and boots, built from shared Godot
/// primitives on the world layer. Same idea as
/// <see cref="FrutaCS.Weapons.WeaponViewmodel"/>: original procedural art,
/// no third-party models. Feet sit at the node origin, total height 72 u —
/// the same 72 u capsule the collision keeps using, so physics, LOS and
/// the 54 u headshot threshold stay untouched; the head (54..72 u) simply
/// becomes visible.
/// Colors follow the team (CT blue, T orange), matching the nameplates.
/// </summary>
public static class BotModel
{
    /// <summary>Below this horizontal speed the walk cycle stands still.</summary>
    public const float MoveThresholdU = 10f;

    /// <summary>Peak hip/shoulder swing at full run (radians, ~31 deg).</summary>
    public const float MaxSwingRad = 0.55f;

    /// <summary>Shoulders swing against the legs, damped by this factor.</summary>
    public const float ShoulderRatio = 0.75f;

    // -- Palette (shared by every bot: one material per color, not per bot) --

    private static readonly StandardMaterial3D SkinMat = Mat(new Color(0.83f, 0.66f, 0.54f));
    private static readonly StandardMaterial3D GloveMat = Mat(new Color(0.10f, 0.10f, 0.12f));
    private static readonly StandardMaterial3D BootMat = Mat(new Color(0.16f, 0.15f, 0.14f));
    private static readonly StandardMaterial3D BeltMat = Mat(new Color(0.12f, 0.11f, 0.10f));
    private static readonly StandardMaterial3D GlassesMat = MatShaded(new Color(0.05f, 0.05f, 0.06f), 0.6f);
    private static readonly StandardMaterial3D VestTrim = Mat(new Color(0.10f, 0.12f, 0.15f));
    private static readonly StandardMaterial3D SkinShadowMat = Mat(new Color(0.70f, 0.49f, 0.38f));

    private static readonly StandardMaterial3D CtTorso = Mat(new Color(0.20f, 0.42f, 0.85f));
    private static readonly StandardMaterial3D CtPants = Mat(new Color(0.15f, 0.30f, 0.62f));
    private static readonly StandardMaterial3D TTorso = Mat(new Color(0.88f, 0.38f, 0.18f));
    private static readonly StandardMaterial3D TPants = Mat(new Color(0.62f, 0.26f, 0.12f));

    // -- Shared meshes (same rig for every bot) --

    private static readonly BoxMesh LegMesh = new() { Size = new Vector3(11f, 34f, 13f) };
    private static readonly BoxMesh FootMesh = new() { Size = new Vector3(9f, 5f, 17f) };
    private static readonly BoxMesh PelvisMesh = new() { Size = new Vector3(26f, 8f, 15f) };
    private static readonly BoxMesh BeltMesh = new() { Size = new Vector3(27f, 3f, 16f) };
    private static readonly BoxMesh TorsoMesh = new() { Size = new Vector3(30f, 19f, 16f) };
    private static readonly BoxMesh YokeMesh = new() { Size = new Vector3(36f, 5f, 16f) };
    private static readonly BoxMesh UpperArmMesh = new() { Size = new Vector3(8f, 15f, 9f) };
    private static readonly BoxMesh ForeArmMesh = new() { Size = new Vector3(7f, 13f, 8f) };
    private static readonly BoxMesh GloveMesh = new() { Size = new Vector3(8.5f, 7.5f, 8f) };
    private static readonly BoxMesh HeadMesh = new() { Size = new Vector3(17f, 18f, 17f) };
    private static readonly BoxMesh CapMesh = new() { Size = new Vector3(17.5f, 5.5f, 17.5f) };
    private static readonly BoxMesh GlassesMesh = new() { Size = new Vector3(14f, 4f, 2f) };
    private static readonly CapsuleMesh TorsoShape = new() { Radius = 0.5f, Height = 1.8f, RadialSegments = 8, Rings = 3 };
    private static readonly CapsuleMesh LimbShape = new() { Radius = 0.5f, Height = 1.8f, RadialSegments = 7, Rings = 3 };
    private static readonly SphereMesh JointShape = new() { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 };

    /// <summary>
    /// Build the humanoid under <paramref name="root"/> (the scene's "Body"
    /// container, feet at the origin) and return the animated rig pivots.
    /// </summary>
    public static BotRig Build(Node3D root, int team)
    {
        var rig = new BotRig();
        if (root == null)
            return rig;

        bool ct = team == 0;
        StandardMaterial3D torso = ct ? CtTorso : TTorso;
        StandardMaterial3D pants = ct ? CtPants : TPants;

        // Core: pelvis, belt, chest, shoulder yoke.
        AddMesh(root, "Pelvis", PelvisMesh, pants, new Vector3(0f, 41f, 0f));
        AddMesh(root, "Belt", BeltMesh, BeltMat, new Vector3(0f, 44.5f, 0f));
        AddMesh(root, "Torso", TorsoShape, torso, new Vector3(0f, 54f, 0f), new Vector3(30f, 20f, 17f));
        AddMesh(root, "Yoke", YokeMesh, torso, new Vector3(0f, 62f, 0f));
        // A simple tactical vest gives the chest a layered, fitted silhouette.
        AddMesh(root, "Vest", TorsoShape, VestTrim, new Vector3(0f, 54f, -1f), new Vector3(25f, 17f, 17.5f));
        AddMesh(root, "VestPanel", new BoxMesh { Size = new Vector3(17f, 9f, 2f) }, torso, new Vector3(0f, 54f, -9.5f));
        AddMesh(root, "PouchL", new BoxMesh { Size = new Vector3(6f, 7f, 4f) }, BeltMat, new Vector3(-7f, 48f, -10f));
        AddMesh(root, "PouchR", new BoxMesh { Size = new Vector3(6f, 7f, 4f) }, BeltMat, new Vector3(7f, 48f, -10f));

        // Head: spans 54..72 u so the visible head matches the headshot band.
        AddMesh(root, "Neck", JointShape, SkinShadowMat, new Vector3(0f, 59f, 0f), new Vector3(8f, 9f, 8f));
        AddMesh(root, "Head", JointShape, SkinMat, new Vector3(0f, 64f, 0f), new Vector3(14.5f, 18f, 14f));
        AddMesh(root, "Jaw", JointShape, SkinShadowMat, new Vector3(0f, 59.5f, -2f), new Vector3(10f, 6f, 10f));
        AddMesh(root, "EarL", JointShape, SkinMat, new Vector3(-7f, 64f, 0f), new Vector3(3f, 5f, 3f));
        AddMesh(root, "EarR", JointShape, SkinMat, new Vector3(7f, 64f, 0f), new Vector3(3f, 5f, 3f));
        AddMesh(root, "Cap", CapMesh, torso, new Vector3(0f, 69.5f, 0f));
        AddMesh(root, "Glasses", GlassesMesh, GlassesMat, new Vector3(0f, 64.5f, -8.7f));

        // Legs (hip pivots at y=36, feet swing with them).
        BuildLeg(root, "L", -7.5f, pants, rig);
        BuildLeg(root, "R", 7.5f, pants, rig);

        // Arms (shoulder pivots at y=60, gloves hang near the hips).
        BuildArm(root, "L", -19f, torso, rig);
        BuildArm(root, "R", 19f, torso, rig);

        return rig;
    }

    private static void BuildLeg(Node3D root, string side, float x,
        StandardMaterial3D pants, BotRig rig)
    {
        var pivot = new Node3D { Name = "Hip" + side, Position = new Vector3(x, 36f, 0f) };
        root.AddChild(pivot);
        AddMesh(pivot, "Leg" + side, LimbShape, pants, new Vector3(0f, -17f, 0f), new Vector3(11f, 34f, 13f));
        AddMesh(pivot, "Knee" + side, JointShape, pants, new Vector3(0f, -20f, 0f), new Vector3(11f, 12f, 13f));
        AddMesh(pivot, "Foot" + side, FootMesh, BootMat, new Vector3(0f, -33.5f, -3f));
        if (side == "L")
            rig.HipL = pivot;
        else
            rig.HipR = pivot;
    }

    private static void BuildArm(Node3D root, string side, float x,
        StandardMaterial3D torso, BotRig rig)
    {
        var pivot = new Node3D { Name = "Shoulder" + side, Position = new Vector3(x, 60f, 0f) };
        root.AddChild(pivot);
        AddMesh(pivot, "UpperArm" + side, LimbShape, torso, new Vector3(0f, -7.5f, 0f), new Vector3(8f, 17f, 9f));
        AddMesh(pivot, "Elbow" + side, JointShape, torso, new Vector3(0f, -14f, 0f), new Vector3(8f, 9f, 9f));
        AddMesh(pivot, "ForeArm" + side, LimbShape, SkinMat, new Vector3(0f, -20f, 0f), new Vector3(7f, 14f, 8f));
        AddMesh(pivot, "Glove" + side, GloveMesh, GloveMat, new Vector3(0f, -27.5f, 0f));
        if (side == "L")
            rig.ShoulderL = pivot;
        else
            rig.ShoulderR = pivot;
    }

    private static void AddMesh(Node3D parent, string name, BoxMesh mesh,
        StandardMaterial3D mat, Vector3 pos)
    {
        parent.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = mat,
            Position = pos,
        });
    }

    private static void AddMesh(Node3D parent, string name, PrimitiveMesh mesh,
        StandardMaterial3D mat, Vector3 pos, Vector3 size)
    {
        parent.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = mat,
            Position = pos,
            Scale = size,
        });
    }

    private static StandardMaterial3D Mat(Color color) => new() { AlbedoColor = color };

    private static StandardMaterial3D MatShaded(Color color, float metallic) => new()
    {
        AlbedoColor = color,
        Metallic = metallic,
        Roughness = 0.35f,
    };
}

/// <summary>
/// Walk-cycle rig: four pivots driven by horizontal speed. The swing math
/// is pure (see <see cref="NextPhase"/>/<see cref="SwingFor"/>) so the
/// cadence stays testable without the engine; <see cref="Animate"/> only
/// eases toward that target and writes the rotations.
/// </summary>
public sealed class BotRig
{
    /// <summary>Hip pivots (legs); null until <see cref="BotModel.Build"/> runs.</summary>
    public Node3D HipL { get; internal set; }
    public Node3D HipR { get; internal set; }

    /// <summary>Shoulder pivots (arms), swinging against the legs.</summary>
    public Node3D ShoulderL { get; internal set; }
    public Node3D ShoulderR { get; internal set; }

    private float _phase;
    private float _swing;

    /// <summary>
    /// Step phase for the next frame: cadence grows with speed (about 1.5
    /// steps per second at full run), frozen while standing still.
    /// </summary>
    public static float NextPhase(float phase, float speedU, float dt)
        => speedU > BotModel.MoveThresholdU
            ? phase + dt * (3.5f + 6f * Mathf.Clamp(speedU / 250f, 0f, 1.3f))
            : phase;

    /// <summary>Pure swing target in radians; zero when standing still.</summary>
    public static float SwingFor(float phase, float speedU)
        => speedU > BotModel.MoveThresholdU
            ? Mathf.Sin(phase) * BotModel.MaxSwingRad
            : 0f;

    /// <summary>
    /// Ease the rig toward the swing for this frame. Legs alternate,
    /// shoulders counter-swing; everything returns to neutral on stop.
    /// </summary>
    public void Animate(float speedU, float dt)
    {
        _phase = NextPhase(_phase, speedU, dt);
        float target = SwingFor(_phase, speedU);
        _swing = Mathf.Lerp(_swing, target, 1f - Mathf.Exp(-12f * dt));
        if (HipL != null)
            HipL.Rotation = new Vector3(_swing, 0f, 0f);
        if (HipR != null)
            HipR.Rotation = new Vector3(-_swing, 0f, 0f);
        float arm = _swing * BotModel.ShoulderRatio;
        if (ShoulderL != null)
            ShoulderL.Rotation = new Vector3(-arm, 0f, 0f);
        if (ShoulderR != null)
            ShoulderR.Rotation = new Vector3(arm, 0f, 0f);
    }
}
