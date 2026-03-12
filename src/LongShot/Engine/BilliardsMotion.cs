using System.Numerics;
using BepuUtilities;

namespace LongShot.Engine;

public static class BilliardsMotion
{
    public const float BallRadius = 0.028575f;

    const float SlidingFriction = 0.20f;
    const float RollingFriction = 0.015f;
    const float SpinDamping = 0.002f;

    public static void Integrate(
        ref Vector3Wide linear,
        ref Vector3Wide angular,
        Vector<float> dt)
    {
        var radius = new Vector<float>(BallRadius);

        Vector3Wide contactOffset;
        contactOffset.X = Vector<float>.Zero;
        contactOffset.Y = -radius;
        contactOffset.Z = Vector<float>.Zero;

        var surfaceVelocity =
            linear +
            Vector3Wide.Cross(angular, contactOffset);

        var speed = Vector3Wide.Length(surfaceVelocity);

        var slidingMask =
            Vector.GreaterThan(speed, new Vector<float>(0.01f));

        var slideFriction = new Vector<float>(SlidingFriction);
        var rollFriction = new Vector<float>(RollingFriction);

        var friction =
            Vector.ConditionalSelect(slidingMask, slideFriction, rollFriction);

        var decay =
            Vector.Max(Vector<float>.Zero, new Vector<float>(1f) - friction * dt);

        linear *= decay;

        var spinDecay =
            Vector.Max(Vector<float>.Zero, new Vector<float>(1f) - new Vector<float>(SpinDamping) * dt);

        angular *= spinDecay;
    }
}