using Evergine.Components.Graphics3D;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Longshot.Engine;
using Longshot.Utils;

namespace Longshot.Visuals;

public class BallVisualizer : Behavior
{
    [BindComponent]
    private Transform3D transform;

    [BindComponent]
    private SphereMesh sphereMesh;

    public int BallId { get; set; }

    protected override void Update(System.TimeSpan gameTime)
    {

    }

    public void SyncVisuals(in BallState state)
    {
        transform.Position = state.Position.ToEvergine();
        sphereMesh.Diameter = 2.0f * GameSettings.BallRadius;
    }
}