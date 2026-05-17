using System;
using System.Collections.Generic;
using SnVector3 = System.Numerics.Vector3;
using SnVector2 = System.Numerics.Vector2;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace LongShot.Engine;

/// <summary>
/// Builds the engine's collision primitives (cushion segments + pocket beams) from a pure-data
/// <see cref="TableDefinition"/>. No external engine dependencies: any host (s&amp;box, console
/// bench, tests) calls this and feeds the output to <see cref="BilliardsEngine.InitializeMatch"/>.
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

    /// <summary>
    /// Returns the closed-loop 2D outline of a single rail in rail-local coordinates (metres),
    /// the rail's world midpoint, and the yaw rotation that maps local +X to the rail's
    /// direction in the engine's Y-up XZ plane. The outline shares geometry with the physics
    /// layer (same call into <see cref="BuildRailPhysics"/>) so the rendered rail edge always
    /// matches the collision boundary.
    /// <para>
    /// Local frame: <c>x</c> = along-rail (-halfLen .. +halfLen), <c>y</c> = perpendicular
    /// (<c>-halfWid</c> = table boundary side, <c>+halfWid</c> = playfield-facing side with
    /// the chamfered jaw cutouts at each end). CCW when viewed from +Y.
    /// </para>
    /// </summary>
    public static (List<SnVector2> Outline, SnVector3 WorldMid, float YawRadians) BuildRailVisualOutline(RailData rail) =>
        BuildRailVisualOutline(rail, BuildOptions.Default);

    /// <inheritdoc cref="BuildRailVisualOutline(RailData)"/>
    public static (List<SnVector2> Outline, SnVector3 WorldMid, float YawRadians) BuildRailVisualOutline(RailData rail, BuildOptions opts)
    {
        BuildRailPhysics(
            rail.Start, rail.End, GameSettings.RailWidth,
            rail.StartJaw, rail.EndJaw,
            opts.CornerResolution, opts.FilletMultiplier, opts.BallDiameter,
            out var segments, out var position, out var angle);

        // Each segment's Start is a path point; the loop closes via segments[last].End == segments[0].Start.
        var outline = new List<SnVector2>(segments.Count);
        foreach (var seg in segments)
        {
            outline.Add(seg.Start);
        }
        return (outline, position, angle);
    }

    public static (CushionSegment[] Rails, PocketBeam[] Pockets) Build(TableDefinition def, BuildOptions opts)
    {
        var rails = new List<CushionSegment>();
        var pockets = new List<PocketBeam>();

        foreach (var pocket in def.Pockets)
        {
            float halfLen = SnVector3.Distance(pocket.P1, pocket.P2) / 2f;
            SnVector3 center = (pocket.P1 + pocket.P2) / 2f;
            SnVector3 pullDir = SnVector3.Normalize(pocket.PullDir);

            SnVector3 offsetCenter = center + (pullDir * opts.PocketCommitmentDepth);
            SnVector3 forward = SnVector3.Normalize(pocket.P2 - pocket.P1);

            SnVector3 p1 = offsetCenter - (forward * halfLen);
            SnVector3 p2 = offsetCenter + (forward * halfLen);

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
                SnVector3 localStart = new SnVector3(seg.Start.X, 0, seg.Start.Y);
                SnVector3 localEnd = new SnVector3(seg.End.X, 0, seg.End.Y);
                SnVector3 localNormal = new SnVector3(seg.Normal.X, 0, seg.Normal.Y);

                SnVector3 worldStart = SnVector3.Transform(localStart, matrix);
                SnVector3 worldEnd = SnVector3.Transform(localEnd, matrix);
                SnVector3 worldNormal = SnVector3.TransformNormal(localNormal, matrix);

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
        SnVector2 start, SnVector2 end, float width,
        JawSpec startJaw, JawSpec endJaw,
        int cornerResolution, float filletMul, float ballDiameter,
        out List<LineSegment> physics,
        out SnVector3 position, out float rotationY)
    {
        SnVector2 delta = end - start;
        float length = delta.Length();
        SnVector2 dir = SnVector2.Normalize(delta);

        // Rotation that maps local +X (rail length direction) to world (dir.X, 0, dir.Y).
        // For CreateRotationY(theta), the local +X axis goes to (cos θ, 0, -sin θ), so we need
        // cos θ = dir.X and -sin θ = dir.Y. Therefore theta = atan2(-dir.Y, dir.X). The naive
        // atan2(dir.Y, dir.X) is 180° off for rails whose direction has a non-zero Y component,
        // which is why the side rails were rendering with the playfield-facing edge on the
        // outside of the table instead of facing inward.
        float angle = MathF.Atan2(-dir.Y, dir.X);
        SnVector2 mid = (start + end) * 0.5f;

        position = new SnVector3(mid.X, 0, mid.Y);
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
        SnVector2[] corners =
        {
            new SnVector2(-halfLen, -halfWid),                             // 0: Back Left  (table boundary)
            new SnVector2(halfLen, -halfWid),                              // 1: Back Right (table boundary)
            new SnVector2(halfLen, halfWid - endDepth),                    // 2: Right Throat (rail end, partway toward playfield)
            new SnVector2(halfLen - rightJawX, halfWid),                   // 3: Right Nose (playfield-edge end)
            new SnVector2(-halfLen + leftJawX, halfWid),                   // 4: Left Nose  (playfield-edge start)
            new SnVector2(-halfLen, halfWid - startDepth),                 // 5: Left Throat
        };

        List<SnVector2> path = BuildFilletedPath(corners, leftJawX, rightJawX, startDepth, endDepth, cornerResolution, filletMul);
        physics = ExtractSegments(path);
    }

    private static List<LineSegment> ExtractSegments(List<SnVector2> path)
    {
        var segments = new List<LineSegment>(path.Count);
        int count = path.Count;

        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            SnVector2 edge = path[next] - path[i];
            if (edge.Length() < 0.0001f)
            {
                continue;
            }

            SnVector2 normal = SnVector2.Normalize(new SnVector2(edge.Y, -edge.X));
            segments.Add(new LineSegment { Start = path[i], End = path[next], Normal = normal });
        }

        return segments;
    }

    /// <summary>
    /// Shared geometric authority for the rail outline path. Both the physics extractor (above)
    /// and the visual mesh extruder consume this same point list, so the rendered rail edge
    /// always matches the collision boundary.
    /// </summary>
    public static List<SnVector2> BuildFilletedPath(SnVector2[] corners, float leftJawX, float rightJawX, float startDepth, float endDepth, int cornerRes, float filletMul)
    {
        var path = new List<SnVector2>();
        int n = corners.Length;
        int arcSteps = Math.Max(2, cornerRes);

        for (int i = 0; i < n; i++)
        {
            SnVector2 prev = corners[(i + n - 1) % n];
            SnVector2 curr = corners[i];
            SnVector2 next = corners[(i + 1) % n];

            // Back corners (at y = -halfWid, i.e. y < 0 in our convention) sit at the table
            // boundary and meet the adjacent rail. They should stay sharp - filleting them
            // produces the bevel-on-the-wrong-corner artefact. Only fillet playfield-side
            // corners (y >= 0), which are the actual jaw/throat transitions.
            if (curr.Y < 0)
            {
                path.Add(curr);
                continue;
            }

            SnVector2 dIn = SnVector2.Normalize(curr - prev);
            SnVector2 dOut = SnVector2.Normalize(next - curr);

            bool isEndSide = curr.X > 0;

            float cx = isEndSide ? rightJawX : leftJawX;
            float cy = isEndSide ? endDepth : startDepth;
            float jawLen = MathF.Sqrt((cx * cx) + (cy * cy));

            float radius = jawLen * filletMul;

            float dot = Math.Clamp(SnVector2.Dot(dIn, dOut), -1f, 1f);
            float angle = MathF.Acos(dot);

            if (angle < 0.001f)
            {
                path.Add(curr);
                continue;
            }

            float tangentDist = radius * MathF.Tan(angle / 2f);

            float lenPrev = SnVector2.Distance(curr, prev);
            float lenNext = SnVector2.Distance(next, curr);
            float maxTangent = Math.Min(lenPrev, lenNext) * 0.49f;

            if (tangentDist > maxTangent)
            {
                tangentDist = maxTangent;
                radius = tangentDist / MathF.Tan(angle / 2f);
            }

            SnVector2 arcStart = curr - (dIn * tangentDist);
            SnVector2 arcEnd = curr + (dOut * tangentDist);

            SnVector2 bisector = SnVector2.Normalize(dOut - dIn);
            float centerDist = radius / MathF.Sin(angle / 2f);
            SnVector2 center = curr + (bisector * centerDist);

            AddArc(path, center, arcStart, arcEnd, arcSteps);
        }

        return path;
    }

    private static void AddArc(List<SnVector2> pts, SnVector2 center, SnVector2 start, SnVector2 end, int steps)
    {
        float radius = SnVector2.Distance(start, center);
        float angleStart = MathF.Atan2(start.Y - center.Y, start.X - center.X);
        float angleEnd = MathF.Atan2(end.Y - center.Y, end.X - center.X);

        float diff = angleEnd - angleStart;
        while (diff < -MathF.PI) diff += MathF.PI * 2f;
        while (diff > MathF.PI) diff -= MathF.PI * 2f;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float ang = angleStart + (diff * t);
            var pt = new SnVector2(
                center.X + (MathF.Cos(ang) * radius),
                center.Y + (MathF.Sin(ang) * radius));

            if (pts.Count > 0 && SnVector2.Distance(pts[^1], pt) < 0.0001f) continue;
            pts.Add(pt);
        }
    }
}
