using System.Diagnostics;
using System.Numerics;
using System.Runtime.Serialization;
using Evergine.Common.Graphics;
using Evergine.Components.Graphics3D;

namespace Longshot.Gameplay.Table;

[Serializable]
public class TableParameters
{
    public float BallDiameter = 0.05715f; // Standard WPA pool ball (57.15mm)
    public float IslandWidth = 0.09f;
}

public class ProceduralRail : Component
{
    [BindComponent]
    public MeshComponent MeshComponent;

    [IgnoreDataMember]
    public TableParameters Parameters = new TableParameters();

    public Vector2 Start { get; set; }
    public Vector2 End { get; set; }
    public JawSpec StartJaw { get; set; }
    public JawSpec EndJaw { get; set; }

    [IgnoreDataMember]
    public int CornerResolution { get; set; } = 8;

    [IgnoreDataMember]
    public float CornerFilletMultiplier { get; set; } = 0.25f;

    [IgnoreDataMember]
    public Color RailColor { get; set; } = new Color(64, 159, 214, 250);

    [IgnoreDataMember]
    public float CornerSmoothness { get; set; } = 0.25f;

    [IgnoreDataMember]
    public float SinkDepth { get; set; } = -0.02f;


    private Mesh _currentMesh;

    protected override void OnActivated()
    {
        RebuildRail();
    }

    public IReadOnlyList<LineSegment> LocalCollisionSegments { get; private set; }

    public void RebuildRail()
    {
        var verts = new List<TronVertex>();
        var indices = new List<uint>();

        RailMeshBuilder.BuildRailBetween(
            Parameters,
            Start,
            End,
            Parameters.IslandWidth,
            StartJaw,
            EndJaw,
            CornerResolution,
            CornerFilletMultiplier,
            SinkDepth,
            RailColor,
            verts,
            indices,
            out var localPhysics,
            out var tips,
            out var pos,
            out var rot);

        var tr = this.Owner.FindComponent<Transform3D>();
        if (tr != null)
        {
            tr.LocalPosition = pos.ToEvergine();
            tr.LocalRotation = rot.ToEvergine();
        }

        LocalCollisionSegments = localPhysics;

        UpdateEvergineMesh(verts.ToArray(), indices.ToArray());

        Debug.Print($"[RAIL BUILDER] Generated {verts.Count} vertices and {indices.Count} indices for {this.Owner?.Name}");
    }

    private void UpdateEvergineMesh(TronVertex[] vertexData, uint[] indexData)
    {
        if (vertexData.Length == 0 || indexData.Length == 0)
        {
            return;
        }

        // Force component creation if binding fails
        if (MeshComponent == null)
        {
            MeshComponent = this.Owner.FindComponent<MeshComponent>();
            if (MeshComponent == null)
            {
                MeshComponent = new MeshComponent();
                this.Owner.AddComponent(MeshComponent);
            }
        }

        if (this.Owner.FindComponent<MeshRenderer>() == null)
        {
            this.Owner.AddComponent(new MeshRenderer());
        }

        var matComp = this.Owner.FindComponent<MaterialComponent>();
        if (matComp == null)
        {
            matComp = new MaterialComponent();
            this.Owner.AddComponent(matComp);
        }

        var graphicsContext = Application.Current.Container.Resolve<GraphicsContext>();

        if (_currentMesh != null)
        {
            foreach (var vb in _currentMesh.VertexBuffers)
            {
                vb.Buffer?.Dispose();
            }

            _currentMesh.IndexBuffer?.Buffer?.Dispose();
        }

        var vDesc = new BufferDescription((uint)(vertexData.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<TronVertex>()), BufferFlags.VertexBuffer, ResourceUsage.Default);
        var vBuffer = graphicsContext.Factory.CreateBuffer(vertexData, ref vDesc);

        var iDesc = new BufferDescription((uint)(indexData.Length * sizeof(uint)), BufferFlags.IndexBuffer, ResourceUsage.Default);
        var iBuffer = graphicsContext.Factory.CreateBuffer(indexData, ref iDesc);

        var min = vertexData[0].Position;
        var max = vertexData[0].Position;
        foreach (var v in vertexData)
        {
            min = System.Numerics.Vector3.Min(min, v.Position);
            max = System.Numerics.Vector3.Max(max, v.Position);
        }

        _currentMesh = new Mesh(
            new VertexBuffer[] { new VertexBuffer(vBuffer, TronVertex.Layout) },
            new IndexBuffer(iBuffer, IndexFormat.UInt32),
            PrimitiveTopology.TriangleList)
        { 
            BoundingBox = new Evergine.Mathematics.BoundingBox(min.ToEvergine(), max.ToEvergine()) 
        };

        MeshComponent.Model = new Model("ProceduralRail", _currentMesh);
    }

    protected override void OnDeactivated()
    {
        base.OnDeactivated();
        if (_currentMesh != null)
        {
            foreach (var vb in _currentMesh.VertexBuffers)
            {
                vb.Buffer?.Dispose();
            }

            _currentMesh.IndexBuffer?.Buffer?.Dispose();
            _currentMesh = null;
        }
    }
}