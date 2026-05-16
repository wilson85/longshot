using System.Numerics;
using System.Runtime.InteropServices;

namespace LongShot.Engine;

[StructLayout(LayoutKind.Sequential)]
public struct BallState
{
    public Vector3 Position;
    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;
    public MotionState State;
}
