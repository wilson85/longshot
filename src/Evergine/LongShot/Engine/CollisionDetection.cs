using System;
using System.Numerics;
using Longshot.Engine;

namespace LongShot.Engine;

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
        if (distToLine <= GameSettings.BallRadius)
        {
            Vector3 cp = ball.Position - (segment.Normal * distToLine);
            Vector3 eDir = segment.End - segment.Start;
            float d = Vector3.Dot(cp - segment.Start, eDir);
            if (d >= 0 && d <= eDir.LengthSquared())
            {
                return 0f;
            }
        }

        float timeToLine = (distToLine - GameSettings.BallRadius) / -velocityTowardsNormal;
        if (timeToLine < 0)
        {
            return float.PositiveInfinity;
        }

        Vector3 impactPos = ball.Position + (ball.LinearVelocity * timeToLine);
        Vector3 contactPoint = impactPos - (segment.Normal * GameSettings.BallRadius);

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
        float cQuad = deltaP.LengthSquared() - (GameSettings.BallRadius * GameSettings.BallRadius);

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

    public static float CalculatePocketCrossTime(in BallState ball, in PocketBeam pocket, float ballRadius)
    {
        // 1. Are we actually moving towards the pocket?
        float vNorm = Vector3.Dot(ball.LinearVelocity, pocket.Normal);
        if (vNorm >= 0)
        {
            return float.PositiveInfinity; // Moving away or parallel
        }

        // 2. THE EXPANDED PLANE MATH
        // Distance from the ball's center to the mathematical center of the laser
        float distToPlane = Vector3.Dot(ball.Position - pocket.P1, pocket.Normal);

        // We want the exact time the distance closes to our combined radii
        float combinedRadii = pocket.Radius + ballRadius;

        // Formula: distToPlane + (Velocity * time) = combinedRadii
        float timeToTouch = (combinedRadii - distToPlane) / vNorm;

        // If it's negative, we are already overlapping or past it
        if (timeToTouch < 0)
        {
            return float.PositiveInfinity;
        }

        // 3. Where will the ball be at that exact microsecond in the future?
        Vector3 futurePos = ball.Position + (ball.LinearVelocity * timeToTouch);

        // 4. THE ALTITUDE CHECK
        // If the ball is jumping, did it fly over the gate?
        if ((futurePos.Y - ballRadius) > pocket.Height)
        {
            return float.PositiveInfinity;
        }

        // 5. THE CAPSULE CORE BOUNDS CHECK
        // Project the future position onto the P1->P2 line to ensure it hit the gate, 
        // and didn't miss to the left or right!
        Vector3 beamVector = pocket.P2 - pocket.P1;
        Vector3 ballToP1 = futurePos - pocket.P1;

        float beamLengthSq = beamVector.LengthSquared();
        float tProj = Vector3.Dot(ballToP1, beamVector) / beamLengthSq;

        // If the projection is between 0.0 (P1) and 1.0 (P2), it cleanly hit the capsule!
        if (tProj >= 0f && tProj <= 1f)
        {
            return timeToTouch;
        }

        return float.PositiveInfinity;
    }

    public static float CalculateBallBallImpactTime(in BallState a, in BallState b)
    {
        Vector3 deltaP = a.Position - b.Position;
        Vector3 deltaV = a.LinearVelocity - b.LinearVelocity;

        float aQuad = deltaV.LengthSquared();
        float bQuad = 2.0f * Vector3.Dot(deltaP, deltaV);
        float cQuad = deltaP.LengthSquared() - (4.0f * GameSettings.BallRadius * GameSettings.BallRadius);

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