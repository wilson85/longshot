using System.Numerics;
using LongShot.Engine;

namespace LongShot;

public class CueBallSystem
{
    private readonly BilliardsEngine _engine;

    public CueBallSystem(BilliardsEngine engine)
    {
        _engine = engine;
    }

    public void ApplyShot(Shot shot)
    {
        Vector3 forward = shot.Direction;

        // 1. Actually use the clamped power!
        float power = MathF.Min(shot.Power, GameSettings.MaxShotPower);

        // 2. Clamp the TipOffset so the cue doesn't strike outside the ball's radius
        Vector2 safeOffset = shot.TipOffset;
        if (safeOffset.LengthSquared() > 1f)
        {
            safeOffset = Vector2.Normalize(safeOffset);
        }

        // Calculate the Linear push
        float ballMass = 0.17f;
        float cueSpeed = power; // Fixed: using the clamped power

        Vector3 linearImpulse = forward * cueSpeed * ballMass;

        // Calculate the Right and Up vectors relative to the shot direction
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 up = Vector3.Cross(forward, right);

        // Squirt (Deflection): pushing the ball slightly opposite to the english applied
        Vector3 squirt = right * safeOffset.X * -0.05f * power;
        linearImpulse += squirt;

        // Convert the safe offset into physical 3D world space
        float radius = GameSettings.StandardBallRadius;
        Vector3 worldTipOffset = (right * safeOffset.X * radius) + (up * safeOffset.Y * radius);

        // Angular Impulse calculation (r x J)
        const float spinFactor = 4f;
        Vector3 angularImpulse = Vector3.Cross(worldTipOffset, linearImpulse) * spinFactor;

        _engine.ApplyImpulse(0, linearImpulse);
        _engine.ApplyAngularImpulse(0, angularImpulse);
    }
}