using System;
using SnVector3 = System.Numerics.Vector3;

namespace LongShot.Engine;

public class TableLayout
{
    public CushionSegment[] Rails = Array.Empty<CushionSegment>();
    public PocketBeam[] Pockets = Array.Empty<PocketBeam>();
    public SnVector3[] JawCorners = Array.Empty<SnVector3>();

    public void LoadProceduralData(CushionSegment[] rails, PocketBeam[] pockets)
    {
        Rails = rails;
        Pockets = pockets;
    }
}
