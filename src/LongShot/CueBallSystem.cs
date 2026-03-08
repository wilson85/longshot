using System.Numerics;
using LongShot.Engine;

namespace LongShot;

public sealed class CueBallSystem
{
    readonly BilliardsEngine _engine;

    const int CueBallId = 0;

    const float BallMass = 0.17f;

    const float SpinMultiplier = 15f;

    public CueBallSystem(BilliardsEngine engine)
    {
        _engine = engine;
    }

    public void ApplyShot(Shot shot)
    {
        var dir = Vector3.Normalize(shot.Direction);

        ApplyLinearImpulse(dir, shot.Power);

        ApplySpin(shot.TipOffset, shot.Power);
    }

    void ApplyLinearImpulse(Vector3 direction, float power)
    {
        var impulse = direction * (power * BallMass);

        _engine.ApplyImpulse(CueBallId, impulse);
    }

    void ApplySpin(Vector2 tipOffset, float power)
    {
        Vector3 spinAxis = new(
            -tipOffset.Y,
             tipOffset.X,
             0);

        var angularImpulse = spinAxis * power * SpinMultiplier;

        _engine.ApplyAngularImpulse(CueBallId, angularImpulse);
    }
}