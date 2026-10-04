namespace FrutaCS.UI;

/// <summary>
/// Cross-scene player settings. The menu writes here, the match reads
/// from here (PlayerBody picks up MouseSensitivity on spawn). Plain
/// static: milestone 1 has no save-file system.
/// </summary>
public static class GameSettings
{
    public const float DefaultSensitivity = 0.0022f;
    public const float MinSensitivity = 0.0005f;
    public const float MaxSensitivity = 0.006f;

    public static float MouseSensitivity = DefaultSensitivity;
}
