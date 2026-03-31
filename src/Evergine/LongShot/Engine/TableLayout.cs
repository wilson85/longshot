using System;
using System.Numerics;

namespace Longshot.Engine;

public class TableLayout
{
    public CushionSegment[] Rails = Array.Empty<CushionSegment>();
    public PocketBeam[] Pockets = Array.Empty<PocketBeam>();

    // You can keep JawCorners if you still use them for specific point-collision logic
    public Vector3[] JawCorners = Array.Empty<Vector3>();

    public void LoadProceduralData(CushionSegment[] rails, PocketBeam[] pockets)
    {
        Rails = rails;
        Pockets = pockets;
    }
}