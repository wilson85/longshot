using SnVector3 = System.Numerics.Vector3;
using System.Runtime.InteropServices;

namespace LongShot.Engine;

[StructLayout(LayoutKind.Sequential)]
public struct BallState
{
    public SnVector3 Position;
    public SnVector3 LinearVelocity;
    public SnVector3 AngularVelocity;
    public MotionState State;
}
