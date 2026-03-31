using Evergine.Common.Graphics;

namespace Longshot.Gameplay.Table;

public class LaserPocketGate : Component
{
    public float Length { get; set; }

    public Color GateColor { get; set; } = new Color(255, 80, 0, 150);

    protected override void OnActivated()
    {
        base.OnActivated();
    }
}