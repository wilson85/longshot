using System.Linq;

namespace Longshot.Gameplay.Table;

public class TronTableDirector : Component
{
    public Prefab RailPrefab { get; set; }
    public Prefab SlatePrefab { get; set; }
    public Prefab GatePrefab { get; set; }
    public Prefab BallPrefab { get; set; }

    // Removed OnActivated() completely. We only build when the Manager gives us data!
    public void BuildVisualTable(TableDefinition data)
    {
        ClearOldTable();
        SpawnSlate(data.Width, data.Length);

        foreach (var rail in data.Rails)
        {
            SpawnRail(rail);
        }

        foreach (var pocket in data.Pockets)
        {
            SpawnGate(pocket);
        }
    }

    private void ClearOldTable()
    {
        for (int i = this.Owner.NumChildren - 1; i >= 0; i--)
        {
            this.Owner.RemoveChild(this.Owner.ChildEntities.ElementAt(i));
        }
    }

    private void SpawnRail(RailData data)
    {
        Entity rail = RailPrefab.Instantiate();
        rail.Name = data.Name;

        var gen = rail.FindComponent<ProceduralRail>();
        if (gen == null)
        {
            gen = new ProceduralRail();
            rail.AddComponent(gen);
        }

        gen.Start = data.Start;
        gen.End = data.End;
        gen.StartJaw = data.StartJaw;
        gen.EndJaw = data.EndJaw;
        gen.Parameters.RailWidth = GameSettings.RailWidth;

        this.Owner.AddChild(rail);
        gen.RebuildRail();

        // Align the rail vertically using the exact RailSlateOffset from GameSettings.
        var t3d = rail.FindComponent<Transform3D>();
        if (t3d != null)
        {
            t3d.LocalPosition = new Evergine.Mathematics.Vector3(t3d.LocalPosition.X, GameSettings.RailSlateOffset, t3d.LocalPosition.Z);
        }
    }

    private void SpawnGate(PocketData data)
    {
        float baseGateLength = System.Numerics.Vector3.Distance(data.P1, data.P2);
        System.Numerics.Vector3 baseCenter = (data.P1 + data.P2) / 2f;
        float capsuleRadius = GameSettings.BallRadius;

        // RESTORED OFFSET: Pushes the gate slightly back into the pocket throat
        float depthOffset = 0.06f; 
        
        // Align the gate vertically using the exact GateSlateOffset from GameSettings.
        float heightOffset = GameSettings.GateSlateOffset; 
        
        System.Numerics.Vector3 pullDirNorm = System.Numerics.Vector3.Normalize(data.PullDir);
        System.Numerics.Vector3 totalOffset = (pullDirNorm * depthOffset) + new System.Numerics.Vector3(0, heightOffset, 0);
        System.Numerics.Vector3 finalCenterPos = baseCenter + totalOffset;

        // EXPAND THE GATE TO FIT THE THROAT
        // Because the jaws sweep outwards, the gap is wider inside the throat than at the nose.
        bool isSidePocket = data.Name.Contains("Mid");
        float sweepAngle = isSidePocket ? GameSettings.RailSideSweep : GameSettings.RailCornerSweep;
        
        // Basic trig: width increases by depth * tan(angle) on both sides
        float expansion = 2f * (depthOffset * (float)System.Math.Tan(sweepAngle * System.Math.PI / 180f));
        
        // For corner pockets, the pull direction is diagonal relative to the rails, 
        // so the effective width expands faster across the flat face of the gate.
        if (!isSidePocket) 
        {
             expansion *= 1.414f; // Sqrt(2) approximation ensures it fully beds into the corner walls
        }

        float finalGateLength = baseGateLength + expansion;

        Entity gate = GatePrefab.Instantiate();
        LaserPocketGate gateComponent = gate.FindComponent<LaserPocketGate>();
        if (gateComponent != null)
        {
            gateComponent.Radius = capsuleRadius;
            gateComponent.Length = finalGateLength;
        }
        gate.Name = data.Name + "_Visual";

        // CALCULATE YAW: Get the direction vector from P1 to P2
        System.Numerics.Vector3 gateDirection = System.Numerics.Vector3.Normalize(data.P2 - data.P1);
        
        // Atan2(X, Z) calculates the rotation angle around the Y-axis (Yaw)
        // We add PiOver2 (90 degrees) to offset the base orientation of the Capsule mesh
        float yawAngle = (float)System.Math.Atan2(gateDirection.X, gateDirection.Z) + Evergine.Mathematics.MathHelper.PiOver2;

        var t3d = gate.FindComponent<Transform3D>();
        if (t3d == null)
        {
            t3d = new Transform3D();
            gate.AddComponent(t3d);
        }
        
        // Apply position and rotation
        t3d.LocalPosition = new Evergine.Mathematics.Vector3(finalCenterPos.X, finalCenterPos.Y, finalCenterPos.Z);
        
        // Pitch 90 degrees (PiOver2) to lay it flat, and apply our calculated Yaw!
        t3d.LocalRotation = new Evergine.Mathematics.Vector3(Evergine.Mathematics.MathHelper.PiOver2, yawAngle, 0);

        this.Owner.AddChild(gate);
    }

    private void SpawnSlate(float width, float length)
    {
        Entity slate = SlatePrefab.Instantiate();
        var transform = slate.FindComponent<Transform3D>();

        // Slate sits at 0.002f so objects with a bottom at 0 are slightly nestled into the felt
        transform.Position = new Evergine.Mathematics.Vector3(0, 0.002f, 0);

        float fullWidth = width + (GameSettings.RailWidth * 2) + 100;
        float fullLength = length + (GameSettings.RailWidth * 2) + 100;

        transform.Scale = new Evergine.Mathematics.Vector3(fullWidth, 1f, fullLength);

        this.Owner.AddChild(slate);
    }
}