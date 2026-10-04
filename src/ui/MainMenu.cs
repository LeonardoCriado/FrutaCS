using Godot;

namespace FrutaCS.UI;

/// <summary>
/// Milestone-1 main menu: JUGAR opens fy_pileta, the sensitivity slider
/// feeds <see cref="GameSettings"/> (which PlayerBody reads on spawn),
/// SALIR quits. All art is original flat fruta styling; no third-party
/// assets.
/// </summary>
public partial class MainMenu : Control
{
    private const string MapScenePath = "res://src/maps/fy_pileta.tscn";

    private HSlider _sens;
    private Label _sensValue;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetNode<Button>("%PlayButton").Pressed += OnPlay;
        GetNode<Button>("%QuitButton").Pressed += OnQuit;
        _sens = GetNode<HSlider>("%SensSlider");
        _sensValue = GetNode<Label>("%SensValue");
        _sens.MinValue = GameSettings.MinSensitivity;
        _sens.MaxValue = GameSettings.MaxSensitivity;
        _sens.Step = 0.0001;
        _sens.Value = GameSettings.MouseSensitivity;
        _sens.ValueChanged += OnSensChanged;
        RefreshSensLabel();
    }

    private void OnPlay() => GetTree().ChangeSceneToFile(MapScenePath);

    private void OnQuit() => GetTree().Quit();

    private void OnSensChanged(double value)
    {
        GameSettings.MouseSensitivity = (float)value;
        RefreshSensLabel();
    }

    private void RefreshSensLabel() => _sensValue.Text = GameSettings.MouseSensitivity.ToString("0.0000");
}
