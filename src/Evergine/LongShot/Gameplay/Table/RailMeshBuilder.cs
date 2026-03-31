using System.Numerics;
using Evergine.Common.Graphics;

namespace Longshot.Gameplay.Table;

public struct JawSpec
{
    public float Cutout;   // inset along the rail direction
    public float AngleDeg; // angle relative to rail normal
}

public struct LineSegment
{
    public Vector2 Start;
    public Vector2 End;
    public Vector2 Normal;
}

public struct JawTips
{
    public Vector2 TopRight;
    public Vector2 BottomRight;
    public Vector2 BottomLeft;
    public Vector2 TopLeft;
}

public interface ICollidableRail
{
    List<CushionSegment> GetTableSpaceSegments();
}

public static class RailMeshBuilder
{
    private static void ExtrudeWalls(
        List<Vector2> path,
        float railHeight, float sinkDepth,
        float length, float width,
        Color color,
        List<TronVertex> verts,
        List<uint> indices,
        out List<LineSegment> physics)
    {
        physics = [];
        int count = path.Count;

        var top = new Vector3[count];
        var bot = new Vector3[count];
        var arcDist = new float[count];
        float totalArc = 0f;

        for (int i = 0; i < count; i++)
        {
            top[i] = new Vector3(path[i].X, railHeight, path[i].Y);
            bot[i] = new Vector3(path[i].X, -sinkDepth, path[i].Y);

            arcDist[i] = totalArc;
            totalArc += Vector2.Distance(path[i], path[(i + 1) % count]);
        }

        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            Vector2 edge = path[next] - path[i];
            if (edge.Length() < 0.0001f)
            {
                continue;
            }

            Vector2 normal2D = Vector2.Normalize(new Vector2(edge.Y, -edge.X));
            Vector3 normal3D = new Vector3(normal2D.X, 0, normal2D.Y);
            Vector3 tangent3D = Vector3.Normalize(top[next] - top[i]);
            Vector4 tangent4D = new Vector4(tangent3D.X, tangent3D.Y, tangent3D.Z, -1.0f);

            physics.Add(new LineSegment { Start = path[i], End = path[next], Normal = normal2D });

            float uA = arcDist[i] / totalArc;
            float uB = (i == count - 1) ? 1.0f : arcDist[next] / totalArc;

            uint v0 = (uint)verts.Count;
            verts.Add(new TronVertex(top[i], normal3D, tangent4D, color, new Vector2(uA, 0)));
            verts.Add(new TronVertex(top[next], normal3D, tangent4D, color, new Vector2(uB, 0)));
            verts.Add(new TronVertex(bot[next], normal3D, tangent4D, color, new Vector2(uB, 1)));
            verts.Add(new TronVertex(bot[i], normal3D, tangent4D, color, new Vector2(uA, 1)));

            AddQuadCCW(indices, v0, v0 + 1, v0 + 2, v0 + 3);
        }
    }

    public static void AddQuadCCW(List<uint> indices, uint topLeft, uint topRight, uint bottomRight, uint bottomLeft)
    {
        indices.Add(topLeft); indices.Add(bottomRight); indices.Add(topRight);
        indices.Add(topLeft); indices.Add(bottomLeft); indices.Add(bottomRight);
    }

    public static void BuildRailBetween(
           TableParameters p, Vector2 start, Vector2 end, float width,
           JawSpec startJaw, JawSpec endJaw, int cornerResolution,
           float filletMul, float sinkDepth, Color color,
           List<TronVertex> verts, List<uint> indices,
           out List<LineSegment> physics, out JawTips tips,
           out Vector3 position, out Vector3 rotation)
    {
        float railHeight = p.BallDiameter * 1.2f;

        Vector2 delta = end - start;
        float length = delta.Length();
        Vector2 dir = Vector2.Normalize(delta);
        float angle = MathF.Atan2(dir.Y, dir.X);
        Vector2 mid = (start + end) * 0.5f;

        position = new Vector3(mid.X, 0, mid.Y);
        rotation = new Vector3(0, -angle, 0);

        float halfLen = length / 2f;
        float halfWid = width / 2f;

        float safeStartAngle = Evergine.Mathematics.MathHelper.Clamp(startJaw.AngleDeg, 0.1f, 89.9f);
        float safeEndAngle = Evergine.Mathematics.MathHelper.Clamp(endJaw.AngleDeg, 0.1f, 89.9f);

        // Limit the jaw depth to the width of the rail to keep proportions sane
        float maxDepth = width * 0.99f;
        float startDepth = MathF.Max(0.001f, MathF.Min(startJaw.Cutout, maxDepth));
        float endDepth = MathF.Max(0.001f, MathF.Min(endJaw.Cutout, maxDepth));

        // Calculate the raw X-axis extension of the jaw using the angle
        float rawLeftJawX = startDepth * MathF.Tan(safeStartAngle * MathF.PI / 180f);
        float rawRightJawX = endDepth * MathF.Tan(safeEndAngle * MathF.PI / 180f);

        // BULLETPROOF CLAMP: A jaw can NEVER extend into the pocket more than half a ball diameter.
        // This guarantees the rails can NEVER overlap or swallow the pockets, and a ball will ALWAYS fit!
        float maxJawX = p.BallDiameter * 0.5f;
        float leftJawX = MathF.Min(rawLeftJawX, maxJawX);
        float rightJawX = MathF.Min(rawRightJawX, maxJawX);

        // --- THE PERFECT 6-POINT CONVEX POLYGON ---
        // RESTORED TO CONVEX MATH: The back corners MUST be wider (-leftJawX, +rightJawX) 
        // to form a perfect convex trapezoid. This guarantees the arc math never inverts!
        Vector2[] corners =
        {
            new Vector2(-halfLen - leftJawX, -halfWid),                  // 0: Outer Left Back
            new Vector2(halfLen + rightJawX, -halfWid),                  // 1: Outer Right Back
            new Vector2(halfLen + rightJawX, halfWid - endDepth),        // 2: Right Throat Corner 
            new Vector2(halfLen, halfWid),                               // 3: Right Nose (Matches Laser Boundary)
            new Vector2(-halfLen, halfWid),                              // 4: Left Nose (Matches Laser Boundary)
            new Vector2(-halfLen - leftJawX, halfWid - startDepth)       // 5: Left Throat Corner 
        };

        tips = new JawTips
        {
            TopLeft = corners[5],
            TopRight = corners[2],
            BottomRight = corners[3],
            BottomLeft = corners[4]
        };

        // Pass the properly clamped depth logic to the fillet builder
        List<Vector2> path = BuildFilletedPath(corners, leftJawX, rightJawX, startDepth, endDepth, cornerResolution, filletMul);

        ExtrudeWalls(path, railHeight, sinkDepth, length, width, color, verts, indices, out physics);
        CapTop(path, railHeight, halfLen, halfWid, length, width, color, verts, indices);
    }

    private static List<Vector2> BuildFilletedPath(Vector2[] corners, float leftJawX, float rightJawX, float startDepth, float endDepth, int cornerRes, float filletMul)
    {
        var path = new List<Vector2>();
        int n = corners.Length;
        int arcSteps = Math.Max(2, cornerRes);

        for (int i = 0; i < n; i++)
        {
            Vector2 prev = corners[(i + n - 1) % n];
            Vector2 curr = corners[i];
            Vector2 next = corners[(i + 1) % n];

            Vector2 dIn = Vector2.Normalize(curr - prev);
            Vector2 dOut = Vector2.Normalize(next - curr);

            // DYNAMIC JAW IDENTIFICATION: Accurately grabs the left or right side data
            bool isEndSide = curr.X > 0;

            float cx = isEndSide ? rightJawX : leftJawX;
            float cy = isEndSide ? endDepth : startDepth;
            float jawLen = MathF.Sqrt((cx * cx) + (cy * cy));

            float radius = jawLen * filletMul;

            float dot = Evergine.Mathematics.MathHelper.Clamp(Vector2.Dot(dIn, dOut), -1f, 1f);
            float angle = MathF.Acos(dot);

            if (angle < 0.001f)
            {
                path.Add(curr);
                continue;
            }

            float tangentDist = radius * MathF.Tan(angle / 2f);

            // SAFETY CLAMP: Keeps fillets from ever self-intersecting
            float lenPrev = Vector2.Distance(curr, prev);
            float lenNext = Vector2.Distance(next, curr);
            float maxTangent = Math.Min(lenPrev, lenNext) * 0.49f;

            if (tangentDist > maxTangent)
            {
                tangentDist = maxTangent;
                radius = tangentDist / MathF.Tan(angle / 2f);
            }

            Vector2 arcStart = curr - (dIn * tangentDist);
            Vector2 arcEnd = curr + (dOut * tangentDist);

            Vector2 bisector = Vector2.Normalize(dOut - dIn);
            float centerDist = radius / MathF.Sin(angle / 2f);
            Vector2 center = curr + (bisector * centerDist);

            AddArc(path, center, arcStart, arcEnd, arcSteps);
        }

        return path;
    }

    private static void CapTop(
        List<Vector2> path, float railHeight, float halfLen, float halfWid,
        float length, float width, Color color, List<TronVertex> verts, List<uint> indices)
    {
        Vector4 tangent = new Vector4(1, 0, 0, -1.0f);
        int count = path.Count;
        if (count < 3) return;

        uint startIdx = (uint)verts.Count;

        for (int i = 0; i < count; i++)
        {
            float u = (path[i].X + halfLen) / length;
            float v = (path[i].Y + halfWid) / width;
            verts.Add(new TronVertex(
                new Vector3(path[i].X, railHeight, path[i].Y),
                Vector3.UnitY, tangent, color, new Vector2(u, v)));
        }

        // Standard CCW top cap winding to keep normals facing UP (+Y)
        for (int i = 1; i < count - 1; i++)
        {
            indices.Add(startIdx);
            indices.Add(startIdx + (uint)i);
            indices.Add(startIdx + (uint)i + 1);
        }
    }

    private static void AddArc(List<Vector2> pts, Vector2 center, Vector2 start, Vector2 end, int steps)
    {
        float radius = Vector2.Distance(start, center);
        float angleStart = MathF.Atan2(start.Y - center.Y, start.X - center.X);
        float angleEnd = MathF.Atan2(end.Y - center.Y, end.X - center.X);

        float diff = angleEnd - angleStart;
        while (diff < -MathF.PI) diff += MathF.PI * 2f;
        while (diff > MathF.PI) diff -= MathF.PI * 2f;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float ang = angleStart + (diff * t);
            var pt = new Vector2(
                center.X + (MathF.Cos(ang) * radius),
                center.Y + (MathF.Sin(ang) * radius));

            if (pts.Count > 0 && Vector2.Distance(pts[^1], pt) < 0.0001f) continue;
            pts.Add(pt);
        }
    }
}