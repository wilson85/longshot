using System;
using System.Numerics;

namespace LongShot.Engine;

/// <summary>
/// Handles all physical interactions on the table including cloth forces, rail bounces, and collisions.
/// </summary>
public static class TablePhysics
{
    public static void ResolveCushionCollision(ref BallState ball, in CushionSegment segment, in PhysicsConfig config)
    {
        float velocityAlongNormal = Vector3.Dot(ball.LinearVelocity, segment.Normal);
        if (velocityAlongNormal > 0)
        {
            return;
        }

        // Anti-clipping separation
        float distToLine = Vector3.Dot(ball.Position - segment.Start, segment.Normal);
        float overlap = config.Ball.Radius - distToLine;
        if (overlap > 0)
        {
            ball.Position += segment.Normal * (overlap + 0.0001f);
        }

        float impactSpeed = -velocityAlongNormal;
        float restitution = PhysicsMath.CalculateDynamicRestitution(impactSpeed, config.Cushion.MaxRestitution, config.Cushion.MinRestitution, config.Cushion.SpeedDecay);

        Vector3 impulseNormal = -(1 + restitution) * velocityAlongNormal * segment.Normal * config.Ball.Mass;
        float impulseNormalMag = impulseNormal.Length();

        Vector3 tangentDir = Vector3.Normalize(segment.End - segment.Start);

        // Rail nose contact is above the ball centre; resolve the geometric offset.
        float hOffset = config.Cushion.NoseHeight - config.Ball.Radius;
        float hOffsetClamped = Math.Clamp(hOffset, -config.Ball.Radius, config.Ball.Radius);
        float horizontalOffset = MathF.Sqrt((config.Ball.Radius * config.Ball.Radius) - (hOffsetClamped * hOffsetClamped));
        Vector3 contactVector = (-segment.Normal * horizontalOffset) + (Vector3.UnitY * hOffsetClamped);

        Vector3 surfaceVel = PhysicsMath.GetSurfaceVelocity(ball.LinearVelocity, ball.AngularVelocity, contactVector);

        float slipSpeed = Vector3.Dot(surfaceVel, tangentDir);
        float verticalSlipSpeed = surfaceVel.Y;

        Vector2 slip2D = new Vector2(slipSpeed, verticalSlipSpeed);
        float totalSlipMag = slip2D.Length();

        Vector3 frictionImpulse = Vector3.Zero;
        if (totalSlipMag > 0.0001f)
        {
            float maxFriction = impulseNormalMag * config.Cushion.FrictionCoeff;
            float effectiveMass = PhysicsMath.SingleSphereContactMass(config.Ball.Mass);
            float requiredFriction = totalSlipMag * effectiveMass;

            float appliedFrictionMag = MathF.Min(requiredFriction, maxFriction);
            Vector2 appliedFriction2D = -(slip2D / totalSlipMag) * appliedFrictionMag;

            // Dampen the vertical friction component to stop backspin from violently grabbing the rail.
            frictionImpulse = (tangentDir * appliedFriction2D.X) +
                              (Vector3.UnitY * (appliedFriction2D.Y * config.Cushion.VerticalFrictionMultiplier));
        }

        // Rubber rail compression bleeds angular momentum on every axis, not only side-spin (Y).
        float dynamicAbsorption = Math.Clamp(impulseNormalMag * config.Cushion.DynamicSpinMultiplier, 0f, config.Cushion.MaxDynamicAbsorption);
        float spinRetained = 1.0f - (config.Cushion.BaseSpinAbsorption + dynamicAbsorption);
        ball.AngularVelocity *= spinRetained;

        PhysicsMath.ApplyImpulse(ref ball, impulseNormal + frictionImpulse, contactVector, config.Ball.Mass, config.Ball.Radius);

        // Rail bounce reverses linear velocity but leaves spin largely intact; the contact patch
        // is once again slipping, so re-engage cloth sliding friction.
        ball.State = MotionState.Sliding;
    }

    public static void ResolveJawCornerCollision(ref BallState ball, Vector3 cornerPoint, in PhysicsConfig config)
    {
        Vector3 normal = Vector3.Normalize(ball.Position - cornerPoint);
        float velocityAlongNormal = Vector3.Dot(ball.LinearVelocity, normal);

        if (velocityAlongNormal > 0)
        {
            return;
        }

        float dist = Vector3.Distance(ball.Position, cornerPoint);
        float overlap = config.Ball.Radius - dist;
        if (overlap > 0)
        {
            ball.Position += normal * (overlap + 0.0001f);
        }

        float impactSpeed = -velocityAlongNormal;
        float restitution = PhysicsMath.CalculateDynamicRestitution(impactSpeed, config.Cushion.MaxRestitution, config.Cushion.MinRestitution, config.Cushion.SpeedDecay);

        Vector3 impulseNormal = -(1 + restitution) * velocityAlongNormal * normal * config.Ball.Mass;

        Vector3 contactVector = -normal * config.Ball.Radius;
        Vector3 surfaceVel = PhysicsMath.GetSurfaceVelocity(ball.LinearVelocity, ball.AngularVelocity, contactVector);
        Vector3 tangent = surfaceVel - (Vector3.Dot(surfaceVel, normal) * normal);

        Vector3 frictionImpulse = Vector3.Zero;
        if (tangent.LengthSquared() > 0.0001f)
        {
            Vector3 tangentDir = Vector3.Normalize(tangent);
            float maxFriction = impulseNormal.Length() * config.Cushion.FrictionCoeff;
            float effectiveMass = PhysicsMath.SingleSphereContactMass(config.Ball.Mass);

            frictionImpulse = -tangentDir * MathF.Max(MathF.Min(tangent.Length() * effectiveMass, maxFriction), -maxFriction);
        }

        PhysicsMath.ApplyImpulse(ref ball, impulseNormal + frictionImpulse, contactVector, config.Ball.Mass, config.Ball.Radius);
        ball.State = MotionState.Sliding;
    }

    public static void ResolveBallBallCollision(ref BallState a, ref BallState b, in PhysicsConfig config)
    {
        Vector3 normal = Vector3.Normalize(b.Position - a.Position);
        Vector3 relVel = b.LinearVelocity - a.LinearVelocity;
        float velAlongNormal = Vector3.Dot(relVel, normal);

        if (velAlongNormal > 0)
        {
            return;
        }

        float overlap = (config.Ball.Radius * 2.0f) - Vector3.Distance(a.Position, b.Position);
        if (overlap > 0)
        {
            Vector3 separation = normal * (overlap * 0.501f);
            a.Position -= separation;
            b.Position += separation;
        }

        float impactSpeed = -velAlongNormal;
        float restitution = PhysicsMath.CalculateDynamicRestitution(
            impactSpeed, config.Ball.MaxRestitution, config.Ball.MinRestitution, config.Ball.RestitutionDecay);

        float impulseMagnitude = -(1 + restitution) * velAlongNormal / ((1 / config.Ball.Mass) + (1 / config.Ball.Mass));
        Vector3 impulseNormal = impulseMagnitude * normal;

        a.LinearVelocity -= impulseNormal / config.Ball.Mass;
        b.LinearVelocity += impulseNormal / config.Ball.Mass;

        Vector3 rA = normal * config.Ball.Radius;
        Vector3 rB = -normal * config.Ball.Radius;

        Vector3 surfVelA = PhysicsMath.GetSurfaceVelocity(a.LinearVelocity, a.AngularVelocity, rA);
        Vector3 surfVelB = PhysicsMath.GetSurfaceVelocity(b.LinearVelocity, b.AngularVelocity, rB);

        Vector3 relSurfVel = surfVelA - surfVelB;
        Vector3 tangent = relSurfVel - (Vector3.Dot(relSurfVel, normal) * normal);

        if (tangent.LengthSquared() > 0.0001f)
        {
            Vector3 tangentDir = Vector3.Normalize(tangent);
            float maxFriction = impulseMagnitude * config.Ball.Friction;
            float effectiveMass = PhysicsMath.TwoSphereContactMass(config.Ball.Mass);

            float appliedFriction = MathF.Min(tangent.Length() * effectiveMass, maxFriction);
            Vector3 frictionImpulse = tangentDir * appliedFriction;

            PhysicsMath.ApplyImpulse(ref a, -frictionImpulse, rA, config.Ball.Mass, config.Ball.Radius);
            PhysicsMath.ApplyImpulse(ref b, frictionImpulse, rB, config.Ball.Mass, config.Ball.Radius);
        }

        a.State = MotionState.Sliding;
        b.State = MotionState.Sliding;
    }

    public static void UpdateBallMotion(ref BallState ball, float dt, in PhysicsConfig config)
    {
        if (ball.State == MotionState.Stationary || ball.State == MotionState.Pocketed)
        {
            return;
        }

        // --- VERTICAL PHYSICS (Gravity & Slate Bouncing) ---
        ball.LinearVelocity.Y -= config.Env.Gravity * dt;

        if (ball.Position.Y <= config.Ball.Radius)
        {
            ball.Position.Y = config.Ball.Radius;

            // Bounce threshold: only landings with significant downward velocity (e.g. a jump
            // shot or a forceful airborne ball) get the elastic slate bounce. Small Y
            // velocities (a few cm/s, picked up from rail-nose friction during normal play)
            // settle immediately - otherwise the ball chatters on the slate for seconds.
            const float SlateBounceThreshold = -0.5f;
            if (ball.LinearVelocity.Y < SlateBounceThreshold)
            {
                ball.LinearVelocity.Y *= -config.Env.SlateRestitution;
            }
            else
            {
                ball.LinearVelocity.Y = 0f;
                if (ball.State == MotionState.Airborne)
                {
                    ball.State = MotionState.Sliding;
                }
            }
        }
        else if (ball.Position.Y > config.Ball.Radius + 0.001f)
        {
            ball.State = MotionState.Airborne;
        }

        // --- HORIZONTAL PHYSICS (Cloth Friction) ---
        if (ball.State != MotionState.Airborne)
        {
            UpdateCloth(ref ball, dt, in config);
        }
    }

    private static void UpdateCloth(ref BallState ball, float dt, in PhysicsConfig config)
    {
        if (ball.State is MotionState.Stationary or MotionState.Airborne)
        {
            return;
        }

        // Y-axis spin decays via exponential drag for fast spin, then linear dry friction at the tail.
        float currentY = ball.AngularVelocity.Y;
        if (currentY != 0f)
        {
            currentY *= MathF.Exp(-config.Cloth.SpinDrag * dt);

            float decayY = config.Cloth.SpinFriction * dt;
            if (MathF.Abs(currentY) <= decayY)
            {
                currentY = 0f;
            }
            else
            {
                currentY -= MathF.Sign(currentY) * decayY;
            }
            ball.AngularVelocity.Y = currentY;
        }

        Vector3 contactVector = new Vector3(0, -config.Ball.Radius, 0);
        Vector3 surfaceVelocity = PhysicsMath.GetSurfaceVelocity(ball.LinearVelocity, ball.AngularVelocity, contactVector);
        Vector3 slip = new(surfaceVelocity.X, 0, surfaceVelocity.Z);
        float slipMag = slip.Length();

        if (ball.State == MotionState.Sliding || slipMag > 0.0001f)
        {
            ApplySliding(ref ball, slip, slipMag, dt, in config);
        }
        else if (ball.State == MotionState.Rolling)
        {
            ApplyRolling(ref ball, dt, in config);
        }

        ApplySleep(ref ball, in config);
    }

    private static void ApplySliding(ref BallState ball, Vector3 slip, float slipMag, float dt, in PhysicsConfig config)
    {
        // Damp X/Z spin proportional to slip (the contact patch can't support infinite point spin).
        float patchDrag = config.Cloth.ContactPatchDrag * slipMag * dt;
        ball.AngularVelocity.X *= MathF.Exp(-patchDrag);
        ball.AngularVelocity.Z *= MathF.Exp(-patchDrag);

        Vector3 dir = slipMag > 0.000001f ? -slip / slipMag : Vector3.Zero;

        // "Nap bunching" - nap direction modulates effective sliding friction with slip speed.
        float dynamicFriction = config.Cloth.SlidingFriction * (1.0f + (slipMag * config.Cloth.NapBunchingFactor));

        // Slip decays at 3.5x the rate of linear friction due to the ball's moment of inertia (2/5 m r²).
        float slipDecayRate = 3.5f * dynamicFriction;

        float timeToRoll = slipMag / slipDecayRate;

        if (timeToRoll <= dt)
        {
            // The ball finishes sliding during this frame. Apply sliding friction for exactly
            // the fraction required, then snap to pure rolling and apply rolling friction for the rest.
            float exactSlidingFriction = dynamicFriction * timeToRoll;
            Vector3 impulse = dir * exactSlidingFriction * config.Ball.Mass;
            Vector3 contactVector = new Vector3(0, -config.Ball.Radius, 0);

            PhysicsMath.ApplyImpulse(ref ball, impulse, contactVector, config.Ball.Mass, config.Ball.Radius);

            // Force the rotational velocity to perfectly match the linear velocity to prevent floating-point drift.
            ball.AngularVelocity = new Vector3(
                ball.LinearVelocity.Z / config.Ball.Radius,
                ball.AngularVelocity.Y,
                -ball.LinearVelocity.X / config.Ball.Radius
            );

            ball.State = MotionState.Rolling;

            float remainingDt = dt - timeToRoll;
            if (remainingDt > 0.0001f)
            {
                ApplyRolling(ref ball, remainingDt, in config);
            }
        }
        else
        {
            float appliedFriction = dynamicFriction * dt;
            Vector3 impulse = dir * appliedFriction * config.Ball.Mass;
            Vector3 contactVector = new Vector3(0, -config.Ball.Radius, 0);

            PhysicsMath.ApplyImpulse(ref ball, impulse, contactVector, config.Ball.Mass, config.Ball.Radius);

            // Apply swerve only if sliding for the whole frame (vertical spin curves trajectory).
            Vector2 horizVel = new Vector2(ball.LinearVelocity.X, ball.LinearVelocity.Z);
            if (MathF.Abs(ball.AngularVelocity.Y) > 0.05f && horizVel.LengthSquared() > 0.0001f)
            {
                Vector3 moveDir = Vector3.Normalize(new Vector3(horizVel.X, 0, horizVel.Y));
                Vector3 swerveDir = Vector3.Cross(moveDir, Vector3.UnitY);

                float spinSurfaceSpeed = ball.AngularVelocity.Y * config.Ball.Radius;
                Vector3 swerveDeltaV = swerveDir * spinSurfaceSpeed * config.Cloth.SwerveFactor * dt;

                PhysicsMath.ApplyImpulse(ref ball, swerveDeltaV * config.Ball.Mass, Vector3.Zero, config.Ball.Mass, config.Ball.Radius);
            }
        }
    }

    private static void ApplyRolling(ref BallState ball, float dt, in PhysicsConfig config)
    {
        Vector2 horizVel = new Vector2(ball.LinearVelocity.X, ball.LinearVelocity.Z);
        float speed = horizVel.Length();

        if (speed < config.Env.LinearSleepSpeed)
        {
            ball.LinearVelocity = new Vector3(0, ball.LinearVelocity.Y, 0);
            ball.AngularVelocity = new Vector3(0, ball.AngularVelocity.Y, 0);
            return;
        }

        float napResistance = speed < config.Cloth.NapResistanceSpeed
            ? (config.Cloth.NapResistanceSpeed - speed) * config.Cloth.NapResistanceMultiplier
            : 0f;

        float effectiveFriction = config.Cloth.RollingFriction + napResistance;

        Vector2 decel2D = horizVel / speed * effectiveFriction * dt;
        Vector3 decel = new Vector3(decel2D.X, 0, decel2D.Y);

        if (decel.LengthSquared() >= horizVel.LengthSquared())
        {
            ball.LinearVelocity = new Vector3(0, ball.LinearVelocity.Y, 0);
        }
        else
        {
            ball.LinearVelocity -= decel;
        }

        ball.AngularVelocity = new Vector3(
            ball.LinearVelocity.Z / config.Ball.Radius,
            ball.AngularVelocity.Y,
            -ball.LinearVelocity.X / config.Ball.Radius
        );

        ball.State = MotionState.Rolling;
    }

    private static void ApplySleep(ref BallState ball, in PhysicsConfig config)
    {
        Vector2 horizVel = new Vector2(ball.LinearVelocity.X, ball.LinearVelocity.Z);

        if (horizVel.LengthSquared() < config.Env.LinearSleepSpeed * config.Env.LinearSleepSpeed &&
            MathF.Abs(ball.AngularVelocity.Y) < config.Env.AngularSleepSpeed)
        {
            ball.LinearVelocity = Vector3.Zero;
            ball.AngularVelocity = Vector3.Zero;
            ball.State = MotionState.Stationary;
        }
    }
}
