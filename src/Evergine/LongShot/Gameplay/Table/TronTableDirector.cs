using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using Evergine.Components.Graphics3D;

namespace Longshot.Gameplay.Table;

public class TronTableDirector : Component
{
    public Prefab RailPrefab { get; set; }
    public Prefab SlatePrefab { get; set; }
    public Prefab GatePrefab { get; set; }
    public Prefab BallPrefab { get; set; }

    // --- TABLE DIMENSIONS 9ft Pro Table ---
    [IgnoreDataMember]
    [RenderProperty]
    public float TableWidth = 1.27f;

    [IgnoreDataMember]
    [RenderProperty]
    public float TableLength = 2.54f;

    [IgnoreDataMember]
    [RenderProperty]
    public float IslandWidth = 0.12f;

    // --- WPA POCKET SPECIFICATIONS ---
    [IgnoreDataMember]
    [RenderProperty]
    public float CornerMouthWidth = 0.1143f; // 4.5 inches

    [IgnoreDataMember]
    [RenderProperty]
    public float SideMouthWidth = 0.127f;   // 5.0 inches

    // Small buffer to ensure the laser beam is visible and doesn't Z-fight with the rail tip
    [IgnoreDataMember]
    [RenderProperty]
    public float JawPadding = 0.02f;

    // --- WPA JAW ANGLES ---
    [IgnoreDataMember]
    [RenderProperty]
    public float CornerSweep = 39f;

    [IgnoreDataMember]
    [RenderProperty]
    public float SideSweep = 15f;

    protected override void OnActivated()
    {
        if (RailPrefab == null)
        {
            return;
        }

        ClearOldTable();
        GenerateTable();
        SpawnPockets();
        SpawnSlate();
    }

    private void ClearOldTable()
    {
        for (int i = this.Owner.NumChildren - 1; i >= 0; i--)
        {
            this.Owner.RemoveChild(this.Owner.ChildEntities.ElementAt(i));
        }
    }

    private void SpawnPockets()
    {
        float w2 = TableWidth / 2f;
        float l2 = TableLength / 2f;

        float cc = (CornerMouthWidth / 1.414f) + (JawPadding / 2f);
        float sc = (SideMouthWidth / 2f) + (JawPadding / 2f);

        // Corner Pockets
        SpawnGate("Gate_BL", new System.Numerics.Vector3(-w2, 0, -l2 + cc), new System.Numerics.Vector3(-w2 + cc, 0, -l2), new System.Numerics.Vector3(-1, 0, -1));
        SpawnGate("Gate_BR", new System.Numerics.Vector3(w2 - cc, 0, -l2), new System.Numerics.Vector3(w2, 0, -l2 + cc), new System.Numerics.Vector3(1, 0, -1));
        SpawnGate("Gate_TL", new System.Numerics.Vector3(-w2 + cc, 0, l2), new System.Numerics.Vector3(-w2, 0, l2 - cc), new System.Numerics.Vector3(-1, 0, 1));
        SpawnGate("Gate_TR", new System.Numerics.Vector3(w2, 0, l2 - cc), new System.Numerics.Vector3(w2 - cc, 0, l2), new System.Numerics.Vector3(1, 0, 1));

        // Side Pockets
        SpawnGate("Gate_MidL", new Vector3(-w2, 0, sc), new System.Numerics.Vector3(-w2, 0, -sc), new System.Numerics.Vector3(-1, 0, 0));
        SpawnGate("Gate_MidR", new System.Numerics.Vector3(w2, 0, -sc), new System.Numerics.Vector3(w2, 0, sc), new System.Numerics.Vector3(1, 0, 0));
    }

    private void GenerateTable()
    {
        float w2 = TableWidth / 2f;
        float l2 = TableLength / 2f;

        float cc = (CornerMouthWidth / 1.414f) + (JawPadding / 2f);
        float sc = (SideMouthWidth / 2f) + (JawPadding / 2f);

        float cornerCut = cc * 0.5f;
        float sideCut = sc * 0.5f;

        // CRITICAL FIX: Offset the rails OUTWARDS by half their width!
        // This ensures the INNER nose of the cushion sits perfectly on the play boundary, 
        // rather than centering the rail on the boundary and hanging off the edge.
        float hw = IslandWidth / 2f;

        // Bottom (Shifted -Z)
        SpawnRail("Bottom",
            new System.Numerics.Vector2(-w2 + cc, -l2 - hw),
            new System.Numerics.Vector2(w2 - cc, -l2 - hw),
            new JawSpec { Cutout = cornerCut, AngleDeg = CornerSweep },
            new JawSpec { Cutout = cornerCut, AngleDeg = CornerSweep });

        // Top (Shifted +Z)
        SpawnRail("Top",
            new System.Numerics.Vector2(w2 - cc, l2 + hw),
            new System.Numerics.Vector2(-w2 + cc, l2 + hw),
            new JawSpec { Cutout = cornerCut, AngleDeg = CornerSweep },
            new JawSpec { Cutout = cornerCut, AngleDeg = CornerSweep });

        // Right (Shifted +X)
        SpawnRail("Right_Bottom",
            new System.Numerics.Vector2(w2 + hw, -l2 + cc),
            new System.Numerics.Vector2(w2 + hw, -sc),
            new JawSpec { Cutout = cornerCut, AngleDeg = CornerSweep },
            new JawSpec { Cutout = sideCut, AngleDeg = SideSweep });

        SpawnRail("Right_Top",
            new System.Numerics.Vector2(w2 + hw, sc),
            new System.Numerics.Vector2(w2 + hw, l2 - cc),
            new JawSpec { Cutout = sideCut, AngleDeg = SideSweep },
            new JawSpec { Cutout = cornerCut, AngleDeg = CornerSweep });

        // Left (Shifted -X)
        SpawnRail("Left_Bottom",
            new System.Numerics.Vector2(-w2 - hw, -sc),
            new System.Numerics.Vector2(-w2 - hw, -l2 + cc),
            new JawSpec { Cutout = sideCut, AngleDeg = SideSweep },
            new JawSpec { Cutout = cornerCut, AngleDeg = CornerSweep });

        SpawnRail("Left_Top",
            new System.Numerics.Vector2(-w2 - hw, l2 - cc),
            new System.Numerics.Vector2(-w2 - hw, sc),
            new JawSpec { Cutout = cornerCut, AngleDeg = CornerSweep },
            new JawSpec { Cutout = sideCut, AngleDeg = SideSweep });
    }

    private void SpawnRail(string name,
        System.Numerics.Vector2 start, System.Numerics.Vector2 end,
        JawSpec startJaw, JawSpec endJaw)
    {
        Entity rail = RailPrefab.Instantiate();
        rail.Name = name;

        var gen = rail.FindComponent<ProceduralRail>();
        if (gen == null)
        {
            gen = new ProceduralRail();
            rail.AddComponent(gen);
        }

        gen.Start = start;
        gen.End = end;
        gen.StartJaw = startJaw;
        gen.EndJaw = endJaw;
        gen.Parameters.IslandWidth = this.IslandWidth;

        this.Owner.AddChild(rail);
        gen.RebuildRail();
    }

    private void SpawnGate(string name, Vector3 p1, Vector3 p2, Vector3 pullDir)
    {
        float gateLength = Vector3.Distance(p1, p2);
        Vector3 baseCenter = (p1 + p2) / 2f;
        float capsuleRadius = 0.02f;

        float depthOffset = 0.06f;
        float heightOffset = 0.03f;
        Vector3 totalOffset = (pullDir * depthOffset) + new Vector3(0, heightOffset, 0);

        Vector3 finalCenterPos = baseCenter + totalOffset;
        Vector3 finalP2 = p2 + totalOffset;

        Entity gatePivot = new Entity(name)
            .AddComponent(new Transform3D() 
            { 
                Position = finalCenterPos.ToEvergine() 
            });

        gatePivot.FindComponent<Transform3D>().LookAt(finalP2.ToEvergine());

        Entity gateVisual = GatePrefab.Instantiate();
        gateVisual.Name = name + "_Visual";

        var cm = gateVisual.FindComponent<CapsuleMesh>();
        if (cm != null)
        {
            cm.Radius = capsuleRadius;
            cm.Height = gateLength;
        }
        else
        {
            gateVisual.AddComponent(new CapsuleMesh()
            {
                Radius = capsuleRadius,
                Height = gateLength
            });
        }

        var t3d = gateVisual.FindComponent<Transform3D>();
        if (t3d != null)
        {
            t3d.LocalRotation = new Evergine.Mathematics.Vector3(Evergine.Mathematics.MathHelper.PiOver2, 0, 0);
        }
        else
        {
            t3d = new Transform3D()
            {
                // Rotate 90 degrees on the X-axis so the Y-up capsule lays flat along the Z-axis
                LocalRotation = new Evergine.Mathematics.Vector3(Evergine.Mathematics.MathHelper.PiOver2, 0, 0)
            };
            gateVisual.AddComponent(t3d);
        }

        gatePivot.AddChild(gateVisual);

        this.Owner.AddChild(gatePivot);
    }

    private void SpawnSlate()
    {
        Entity slate = SlatePrefab.Instantiate();
        var transform = slate.FindComponent<Transform3D>();

        // this is so the balls don't look like they are floating above the table surface
        transform.Position = new Evergine.Mathematics.Vector3(0, 0.002f, 0);

        float fullWidth = TableWidth + (IslandWidth * 2) +100;
        float fullLength = TableLength + (IslandWidth * 2) + 100;

        transform.Scale = new Evergine.Mathematics.Vector3(fullWidth, 1f, fullLength);

        this.Owner.AddChild(slate);
    }
}