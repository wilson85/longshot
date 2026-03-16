using System.Numerics;
using LongShot;
using LongShot.Engine;

public class CueBallSystem
{
    private readonly BilliardsEngine _engine;

    public CueBallSystem(BilliardsEngine engine)
    {
        _engine = engine;
    }

    public void ApplyShot(Shot shot)
    {
        // The aim direction is already fully 3D (includes pitch/yaw) from ShotPowerManager
        Vector3 aimDirection = Vector3.Normalize(shot.Direction);

        // 2. Convert Target Velocity to Impulse
        // ShotPowerManager returns 'Power' as a target linear velocity (meters/second).
        // StrikeCueBall expects 'force' (impulse), and will divide it by the ball's mass.
        // Therefore, Impulse = TargetVelocity * Mass.
        float targetVelocity = shot.Power;
        float impulseMagnitude = targetVelocity * _engine.Config.Ball.Mass;

        // 3. Map the 2D Tip Offset into 3D World Space
        // We need the local X (Right) and Y (Up) axes relative to the tilted cue stick
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, aimDirection));

        // This gives us the local 'Up' along the face of the cue tip, 
        // accounting for the player elevating the back of the cue stick!
        Vector3 up = Vector3.Normalize(Vector3.Cross(aimDirection, right));

        // ShotPowerManager already clamped tipOffset to a max magnitude of 0.6.
        // We just scale it by the physical ball radius to get physical meters.
        float r = _engine.Config.Ball.Radius;
        Vector3 physicalTipOffset = (right * shot.TipOffset.X * r) +
                                    (up * shot.TipOffset.Y * r);

        // 4. Fire!
        _engine.StrikeCueBall(0, aimDirection, impulseMagnitude, physicalTipOffset);
    }
}