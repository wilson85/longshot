using System.Numerics;

namespace Longshot.Engine;

public readonly struct CushionSegment
{
    public readonly Vector3 Start;
    public readonly Vector3 End;
    public readonly Vector3 Normal;

    public CushionSegment(Vector3 start, Vector3 end, Vector3 normal)
    {
        Start = start; End = end; Normal = Vector3.Normalize(normal);
    }
}
