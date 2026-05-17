using SnVector3 = System.Numerics.Vector3;

namespace LongShot.Engine;

public readonly struct CushionSegment
{
    public readonly SnVector3 Start;
    public readonly SnVector3 End;
    public readonly SnVector3 Normal;

    public CushionSegment(SnVector3 start, SnVector3 end, SnVector3 normal)
    {
        Start = start;
        End = end;
        Normal = SnVector3.Normalize(normal);
    }
}
