using System.Numerics;
using Evergine.Common.Graphics;

namespace Longshot.Gameplay.Table;

/// <summary>
/// Visual mesh generator for procedural rails. The collision geometry is owned by
/// <see cref="LongShot.Engine.TableBuilder"/> - we consume the same filleted path so the
/// rendered edge perfectly matches the cushion segments the engine uses.
/// </summary>
public struct JawTips
{
    public Vector2 TopRight;
    public Vector2 BottomRight;
    public Vector2 BottomLeft;
    public Vector2 TopLeft;
}

public static class RailMeshBuilder
{
    public static void BuildRailBetween(
        TableParameters p, Vector2 start, Vector2 end, float width,
        JawSpec startJaw, JawSpec endJaw, int cornerResolution,
        float filletMul, float sinkDepth, Color color,
        List<TronVertex> verts, List<uint> indices,
        out JawTips tips, out Vector3 position, out Vector3 rotation)
    {
        float railHeight = GameSettings.RailHeight;

        Vector2 delta = end - start;
        float length = delta.Length();
        Vector2 dir = Vector2.Normalize(delta);
        // See TableBuilder.BuildRailPhysics for derivation - the previous atan2(dir.Y, dir.X)
        // was 180° off for any rail with non-zero Y direction (i.e. all four side rails).
        float angle = MathF.Atan2(-dir.Y, dir.X);
        Vector2 mid = (start + end) * 0.5f;

        position = new Vector3(mid.X, 0, mid.Y);
        rotation = new Vector3(0, -angle, 0);

        float halfLen = length / 2f;
        float halfWid = width / 2f;

        float safeStartAngle = Math.Clamp(startJaw.AngleDeg, 0.1f, 89.9f);
        float safeEndAngle = Math.Clamp(endJaw.AngleDeg, 0.1f, 89.9f);

        float maxDepth = width * 0.99f;
        float startDepth = MathF.Max(0.001f, MathF.Min(startJaw.Cutout, maxDepth));
        float endDepth = MathF.Max(0.001f, MathF.Min(endJaw.Cutout, maxDepth));

        float rawLeftJawX = startDepth * MathF.Tan(safeStartAngle * MathF.PI / 180f);
        float rawRightJawX = endDepth * MathF.Tan(safeEndAngle * MathF.PI / 180f);

        float maxJawX = p.BallDiameter * 0.5f;
        float leftJawX = MathF.Min(rawLeftJawX, maxJawX);
        float rightJawX = MathF.Min(rawRightJawX, maxJawX);

        // Jaws cut INTO the playfield-facing edge (real-rail profile). See TableBuilder.
        Vector2[] corners =
        {
            new Vector2(-halfLen, -halfWid),                  // 0: Back Left
            new Vector2(halfLen, -halfWid),                   // 1: Back Right
            new Vector2(halfLen, halfWid - endDepth),         // 2: Right Throat
            new Vector2(halfLen - rightJawX, halfWid),        // 3: Right Nose
            new Vector2(-halfLen + leftJawX, halfWid),        // 4: Left Nose
            new Vector2(-halfLen, halfWid - startDepth),      // 5: Left Throat
        };

        tips = new JawTips
        {
            TopLeft = corners[5],
            TopRight = corners[2],
            BottomRight = corners[3],
            BottomLeft = corners[4],
        };

        var path = LongShot.Engine.TableBuilder.BuildFilletedPath(corners, leftJawX, rightJawX, startDepth, endDepth, cornerResolution, filletMul);

        ExtrudeWalls(path, railHeight, sinkDepth, length, width, color, verts, indices);
        CapTop(path, railHeight, halfLen, halfWid, length, width, color, verts, indices);
    }

    private static void ExtrudeWalls(
        List<Vector2> path,
        float railHeight, float sinkDepth,
        float length, float width,
        Color color,
        List<TronVertex> verts,
        List<uint> indices)
    {
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

        for (int i = 1; i < count - 1; i++)
        {
            indices.Add(startIdx);
            indices.Add(startIdx + (uint)i);
            indices.Add(startIdx + (uint)i + 1);
        }
    }
}
