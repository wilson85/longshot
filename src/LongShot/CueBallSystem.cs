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
        float power = shot.Power;

        // 1. Calculate the Linear push (The ball moving forward)
        Vector3 linearImpulse = forward * power;

        // 2. Calculate the Right and Up vectors relative to the shot direction
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 up = Vector3.Cross(forward, right);

        // 3. Convert the normalized 2D TipOffset (-1 to 1) into physical 3D world space
        // We multiply by the ball's radius so the math matches the physical mesh
        float radius = GameSettings.StandardBallRadius;
        Vector3 worldTipOffset = (right * shot.TipOffset.X * radius) + (up * shot.TipOffset.Y * radius);

        // 4. Calculate the Spin! (Cross product of the offset vector and the force vector)
        // Multiplying by a constant (e.g., 50f) lets you tune how "grippy" the cue tip is.
        Vector3 angularImpulse = Vector3.Cross(worldTipOffset, linearImpulse) * 30f;

        // 5. Apply BOTH to BepuPhysics!
        _engine.ApplyImpulse(0, linearImpulse);
        _engine.ApplyAngularImpulse(0, angularImpulse);
    }
}