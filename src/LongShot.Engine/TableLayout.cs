using System;
using System.Numerics;

namespace LongShot.Engine;

public class TableLayout
{
    public CushionSegment[] Rails = Array.Empty<CushionSegment>();
    public PocketBeam[] Pockets = Array.Empty<PocketBeam>();
    public Vector3[] JawCorners = Array.Empty<Vector3>();

    public void LoadProceduralData(CushionSegment[] rails, PocketBeam[] pockets)
    {
        Rails = rails;
        Pockets = pockets;
    }
}
