using System.Numerics;

namespace LongShot.Table;

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

public readonly struct PocketBeam
{
    public readonly Vector3 P1;
    public readonly Vector3 P2;
    public readonly Vector3 PullDirection;
    public readonly Vector3 Normal;

    public PocketBeam(Vector3 p1, Vector3 p2, Vector3 pullDirection)
    {
        P1 = p1; P2 = p2; PullDirection = Vector3.Normalize(pullDirection); Normal = -PullDirection;
    }
}

// Represents a single, continuous 3D block (Jaw -> Main Rail -> Jaw)
public struct ContinuousRail
{
    public Vector3 Deep1;
    public Vector3 Mouth1;
    public Vector3 Mouth2;
    public Vector3 Deep2;
    public Vector3 Normal; // Direction pointing INTO the table center
}

public class TableLayout
{
    public CushionSegment[] Rails = Array.Empty<CushionSegment>();
    public Vector3[] JawCorners = Array.Empty<Vector3>();
    public PocketBeam[] Pockets = Array.Empty<PocketBeam>();

    // NEW: The exact polygonal footprint for the 6 monolithic rails
    public ContinuousRail[] RailBlocks = Array.Empty<ContinuousRail>();

    public float CornerPocketWidth { get; set; } = 0.028575f * 2.8f;
    public float SidePocketWidth { get; set; } = 0.028575f * 2.8f;
    public float JawDepth { get; set; } = 0.05f;

    public void BuildBallTronTable(float width, float length)
    {
        float hw = width / 2f;
        float hl = length / 2f;

        float cM = CornerPocketWidth;
        float sM = SidePocketWidth;
        float jawD = JawDepth;

        var blocks = new ContinuousRail[6];

        // 0. Top Rail
        Vector3 m1 = new Vector3(-hw + cM, 0, -hl);
        Vector3 m2 = new Vector3(hw - cM, 0, -hl);
        blocks[0] = new ContinuousRail
        {
            Mouth1 = m1,
            Mouth2 = m2,
            Deep1 = m1 + Vector3.Normalize(new Vector3(-1, 0, -1)) * jawD,
            Deep2 = m2 + Vector3.Normalize(new Vector3(1, 0, -1)) * jawD,
            Normal = new Vector3(0, 0, 1)
        };

        // 1. Bottom Rail
        m1 = new Vector3(-hw + cM, 0, hl);
        m2 = new Vector3(hw - cM, 0, hl);
        blocks[1] = new ContinuousRail
        {
            Mouth1 = m1,
            Mouth2 = m2,
            Deep1 = m1 + Vector3.Normalize(new Vector3(-1, 0, 1)) * jawD,
            Deep2 = m2 + Vector3.Normalize(new Vector3(1, 0, 1)) * jawD,
            Normal = new Vector3(0, 0, -1)
        };

        // 2. Left Top Rail
        m1 = new Vector3(-hw, 0, -hl + cM);
        m2 = new Vector3(-hw, 0, -sM);
        blocks[2] = new ContinuousRail
        {
            Mouth1 = m1,
            Mouth2 = m2,
            Deep1 = m1 + Vector3.Normalize(new Vector3(-1, 0, -1)) * jawD,
            Deep2 = m2 + new Vector3(-1, 0, 0) * jawD,
            Normal = new Vector3(1, 0, 0)
        };

        // 3. Left Bottom Rail
        m1 = new Vector3(-hw, 0, sM);
        m2 = new Vector3(-hw, 0, hl - cM);
        blocks[3] = new ContinuousRail
        {
            Mouth1 = m1,
            Mouth2 = m2,
            Deep1 = m1 + new Vector3(-1, 0, 0) * jawD,
            Deep2 = m2 + Vector3.Normalize(new Vector3(-1, 0, 1)) * jawD,
            Normal = new Vector3(1, 0, 0)
        };

        // 4. Right Top Rail
        m1 = new Vector3(hw, 0, -hl + cM);
        m2 = new Vector3(hw, 0, -sM);
        blocks[4] = new ContinuousRail
        {
            Mouth1 = m1,
            Mouth2 = m2,
            Deep1 = m1 + Vector3.Normalize(new Vector3(1, 0, -1)) * jawD,
            Deep2 = m2 + new Vector3(1, 0, 0) * jawD,
            Normal = new Vector3(-1, 0, 0)
        };

        // 5. Right Bottom Rail
        m1 = new Vector3(hw, 0, sM);
        m2 = new Vector3(hw, 0, hl - cM);
        blocks[5] = new ContinuousRail
        {
            Mouth1 = m1,
            Mouth2 = m2,
            Deep1 = m1 + new Vector3(1, 0, 0) * jawD,
            Deep2 = m2 + Vector3.Normalize(new Vector3(1, 0, 1)) * jawD,
            Normal = new Vector3(-1, 0, 0)
        };

        RailBlocks = blocks;

        // --- Generate Collision Segments from the precise Blocks ---
        var rails = new List<CushionSegment>();
        var corners = new List<Vector3>();

        foreach (var b in blocks)
        {
            Vector3 n1 = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, b.Deep1 - b.Mouth1));
            if (Vector3.Dot(n1, b.Normal) < 0) n1 = -n1;

            Vector3 n2 = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, b.Deep2 - b.Mouth2));
            if (Vector3.Dot(n2, b.Normal) < 0) n2 = -n2;

            rails.Add(new CushionSegment(b.Deep1, b.Mouth1, n1));
            rails.Add(new CushionSegment(b.Mouth1, b.Mouth2, b.Normal));
            rails.Add(new CushionSegment(b.Mouth2, b.Deep2, n2));

            corners.Add(b.Deep1); corners.Add(b.Mouth1);
            corners.Add(b.Mouth2); corners.Add(b.Deep2);
        }

        Rails = rails.ToArray();
        JawCorners = corners.ToArray();

        // --- Mathematically seal the pockets EXACTLY between the Deep Jaw endpoints ---
        var pockets = new List<PocketBeam>();
        pockets.Add(new PocketBeam(blocks[2].Deep1, blocks[0].Deep1, Vector3.Normalize(new Vector3(-1, 0, -1)))); // Top Left
        pockets.Add(new PocketBeam(blocks[0].Deep2, blocks[4].Deep1, Vector3.Normalize(new Vector3(1, 0, -1))));  // Top Right
        pockets.Add(new PocketBeam(blocks[1].Deep1, blocks[3].Deep2, Vector3.Normalize(new Vector3(-1, 0, 1))));  // Bottom Left
        pockets.Add(new PocketBeam(blocks[5].Deep2, blocks[1].Deep2, Vector3.Normalize(new Vector3(1, 0, 1))));   // Bottom Right
        pockets.Add(new PocketBeam(blocks[3].Deep1, blocks[2].Deep2, new Vector3(-1, 0, 0))); // Left Side
        pockets.Add(new PocketBeam(blocks[4].Deep2, blocks[5].Deep1, new Vector3(1, 0, 0)));  // Right Side
        Pockets = pockets.ToArray();
    }
}