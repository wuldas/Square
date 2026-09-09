using Godot;
using Square.Hosting;
using Square.Platform.Godot;

namespace Square.Sample.Godot;

public partial class Main : Control
{
    private SquareControl? _squareControl;
    private Label? _godotCount;
    private int _count;

    public override void _Ready()
    {
        var button = GetNode<Button>("GodotButton");
        _godotCount = GetNode<Label>("GodotCount");
        var container = GetNode<Control>("SquareContainer");
        button.Pressed += OnGodotButtonPressed;

        _squareControl = new SquareControl();
        container.AddChild(_squareControl);
        _squareControl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var window = new AppWindow("Square Godot", 800, 520);
        window.Load(new SquarePage());
        _squareControl.Attach(window);
    }

    private void OnGodotButtonPressed()
    {
        _count++;
        if (_godotCount != null) _godotCount.Text = $"Godot count: {_count}";
        if (_squareControl != null) _squareControl.Visible = !_squareControl.Visible;
    }
}
