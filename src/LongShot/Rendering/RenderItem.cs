using System.Numerics;

namespace LongShot.Rendering;

public enum MeshType
{
    Cube,
    Sphere, 
    Quad,
    Circle,
    Cylinder
}

public enum MaterialType
{
    Table = 0,
    Cushion = 1,
    Ball = 2,
    Cue = 3,
    Trail = 4,
    HitMark = 5
}

public struct RenderItem
{
    public MeshType Mesh;
    public Matrix4x4 World;
    public Vector4 Color;
    public MaterialType Material;
}
