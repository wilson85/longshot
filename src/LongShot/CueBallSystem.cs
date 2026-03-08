using System.Numerics;
using LongShot;
using LongShot.Engine;

public sealed class CueBallSystem(BilliardsEngine _engine)
{
    const int CueBallId = 0;
    const float BallMass = 0.17f;

    const float SpinMultiplier = 0.002f;

    public void ApplyShot(Shot shot)
    {
        _engine.ClearTrails();

        var dir = Vector3.Normalize(shot.Direction);
        ApplyLinearImpulse(dir, shot.Power);

        // Pass the direction into the spin calculator so we can find the local axes
        ApplySpin(shot.TipOffset, shot.Power, dir);
    }

    void ApplyLinearImpulse(Vector3 direction, float power)
    {
        var impulse = direction * (power * BallMass);
        _engine.ApplyImpulse(CueBallId, impulse);
    }

    void ApplySpin(Vector2 tipOffset, float power, Vector3 shotDirection)
    {
        Vector3 forward = shotDirection;
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.UnitY;

        float R = GameSettings.StandardBallRadius;

        // We know the player can hit up to 60% of the radius away from center
        float hitX = tipOffset.X * (R * 0.6f);
        float hitY = tipOffset.Y * (R * 0.6f);

        // Pythagorean theorem to find the Z-depth on the sphere's surface
        float hitZ = MathF.Sqrt((R * R) - (hitX * hitX) - (hitY * hitY));

        // The cue hits the BACK of the ball relative to the shot direction
        Vector3 worldHitOffset = (right * hitX) + (up * hitY) - (forward * hitZ);

        // Send it to the engine to permanently mark the ball
        _engine.SetChalkMark(CueBallId, worldHitOffset);

        // Pure Physics: Angular Impulse = r x F
        Vector3 linearImpulse = forward * (power * BallMass);
        Vector3 angularImpulse = Vector3.Cross(worldHitOffset, linearImpulse);

        _engine.ApplyAngularImpulse(CueBallId, angularImpulse);
    }
}