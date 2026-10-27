using System.Collections.Generic;
using Godot;

namespace FrutaCS.Weapons;

/// <summary>Rest pose and muzzle offset for a first-person weapon model.</summary>
public readonly record struct WeaponPose(
    Vector3 Position,
    Vector3 RotationDegrees,
    Vector3 Muzzle,
    float Scale
);

/// <summary>
/// Asset-free low-poly viewmodels built from Godot primitives. Each weapon has
/// its own silhouette, palette and muzzle point (rifles, sniper, pistol, knife)
/// instead of one scaled box. Parts live on <see cref="ViewmodelLayer"/> so a
/// camera-local fill light gives them readable shape without touching the map.
/// </summary>
public static class WeaponViewmodel
{
    /// <summary>Render layer used by every viewmodel mesh (layer 2).</summary>
    public const uint ViewmodelLayer = 1u << 1;

    /// <summary>
    /// Rest pose plus the two first-person hand anchors (root-local):
    /// <see cref="Grip"/> on the pistol grip and <see cref="Support"/>
    /// where the off-hand holds the weapon.
    /// </summary>
    private sealed record Spec(
        Vector3 Position,
        Vector3 RotationDegrees,
        Vector3 Muzzle,
        float Scale,
        Vector3 Grip,
        Vector3 Support
    );

    private static readonly Vector3 FallbackPosition = new(0.25f, -0.22f, -0.45f);
    private static readonly Vector3 FallbackMuzzle = new(0f, 0.02f, -0.4f);

    private static readonly Dictionary<string, Spec> Specs = new()
    {
        ["ak47"] = new(
            new Vector3(0.20f, -0.19f, -0.62f),
            new Vector3(0f, 3f, 0f),
            new Vector3(0f, 0.014f, -0.57f),
            0.85f,
            new Vector3(0f, -0.09f, 0.095f),
            new Vector3(0f, -0.03f, -0.28f)),
        ["m4a1"] = new(
            new Vector3(0.195f, -0.19f, -0.62f),
            new Vector3(0f, 3f, 0f),
            new Vector3(0f, 0.014f, -0.59f),
            0.85f,
            new Vector3(0f, -0.09f, 0.10f),
            new Vector3(0f, -0.03f, -0.28f)),
        ["awp"] = new(
            new Vector3(0.21f, -0.20f, -0.66f),
            new Vector3(0f, 3f, 0f),
            new Vector3(0f, 0.02f, -0.70f),
            0.9f,
            new Vector3(0f, -0.10f, 0.10f),
            new Vector3(0f, -0.04f, -0.40f)),
        ["deagle"] = new(
            new Vector3(0.18f, -0.17f, -0.50f),
            new Vector3(0f, 3f, 0f),
            new Vector3(0f, 0.02f, -0.23f),
            0.9f,
            new Vector3(0f, -0.12f, 0.085f),
            new Vector3(0.02f, -0.135f, 0.055f)),
        ["knife"] = new(
            new Vector3(0.17f, -0.15f, -0.42f),
            new Vector3(0f, 10f, -8f),
            new Vector3(0f, 0f, -0.295f),
            0.95f,
            new Vector3(0f, -0.01f, 0.095f),
            new Vector3(0f, -0.01f, 0.03f)),
    };

    /// <summary>True when the id has a hand-built silhouette (not the fallback).</summary>
    public static bool HasCustomModel(string weaponId)
        => !string.IsNullOrEmpty(weaponId) && Specs.ContainsKey(weaponId);

    /// <summary>Spec for an id, falling back to the generic pose + anchors.</summary>
    private static Spec SpecFor(string weaponId)
        => weaponId != null && Specs.TryGetValue(weaponId, out Spec s)
            ? s
            : new Spec(FallbackPosition, Vector3.Zero, FallbackMuzzle, 1f,
                new Vector3(0f, -0.10f, 0.10f), new Vector3(0f, -0.10f, -0.10f));

    public static WeaponPose PoseFor(string weaponId)
    {
        Spec spec = SpecFor(weaponId);
        return new WeaponPose(spec.Position, spec.RotationDegrees, spec.Muzzle, spec.Scale);
    }

    /// <summary>
    /// Rebuild the model under <paramref name="root"/> (the scene's Viewmodel
    /// node) and move <paramref name="muzzle"/> to the current barrel tip.
    /// <paramref name="team"/> colors the sleeve of the holding hands
    /// (0 = CT blue, anything else = T orange).
    /// </summary>
    public static WeaponPose Build(Node3D root, Marker3D muzzle, string weaponId, int team = 0)
    {
        WeaponPose pose = PoseFor(weaponId);
        if (root == null)
            return pose;

        foreach (Node child in root.GetChildren())
        {
            if (child == muzzle)
                continue;
            root.RemoveChild(child);
            child.QueueFree();
        }

        var model = new Node3D { Name = "Model" };
        root.AddChild(model);
        root.Scale = Vector3.One * pose.Scale;

        switch (weaponId)
        {
            case "ak47":
                BuildAk47(model);
                break;
            case "m4a1":
                BuildM4a1(model);
                break;
            case "awp":
                BuildAwp(model);
                break;
            case "deagle":
                BuildDeagle(model);
                break;
            case "knife":
                BuildKnife(model);
                break;
            default:
                BuildFallback(model);
                break;
        }

        AddFillLight(model);
        AddArms(model, weaponId, team);
        if (muzzle != null)
            muzzle.Position = pose.Muzzle;
        return pose;
    }

    // -- Holding hands (first-person arms) ---------------------------------

    /// <summary>
    /// Two gloved arms: the right hand on the pistol grip, the left hand
    /// supporting the weapon forward. Sleeves take the player's team
    /// color; the arms ride the viewmodel node, so they inherit the bob,
    /// sway, reload dip and knife swing for free.
    /// </summary>
    private static void AddArms(Node3D model, string weaponId, int team)
    {
        Spec spec = SpecFor(weaponId);
        StandardMaterial3D sleeve = Mat(
            team == 0 ? new Color(0.20f, 0.42f, 0.85f) : new Color(0.88f, 0.38f, 0.18f), 0.05f, 0.7f);
        StandardMaterial3D glove = Mat(new Color(0.10f, 0.10f, 0.12f), 0.1f, 0.6f);
        StandardMaterial3D skin = Mat(new Color(0.83f, 0.66f, 0.54f), 0.0f, 0.65f);

        // Right hand on the grip: arm drops back and out to the lower right.
        BuildArm(model, "Right", spec.Grip, sleeve, glove, skin,
            new Vector3(22f, 0f, 38f));
        // Left hand supporting: reaches further forward, crossing from the left.
        BuildArm(model, "Left", spec.Support, sleeve, glove, skin,
            new Vector3(30f, 0f, -40f));
    }

    private static void BuildArm(Node3D model, string side, Vector3 anchor,
        StandardMaterial3D sleeve, StandardMaterial3D glove, StandardMaterial3D skin,
        Vector3 rotationDeg)
    {
        var pivot = new Node3D { Name = "Hand" + side, Position = anchor, RotationDegrees = rotationDeg };
        model.AddChild(pivot);
        AddBox(pivot, "Sleeve" + side, new Vector3(0.075f, 0.30f, 0.075f),
            new Vector3(0f, -0.145f, 0f), sleeve);
        AddBox(pivot, "Wrist" + side, new Vector3(0.062f, 0.03f, 0.062f),
            new Vector3(0f, -0.305f, 0f), skin);
        AddBox(pivot, "Glove" + side, new Vector3(0.088f, 0.095f, 0.105f),
            new Vector3(0f, -0.345f, 0f), glove);
    }

    // -- Weapons ----------------------------------------------------------

    private static void BuildAk47(Node3D root)
    {
        StandardMaterial3D steel = Mat(new Color(0.34f, 0.35f, 0.37f), 0.45f, 0.38f);
        StandardMaterial3D black = Mat(new Color(0.13f, 0.135f, 0.145f), 0.12f, 0.50f);
        StandardMaterial3D wood = Mat(new Color(0.38f, 0.21f, 0.10f), 0.0f, 0.55f);

        AddBox(root, "Receiver", new Vector3(0.075f, 0.105f, 0.30f), Vector3.Zero, steel);
        AddBox(root, "DustCover", new Vector3(0.07f, 0.018f, 0.28f), new Vector3(0f, 0.06f, -0.01f), black);
        AddBox(root, "Stock", new Vector3(0.062f, 0.085f, 0.235f), new Vector3(0f, -0.005f, 0.265f), wood, new Vector3(-3f, 0f, 0f));
        AddBox(root, "ButtPlate", new Vector3(0.065f, 0.10f, 0.018f), new Vector3(0f, -0.005f, 0.39f), black);
        AddBox(root, "Handguard", new Vector3(0.072f, 0.075f, 0.21f), new Vector3(0f, 0.005f, -0.26f), wood);
        AddCylinder(root, "GasTube", 0.011f, 0.32f, new Vector3(0f, 0.052f, -0.27f), steel);
        AddCylinder(root, "Barrel", 0.0145f, 0.25f, new Vector3(0f, 0.012f, -0.43f), black);
        AddCylinder(root, "MuzzleBrake", 0.022f, 0.045f, new Vector3(0f, 0.012f, -0.545f), steel);
        AddBox(root, "GasBlock", new Vector3(0.032f, 0.032f, 0.03f), new Vector3(0f, 0.045f, -0.47f), black);
        AddBox(root, "FrontSightBase", new Vector3(0.031f, 0.022f, 0.03f), new Vector3(0f, 0.062f, -0.50f), black);
        AddBox(root, "FrontSightPost", new Vector3(0.009f, 0.035f, 0.01f), new Vector3(0f, 0.086f, -0.50f), steel);
        AddBox(root, "RearSight", new Vector3(0.028f, 0.014f, 0.018f), new Vector3(0f, 0.075f, 0.08f), black);
        AddBox(root, "ChargingHandle", new Vector3(0.018f, 0.016f, 0.045f), new Vector3(0.048f, 0.045f, 0.07f), steel);

        AddBox(root, "MagazineTop", new Vector3(0.05f, 0.13f, 0.075f), new Vector3(0f, -0.105f, -0.005f), black, new Vector3(8f, 0f, 0f));
        AddBox(root, "MagazineMid", new Vector3(0.048f, 0.10f, 0.07f), new Vector3(0f, -0.19f, -0.025f), black, new Vector3(20f, 0f, 0f));
        AddBox(root, "MagazineFloor", new Vector3(0.052f, 0.018f, 0.075f), new Vector3(0f, -0.235f, -0.06f), steel, new Vector3(20f, 0f, 0f));

        AddBox(root, "Grip", new Vector3(0.048f, 0.13f, 0.06f), new Vector3(0f, -0.09f, 0.095f), wood, new Vector3(-18f, 0f, 0f));
        AddBox(root, "TriggerGuard", new Vector3(0.035f, 0.012f, 0.09f), new Vector3(0f, -0.065f, 0.04f), steel);
        AddBox(root, "Trigger", new Vector3(0.009f, 0.028f, 0.012f), new Vector3(0f, -0.052f, 0.035f), steel, new Vector3(-12f, 0f, 0f));
    }

    private static void BuildM4a1(Node3D root)
    {
        StandardMaterial3D metal = Mat(new Color(0.32f, 0.33f, 0.35f), 0.5f, 0.30f);
        StandardMaterial3D polymer = Mat(new Color(0.13f, 0.135f, 0.145f), 0.08f, 0.58f);
        StandardMaterial3D dark = Mat(new Color(0.08f, 0.085f, 0.095f), 0.15f, 0.50f);

        AddBox(root, "Receiver", new Vector3(0.065f, 0.095f, 0.30f), Vector3.Zero, polymer);
        AddBox(root, "TopRail", new Vector3(0.045f, 0.012f, 0.34f), new Vector3(0f, 0.06f, 0.0f), metal);
        for (int i = 0; i < 6; i++)
        {
            AddBox(root, "RailTooth" + i, new Vector3(0.052f, 0.008f, 0.012f),
                new Vector3(0f, 0.069f, -0.14f + i * 0.052f), dark);
        }

        AddBox(root, "Handguard", new Vector3(0.06f, 0.065f, 0.22f), new Vector3(0f, 0.005f, -0.26f), polymer);
        AddBox(root, "HandguardRail", new Vector3(0.05f, 0.01f, 0.18f), new Vector3(0f, 0.045f, -0.26f), metal);
        AddBox(root, "SideRail", new Vector3(0.012f, 0.032f, 0.17f), new Vector3(0.038f, 0.005f, -0.26f), metal);
        AddCylinder(root, "Barrel", 0.013f, 0.26f, new Vector3(0f, 0.012f, -0.44f), metal);
        AddCylinder(root, "FlashHider", 0.02f, 0.06f, new Vector3(0f, 0.012f, -0.56f), dark);

        AddCylinder(root, "StockTube", 0.017f, 0.11f, new Vector3(0f, 0.015f, 0.22f), metal);
        AddBox(root, "Stock", new Vector3(0.058f, 0.085f, 0.16f), new Vector3(0f, -0.005f, 0.33f), polymer, new Vector3(-2f, 0f, 0f));
        AddBox(root, "ButtPlate", new Vector3(0.06f, 0.09f, 0.015f), new Vector3(0f, -0.005f, 0.415f), dark);

        AddBox(root, "Magazine", new Vector3(0.05f, 0.17f, 0.08f), new Vector3(0f, -0.135f, -0.015f), polymer, new Vector3(5f, 0f, 0f));
        AddBox(root, "MagazineFloor", new Vector3(0.054f, 0.016f, 0.085f), new Vector3(0f, -0.222f, -0.023f), dark, new Vector3(5f, 0f, 0f));
        AddBox(root, "Grip", new Vector3(0.045f, 0.13f, 0.06f), new Vector3(0f, -0.09f, 0.10f), polymer, new Vector3(-18f, 0f, 0f));
        AddBox(root, "TriggerGuard", new Vector3(0.035f, 0.012f, 0.09f), new Vector3(0f, -0.065f, 0.04f), metal);
        AddBox(root, "Trigger", new Vector3(0.009f, 0.028f, 0.012f), new Vector3(0f, -0.052f, 0.035f), metal, new Vector3(-12f, 0f, 0f));
        AddBox(root, "RearSight", new Vector3(0.03f, 0.025f, 0.02f), new Vector3(0f, 0.081f, 0.07f), dark);
        AddBox(root, "FrontSightBase", new Vector3(0.03f, 0.025f, 0.025f), new Vector3(0f, 0.06f, -0.45f), dark);
        AddBox(root, "FrontSightPost", new Vector3(0.008f, 0.03f, 0.008f), new Vector3(0f, 0.086f, -0.45f), metal);
    }

    private static void BuildAwp(Node3D root)
    {
        StandardMaterial3D steel = Mat(new Color(0.30f, 0.31f, 0.33f), 0.5f, 0.33f);
        StandardMaterial3D olive = Mat(new Color(0.28f, 0.33f, 0.23f), 0.05f, 0.68f);
        StandardMaterial3D black = Mat(new Color(0.09f, 0.095f, 0.10f), 0.15f, 0.55f);
        StandardMaterial3D lens = Mat(new Color(0.04f, 0.16f, 0.18f), 0.4f, 0.15f, new Color(0.10f, 0.38f, 0.42f), 0.5f);

        AddBox(root, "Receiver", new Vector3(0.07f, 0.09f, 0.28f), Vector3.Zero, black);
        AddBox(root, "Stock", new Vector3(0.075f, 0.10f, 0.30f), new Vector3(0f, -0.01f, 0.27f), olive, new Vector3(-5f, 0f, 0f));
        AddBox(root, "CheekRest", new Vector3(0.06f, 0.03f, 0.12f), new Vector3(0f, 0.055f, 0.24f), black);
        AddBox(root, "ButtPlate", new Vector3(0.078f, 0.11f, 0.02f), new Vector3(0f, -0.005f, 0.43f), black);
        AddBox(root, "Forend", new Vector3(0.07f, 0.075f, 0.30f), new Vector3(0f, 0.0f, -0.28f), olive);
        AddCylinder(root, "Barrel", 0.015f, 0.36f, new Vector3(0f, 0.015f, -0.46f), steel);
        AddCylinder(root, "MuzzleBrake", 0.024f, 0.07f, new Vector3(0f, 0.015f, -0.625f), black);

        AddCylinder(root, "ScopeTube", 0.030f, 0.24f, new Vector3(0f, 0.105f, -0.02f), black);
        AddCylinder(root, "ScopeFrontBell", 0.038f, 0.07f, new Vector3(0f, 0.105f, -0.145f), black);
        AddCylinder(root, "ScopeRearBell", 0.036f, 0.06f, new Vector3(0f, 0.105f, 0.09f), black);
        AddCylinder(root, "ScopeFrontLens", 0.031f, 0.006f, new Vector3(0f, 0.105f, -0.181f), lens);
        AddCylinder(root, "ScopeRearLens", 0.029f, 0.006f, new Vector3(0f, 0.105f, 0.121f), lens);
        AddBox(root, "ScopeMountFront", new Vector3(0.04f, 0.035f, 0.02f), new Vector3(0f, 0.065f, -0.08f), steel);
        AddBox(root, "ScopeMountRear", new Vector3(0.04f, 0.035f, 0.02f), new Vector3(0f, 0.065f, 0.05f), steel);

        AddCylinder(root, "Bolt", 0.008f, 0.07f, new Vector3(0.06f, 0.03f, 0.06f), steel, -1f, new Vector3(0f, 0f, 90f));
        AddSphere(root, "BoltKnob", 0.014f, new Vector3(0.098f, 0.03f, 0.06f), steel);

        AddBox(root, "Magazine", new Vector3(0.05f, 0.08f, 0.10f), new Vector3(0f, -0.085f, 0.01f), black, new Vector3(5f, 0f, 0f));
        AddBox(root, "Grip", new Vector3(0.048f, 0.13f, 0.07f), new Vector3(0f, -0.10f, 0.10f), olive, new Vector3(-18f, 0f, 0f));
        AddBox(root, "TriggerGuard", new Vector3(0.035f, 0.012f, 0.09f), new Vector3(0f, -0.075f, 0.04f), steel);
        AddBox(root, "Trigger", new Vector3(0.009f, 0.028f, 0.012f), new Vector3(0f, -0.06f, 0.035f), steel, new Vector3(-12f, 0f, 0f));
    }

    private static void BuildDeagle(Node3D root)
    {
        StandardMaterial3D silver = Mat(new Color(0.62f, 0.635f, 0.65f), 0.6f, 0.28f);
        StandardMaterial3D black = Mat(new Color(0.10f, 0.105f, 0.115f), 0.1f, 0.62f);
        StandardMaterial3D gold = Mat(new Color(0.72f, 0.52f, 0.18f), 0.8f, 0.3f);

        AddBox(root, "Slide", new Vector3(0.078f, 0.075f, 0.26f), new Vector3(0f, 0.025f, -0.05f), silver);
        AddBox(root, "Frame", new Vector3(0.07f, 0.055f, 0.22f), new Vector3(0f, -0.03f, -0.01f), black);
        AddCylinder(root, "Muzzle", 0.019f, 0.035f, new Vector3(0f, 0.025f, -0.195f), silver);
        AddBox(root, "Grip", new Vector3(0.054f, 0.15f, 0.075f), new Vector3(0f, -0.12f, 0.085f), black, new Vector3(-20f, 0f, 0f));
        AddBox(root, "TriggerGuard", new Vector3(0.04f, 0.012f, 0.08f), new Vector3(0f, -0.075f, 0.035f), black);
        AddBox(root, "Trigger", new Vector3(0.009f, 0.03f, 0.012f), new Vector3(0f, -0.05f, 0.03f), gold, new Vector3(-12f, 0f, 0f));
        AddBox(root, "RearSight", new Vector3(0.036f, 0.018f, 0.018f), new Vector3(0f, 0.072f, 0.06f), black);
        AddBox(root, "FrontSight", new Vector3(0.01f, 0.018f, 0.012f), new Vector3(0f, 0.072f, -0.17f), black);
        AddBox(root, "Hammer", new Vector3(0.014f, 0.025f, 0.015f), new Vector3(0f, 0.055f, 0.09f), gold, new Vector3(-20f, 0f, 0f));
        for (int i = 0; i < 4; i++)
        {
            AddBox(root, "SlideSerration" + i, new Vector3(0.004f, 0.038f, 0.008f),
                new Vector3(0.041f, 0.025f, 0.015f + i * 0.018f), black);
        }
    }

    private static void BuildKnife(Node3D root)
    {
        StandardMaterial3D blade = Mat(new Color(0.62f, 0.65f, 0.68f), 0.8f, 0.25f);
        StandardMaterial3D grip = Mat(new Color(0.10f, 0.08f, 0.06f), 0.05f, 0.75f);
        StandardMaterial3D wrap = Mat(new Color(0.28f, 0.19f, 0.11f), 0.0f, 0.8f);
        StandardMaterial3D gold = Mat(new Color(0.72f, 0.52f, 0.18f), 0.8f, 0.3f);

        AddBlade(root, "Blade", blade);
        AddBox(root, "Guard", new Vector3(0.075f, 0.014f, 0.025f), new Vector3(0f, 0f, 0.012f), gold);
        AddCylinder(root, "Handle", 0.022f, 0.15f, new Vector3(0f, 0f, 0.095f), grip);
        AddCylinder(root, "Pommel", 0.026f, 0.025f, new Vector3(0f, 0f, 0.175f), gold);
        for (int i = 0; i < 3; i++)
        {
            AddCylinder(root, "Wrap" + i, 0.024f, 0.012f,
                new Vector3(0f, 0f, 0.045f + i * 0.035f), wrap);
        }

        AddSphere(root, "Pivot", 0.009f, new Vector3(0f, 0f, 0.03f), blade);
    }

    private static void BuildFallback(Node3D root)
    {
        StandardMaterial3D mat = Mat(new Color(0.16f, 0.16f, 0.17f), 0.3f, 0.55f);
        AddBox(root, "Body", new Vector3(0.09f, 0.14f, 0.7f), Vector3.Zero, mat);
        AddBox(root, "Grip", new Vector3(0.07f, 0.16f, 0.12f), new Vector3(0f, -0.1f, 0.2f), mat);
    }

    // -- Shared building blocks -------------------------------------------

    private static void AddFillLight(Node3D root)
    {
        var light = new OmniLight3D
        {
            Name = "ViewmodelFill",
            Position = new Vector3(0.18f, 0.28f, 0.05f),
            LightColor = new Color(1f, 0.93f, 0.82f),
            LightEnergy = 0.9f,
            OmniRange = 2.5f,
            ShadowEnabled = false,
            LightCullMask = ViewmodelLayer,
        };
        root.AddChild(light);

        var rim = new OmniLight3D
        {
            Name = "ViewmodelRim",
            Position = new Vector3(-0.30f, 0.18f, -0.25f),
            LightColor = new Color(0.55f, 0.68f, 0.85f),
            LightEnergy = 0.45f,
            OmniRange = 2.5f,
            ShadowEnabled = false,
            LightCullMask = ViewmodelLayer,
        };
        root.AddChild(rim);
    }

    private static MeshInstance3D AddBox(Node3D parent, string name, Vector3 size,
        Vector3 pos, Material mat, Vector3? rotDeg = null)
    {
        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = mat,
            Position = pos,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = ViewmodelLayer,
        };
        if (rotDeg.HasValue)
            node.RotationDegrees = rotDeg.Value;
        parent.AddChild(node);
        return node;
    }

    private static MeshInstance3D AddCylinder(Node3D parent, string name, float radius,
        float length, Vector3 pos, Material mat, float topRadius = -1f, Vector3? rotDeg = null)
    {
        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = new CylinderMesh
            {
                TopRadius = topRadius < 0f ? radius : topRadius,
                BottomRadius = radius,
                Height = length,
                RadialSegments = 12,
                Rings = 1,
            },
            MaterialOverride = mat,
            Position = pos,
            RotationDegrees = rotDeg ?? new Vector3(90f, 0f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = ViewmodelLayer,
        };
        parent.AddChild(node);
        return node;
    }

    private static MeshInstance3D AddSphere(Node3D parent, string name, float radius,
        Vector3 pos, Material mat)
    {
        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = new SphereMesh
            {
                Radius = radius,
                Height = radius * 2f,
                RadialSegments = 12,
                Rings = 6,
            },
            MaterialOverride = mat,
            Position = pos,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = ViewmodelLayer,
        };
        parent.AddChild(node);
        return node;
    }

    /// <summary>
    /// Extruded low-poly blade: a 2D profile in the Y/Z plane, 12 mm thick.
    /// Explicit per-face normals keep the flats readable under the fill light.
    /// </summary>
    private static void AddBlade(Node3D parent, string name, Material mat)
    {
        // (z, y) profile, base at z=0 and tip at z=-0.29.
        Vector2[] profile =
        {
            new(0f, 0.028f),
            new(-0.20f, 0.034f),
            new(-0.29f, 0f),
            new(-0.22f, -0.022f),
            new(-0.06f, -0.034f),
            new(0f, -0.022f),
        };
        const float halfThickness = 0.006f;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? halfThickness : -halfThickness;
            Vector3 normal = new(side == 0 ? 1f : -1f, 0f, 0f);
            for (int i = 1; i < profile.Length - 1; i++)
            {
                AddTriangle(st,
                    new Vector3(x, profile[0].Y, profile[0].X),
                    new Vector3(x, profile[i].Y, profile[i].X),
                    new Vector3(x, profile[i + 1].Y, profile[i + 1].X),
                    normal);
            }
        }

        for (int i = 0; i < profile.Length; i++)
        {
            Vector2 a = profile[i];
            Vector2 b = profile[(i + 1) % profile.Length];
            Vector3 edge = new(0f, b.Y - a.Y, b.X - a.X);
            Vector3 normal = new Vector3(0f, -edge.Z, edge.Y).Normalized();
            AddTriangle(st,
                new Vector3(halfThickness, a.Y, a.X),
                new Vector3(-halfThickness, a.Y, a.X),
                new Vector3(-halfThickness, b.Y, b.X),
                normal);
            AddTriangle(st,
                new Vector3(halfThickness, a.Y, a.X),
                new Vector3(-halfThickness, b.Y, b.X),
                new Vector3(halfThickness, b.Y, b.X),
                normal);
        }

        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = st.Commit(),
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = ViewmodelLayer,
        };
        parent.AddChild(node);
    }

    /// <summary>Adds a triangle with the winding forced to face <paramref name="normal"/>.</summary>
    private static void AddTriangle(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
    {
        Vector3 face = (b - a).Cross(c - a);
        if (face.Dot(normal) < 0f)
            (b, c) = (c, b);
        st.SetNormal(normal);
        st.AddVertex(a);
        st.SetNormal(normal);
        st.AddVertex(b);
        st.SetNormal(normal);
        st.AddVertex(c);
    }

    private static StandardMaterial3D Mat(Color color, float metallic, float roughness,
        Color? emission = null, float emissionEnergy = 1f)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Metallic = metallic,
            Roughness = roughness,
        };
        if (emission.HasValue)
        {
            mat.EmissionEnabled = true;
            mat.Emission = emission.Value;
            mat.EmissionEnergyMultiplier = emissionEnergy;
        }
        return mat;
    }
}
