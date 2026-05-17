using SnVector3 = System.Numerics.Vector3;

namespace LongShot.Engine;

public readonly struct PocketBeam
{
    public readonly SnVector3 P1;
    public readonly SnVector3 P2;
    public readonly SnVector3 PullDirection;
    public readonly SnVector3 Normal;
    public readonly float Radius;
    public readonly float Height;

    public PocketBeam(SnVector3 p1, SnVector3 p2, SnVector3 pullDirection, float radius = 0.02f, float height = 0.1f)
    {
        P1 = p1;
        P2 = p2;
        PullDirection = SnVector3.Normalize(pullDirection);
        Normal = -PullDirection;
        Radius = radius;
        Height = height;
    }
}
