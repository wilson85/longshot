using System.Numerics;
using LongShot.Rendering;
using LongShot.Table;

namespace LongShot.Engine;

/// <summary>
/// Pure, stateless mathematical functions for Continuous Collision Detection (CCD).
/// </summary>
public static class CollisionDetection
{
    public static float CalculateBallSegmentImpactTime(in BallState ball, in CushionSegment segment)
    {
        float velocityTowardsNormal = Vector3.Dot(ball.LinearVelocity, segment.Normal);
        if (velocityTowardsNormal >= 0)
        {
            return float.PositiveInfinity;
        }

        float distToLine = Vector3.Dot(ball.Position - segment.Start, segment.Normal);
        if (distToLine <= BilliardsConstants.BallRadius)
        {
            Vector3 cp = ball.Position - (segment.Normal * distToLine);
            Vector3 eDir = segment.End - segment.Start;
            float d = Vector3.Dot(cp - segment.Start, eDir);
            if (d >= 0 && d <= eDir.LengthSquared())
            {
                return 0f;
            }
        }

        float timeToLine = (distToLine - BilliardsConstants.BallRadius) / -velocityTowardsNormal;
        if (timeToLine < 0)
        {
            return float.PositiveInfinity;
        }

        Vector3 impactPos = ball.Position + (ball.LinearVelocity * timeToLine);
        Vector3 contactPoint = impactPos - (segment.Normal * BilliardsConstants.BallRadius);

        Vector3 edgeDir = segment.End - segment.Start;
        float dot = Vector3.Dot(contactPoint - segment.Start, edgeDir);

        if (dot >= 0 && dot <= edgeDir.LengthSquared())
        {
            return timeToLine;
        }

        return float.PositiveInfinity;
    }

    public static float CalculateBallPointImpactTime(in BallState ball, Vector3 point)
    {
        Vector3 deltaP = ball.Position - point;
        Vector3 deltaV = ball.LinearVelocity;

        if (Vector3.Dot(deltaP, deltaV) >= 0)
        {
            return float.PositiveInfinity;
        }

        float aQuad = deltaV.LengthSquared();
        float bQuad = 2.0f * Vector3.Dot(deltaP, deltaV);
        float cQuad = deltaP.LengthSquared() - (BilliardsConstants.BallRadius * BilliardsConstants.BallRadius);

        if (cQuad <= 0 && bQuad < 0)
        {
            return 0f;
        }

        float discriminant = (bQuad * bQuad) - (4.0f * aQuad * cQuad);
        if (discriminant < 0)
        {
            return float.PositiveInfinity;
        }

        float t = (-bQuad - MathF.Sqrt(discriminant)) / (2.0f * aQuad);
        return t >= 0 ? t : float.PositiveInfinity;
    }

    public static float CalculatePocketCrossTime(in BallState ball, in PocketBeam pocket)
    {
        float vNorm = Vector3.Dot(ball.LinearVelocity, pocket.Normal);
        if (vNorm >= 0)
        {
            return float.PositiveInfinity;
        }

        float distToLine = Vector3.Dot(ball.Position - pocket.P1, pocket.Normal);
        float timeToLine = distToLine / -vNorm;

        if (timeToLine < 0)
        {
            return float.PositiveInfinity;
        }

        Vector3 impactPos = ball.Position + (ball.LinearVelocity * timeToLine);
        Vector3 edgeDir = pocket.P2 - pocket.P1;
        float dot = Vector3.Dot(impactPos - pocket.P1, edgeDir);

        if (dot >= 0 && dot <= edgeDir.LengthSquared())
        {
            return timeToLine;
        }

        return float.PositiveInfinity;
    }

    public static float CalculateBallBallImpactTime(in BallState a, in BallState b)
    {
        Vector3 deltaP = a.Position - b.Position;
        Vector3 deltaV = a.LinearVelocity - b.LinearVelocity;

        float aQuad = deltaV.LengthSquared();
        float bQuad = 2.0f * Vector3.Dot(deltaP, deltaV);
        float cQuad = deltaP.LengthSquared() - (4.0f * BilliardsConstants.BallRadius * BilliardsConstants.BallRadius);

        if (cQuad < 0 && bQuad < 0)
        {
            return 0f;
        }

        if (bQuad >= 0)
        {
            return float.PositiveInfinity;
        }

        float discriminant = (bQuad * bQuad) - (4.0f * aQuad * cQuad);
        if (discriminant < 0)
        {
            return float.PositiveInfinity;
        }

        float t = (-bQuad - MathF.Sqrt(discriminant)) / (2.0f * aQuad);
        return t >= 0 ? t : float.PositiveInfinity;
    }
}