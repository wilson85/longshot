using System.Numerics;
using System.Runtime.InteropServices;
using LongShot.Engine;
using Vortice.Direct3D12;

namespace LongShot.Rendering;

public static class TronColors
{
    public static readonly Vector4 NeonRed = new Vector4(3.0f, 0.1f, 0.2f, 1.0f);   // Values > 1.0f create the glow
    public static readonly Vector4 NeonBlue = new Vector4(0.1f, 2.0f, 3.0f, 1.0f);
    public static readonly Vector4 NeonPink = new Vector4(3.0f, 0.0f, 1.5f, 1.0f);
}

[StructLayout(LayoutKind.Sequential)]
public struct BallState
{
    public Vector3 Position;
    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;
    public MotionState State;
}

[StructLayout(LayoutKind.Sequential)]
public struct RenderItem
{
    public Matrix4x4 World;
    public Vector4 Color;

    // Added explicit emission properties for the shader
    public Vector4 EmissionColor;
    public float EmissionIntensity;

    public MeshType Mesh;
    public MaterialType Material;
    public ID3D12Resource? CustomBuffer;
    public int CustomIndexCount;
}

[StructLayout(LayoutKind.Sequential)]
public struct TrailPoint
{
    public Vector3 Position;
    public float Spin;
    public Vector3 Direction;
    public Vector4 Color;
}

[StructLayout(LayoutKind.Sequential)]
public struct ObjectConstants
{
    public Matrix4x4 World;
    public Matrix4x4 ViewProj;
    public Vector4 GlobalColor;
    // Pack MaterialType into the W component of CameraPos for perfect 16-byte alignment
    public Vector4 CameraPosAndMaterial;
}

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Color;
}