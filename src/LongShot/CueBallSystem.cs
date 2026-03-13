using System;
using System.Numerics;

namespace LongShot.Engine;

public class CueBallSystem
{
    private readonly BilliardsEngine _engine;

    // Hardcoded max power if GameSettings isn't migrated yet
    private const float MaxShotPower = 15.0f;

    public CueBallSystem(BilliardsEngine engine)
    {
        _engine = engine;
    }

    public void ApplyShot(Shot shot)
    {
        Vector3 forward = shot.Direction;

        // 1. Actually use the clamped power!
        float power = MathF.Min(shot.Power, MaxShotPower);

        // 2. Clamp the TipOffset so the cue doesn't strike outside the ball's radius
        // The TipOffset is expected to be normalized between -1.0 and 1.0
        Vector2 safeOffset = shot.TipOffset;
        if (safeOffset.LengthSquared() > 1f)
        {
            safeOffset = Vector2.Normalize(safeOffset);
        }

        // Calculate the Linear push
        float cueSpeed = power;
        Vector3 linearImpulse = forward * cueSpeed * BilliardsEngine.BallMass;

        // Calculate the Right and Up vectors relative to the shot direction
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 up = Vector3.Cross(forward, right);

        // Squirt (Deflection): pushing the ball slightly opposite to the english applied
        Vector3 squirt = right * safeOffset.X * -0.05f * power;
        linearImpulse += squirt;

        // Convert the safe offset into physical 3D world space relative to the center of the ball
        Vector3 worldTipOffset = (right * safeOffset.X * BilliardsEngine.BallRadius) +
                                 (up * safeOffset.Y * BilliardsEngine.BallRadius);

        // 3. Apply to the custom analytical engine
        // Note: The engine's ApplyImpulse now automatically calculates the Angular Impulse
        // using the inertia tensor and the cross product of the offset and the force!
        _engine.ApplyImpulse(0, linearImpulse, worldTipOffset);
    }
}