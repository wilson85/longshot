using System.Numerics;
using System.Runtime.InteropServices;
using LongShot.Engine;

namespace Longshot.Engine;

[StructLayout(LayoutKind.Sequential)]
public struct BallState
{
    public Vector3 Position;
    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;
    public MotionState State;
}

[StructLayout(LayoutKind.Sequential)]
public struct TrailPoint
{
    public Vector3 Position;
    public float Spin;
    public Vector3 Direction;
    public Vector4 Color;
}