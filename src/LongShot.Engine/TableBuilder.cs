using System;
using System.Collections.Generic;
using System.Numerics;

namespace LongShot.Engine;

/// <summary>
/// Builds the engine's collision primitives (cushion segments + pocket beams) from a pure-data
/// <see cref="TableDefinition"/>. No external engine dependencies: any host (Evergine, s&amp;box,
/// console bench, tests) calls this and feeds the output to <see cref="BilliardsEngine.InitializeMatch"/>.
/// </summary>
public static class TableBuilder
{
    /// <summary>Default visual fillet smoothness, exposed for hosts that want to match it for rendering.</summary>
    public const int DefaultCornerResolution = 12;

    /// <summary>Default rail fillet ratio (multiplier of jaw length).</summary>
    public const float DefaultFilletMultiplier = 0.5f;

    /// <summary>How deep past the throat line a ball must commit before the pocket fires (Default: 1 ball-radius).</summary>
    public const float DefaultPocketCommitmentDepth = GameSettings.BallRadius;

    public readonly struct BuildOptions
    {
        public int CornerResolution { get; init; }
        public float FilletMultiplier { get; init; }
        public float BallDiameter { get; init; }
        public float PocketCommitmentDepth { get; init; }
        public float PocketCapsuleRadius { get; init; }

        public static BuildOptions Default => new()
        {
            CornerResolution = DefaultCornerResolution,
            FilletMultiplier = DefaultFilletMultiplier,
            BallDiameter = GameSettings.BallRadius * 2f,
            // Trigger plane sits one ball-radius INSIDE the throat - the ball must commit before pocketing.
            // (combinedRadii = 0 + BallRadius = BallRadius; trigger inwardDepth >= 2R - R = R)
            PocketCommitmentDepth = GameSettings.BallRadius * 2f,
            PocketCapsuleRadius = 0f,
        };
    }

    public static (CushionSegment[] Rails, PocketBeam[] Pockets) Build(TableDefinition def) =>
        Build(def, BuildOptions.Default);

    public static (CushionSegment[] Rails, PocketBeam[] Pockets) Build(TableDefinition def, BuildOptions opts)
    {
        var rails = new List<CushionSegment>();
        var pockets = new List<PocketBeam>();

        foreach (var pocket in def.Pockets)
        {
            float halfLen = Vector3.Distance(pocket.P1, pocket.P2) / 2f;
            Vector3 center = (pocket.P1 + pocket.P2) / 2f;
            Vector3 pullDir = Vector3.Normalize(pocket.PullDir);

            Vector3 offsetCenter = center + (pullDir * opts.PocketCommitmentDepth);
            Vector3 forward = Vector3.Normalize(pocket.P2 - pocket.P1);

            Vector3 p1 = offsetCenter - (forward * halfLen);
            Vector3 p2 = offsetCenter + (forward * halfLen);

            pockets.Add(new PocketBeam(p1, p2, pullDir, opts.PocketCapsuleRadius));
        }

        foreach (var rail in def.Rails)
        {
            BuildRailPhysics(
                rail.Start, rail.End, GameSettings.RailWidth,
                rail.StartJaw, rail.EndJaw,
                opts.CornerResolution, opts.FilletMultiplier, opts.BallDiameter,
                out var localSegments, out var position, out var rotationY);

            // rotationY is the rail's orientation angle around Y. Rotate the local geometry by
            // that angle to align it with the rail's world direction.
            var matrix = Matrix4x4.CreateRotationY(rotationY) * Matrix4x4.CreateTranslation(position.X, 0, position.Z);

            foreach (var seg in localSegments)
            {
                Vector3 localStart = new Vector3(seg.Start.X, 0, seg.Start.Y);
                Vector3 localEnd = new Vector3(seg.End.X, 0, seg.End.Y);
                Vector3 localNormal = new Vector3(seg.Normal.X, 0, seg.Normal.Y);

                Vector3 worldStart = Vector3.Transform(localStart, matrix);
                Vector3 worldEnd = Vector3.Transform(localEnd, matrix);
                Vector3 worldNormal = Vector3.TransformNormal(localNormal, matrix);

                rails.Add(new CushionSegment(worldStart, worldEnd, worldNormal));
            }
        }

        return (rails.ToArray(), pockets.ToArray());
    }

    /// <summary>
    /// Generates the physics-only representation of a single rail. Identical geometry to the
    /// visual mesh (rail_mesh_builder remains the visual authority) - we share the path-fillet
    /// math so collisions line up perfectly with the rendered rail edge.
    /// </summary>
    public static void BuildRailPhysics(
        Vector2 start, Vector2 end, float width,
        JawSpec startJaw, JawSpec endJaw,
        int cornerResolution, float filletMul, float ballDiameter,
        out List<LineSegment> physics,
        out Vector3 position, out float rotationY)
    {
        Vector2 delta = end - start;
        float length = delta.Length();
        Vector2 dir = Vector2.Normalize(delta);

        // Rotation that maps local +X (rail length direction) to world (dir.X, 0, dir.Y).
        // For CreateRotationY(theta), the local +X axis goes to (cos θ, 0, -sin θ), so we need
        // cos θ = dir.X and -sin θ = dir.Y. Therefore theta = atan2(-dir.Y, dir.X). The naive
        // atan2(dir.Y, dir.X) is 180° off for rails whose direction has a non-zero Y component,
        // which is why the side rails were rendering with the playfield-facing edge on the
        // outside of the table instead of facing inward.
        float angle = MathF.Atan2(-dir.Y, dir.X);
        Vector2 mid = (start + end) * 0.5f;

        position = new Vector3(mid.X, 0, mid.Y);
        rotationY = angle;

        float halfLen = length / 2f;
        float halfWid = width / 2f;

        float safeStartAngle = Math.Clamp(startJaw.AngleDeg, 0.1f, 89.9f);
        float safeEndAngle = Math.Clamp(endJaw.AngleDeg, 0.1f, 89.9f);

        // Cap the jaw cutout at rail width so proportions stay sane.
        float maxDepth = width * 0.99f;
        float startDepth = MathF.Max(0.001f, MathF.Min(startJaw.Cutout, maxDepth));
        float endDepth = MathF.Max(0.001f, MathF.Min(endJaw.Cutout, maxDepth));

        float rawLeftJawX = startDepth * MathF.Tan(safeStartAngle * MathF.PI / 180f);
        float rawRightJawX = endDepth * MathF.Tan(safeEndAngle * MathF.PI / 180f);

        // A jaw can never extend into the pocket more than half a ball diameter - guarantees rails
        // don't overlap and pockets always fit a ball.
        float maxJawX = ballDiameter * 0.5f;
        float leftJawX = MathF.Min(rawLeftJawX, maxJawX);
        float rightJawX = MathF.Min(rawRightJawX, maxJawX);

        // Six-point hexagon defining the rail outline. The back edge follows the table
        // boundary in a straight line; the playfield-facing edge has chamfered cutouts
        // (the "jaws") at each end that face into the adjacent pocket. This matches a
        // real rail's profile: long straight back, shorter playfield edge with miters.
        Vector2[] corners =
        {
            new Vector2(-halfLen, -halfWid),                             // 0: Back Left  (table boundary)
            new Vector2(halfLen, -halfWid),                              // 1: Back Right (table boundary)
            new Vector2(halfLen, halfWid - endDepth),                    // 2: Right Throat (rail end, partway toward playfield)
            new Vector2(halfLen - rightJawX, halfWid),                   // 3: Right Nose (playfield-edge end)
            new Vector2(-halfLen + leftJawX, halfWid),                   // 4: Left Nose  (playfield-edge start)
            new Vector2(-halfLen, halfWid - startDepth),                 // 5: Left Throat
        };

        List<Vector2> path = BuildFilletedPath(corners, leftJawX, rightJawX, startDepth, endDepth, cornerResolution, filletMul);
        physics = ExtractSegments(path);
    }

    private static List<LineSegment> ExtractSegments(List<Vector2> path)
    {
        var segments = new List<LineSegment>(path.Count);
        int count = path.Count;

        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            Vector2 edge = path[next] - path[i];
            if (edge.Length() < 0.0001f)
            {
                continue;
            }

            Vector2 normal = Vector2.Normalize(new Vector2(edge.Y, -edge.X));
            segments.Add(new LineSegment { Start = path[i], End = path[next], Normal = normal });
        }

        return segments;
    }

    /// <summary>
    /// Shared geometric authority for the rail outline path. Both the physics extractor (above)
    /// and the visual mesh extruder consume this same point list, so the rendered rail edge
    /// always matches the collision boundary.
    /// </summary>
    public static List<Vector2> BuildFilletedPath(Vector2[] corners, float leftJawX, float rightJawX, float startDepth, float endDepth, int cornerRes, float filletMul)
    {
        var path = new List<Vector2>();
        int n = corners.Length;
        int arcSteps = Math.Max(2, cornerRes);

        for (int i = 0; i < n; i++)
        {
            Vector2 prev = corners[(i + n - 1) % n];
            Vector2 curr = corners[i];
            Vector2 next = corners[(i + 1) % n];

            // Back corners (at y = -halfWid, i.e. y < 0 in our convention) sit at the table
            // boundary and meet the adjacent rail. They should stay sharp - filleting them
            // produces the bevel-on-the-wrong-corner artefact. Only fillet playfield-side
            // corners (y >= 0), which are the actual jaw/throat transitions.
            if (curr.Y < 0)
            {
                path.Add(curr);
                continue;
            }

            Vector2 dIn = Vector2.Normalize(curr - prev);
            Vector2 dOut = Vector2.Normalize(next - curr);

            bool isEndSide = curr.X > 0;

            float cx = isEndSide ? rightJawX : leftJawX;
            float cy = isEndSide ? endDepth : startDepth;
            float jawLen = MathF.Sqrt((cx * cx) + (cy * cy));

            float radius = jawLen * filletMul;

            float dot = Math.Clamp(Vector2.Dot(dIn, dOut), -1f, 1f);
            float angle = MathF.Acos(dot);

            if (angle < 0.001f)
            {
                path.Add(curr);
                continue;
            }

            float tangentDist = radius * MathF.Tan(angle / 2f);

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
