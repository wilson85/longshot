using System.Numerics;
using LongShot.Rendering;

namespace LongShot.Engine;

/// <summary>
/// Reusable physics calculations to prevent duplicated math across the engine.
/// </summary>
public static class PhysicsMath
{
    // I = 2/5 * m * r^2
    public static float GetInertia(float mass, float radius) =>
        0.4f * mass * (radius * radius);

    // V_surface = V_linear + (Omega x R_contact)
    public static Vector3 GetSurfaceVelocity(Vector3 linearVel, Vector3 angularVel, Vector3 contactVector) =>
        linearVel + Vector3.Cross(angularVel, contactVector);

    // Dynamic restitution: materials absorb more energy at higher impact speeds
    public static float CalculateDynamicRestitution(float impactSpeed, float maxRestitution, float minRestitution, float speedDecay) =>
        Math.Clamp(maxRestitution - (impactSpeed * speedDecay), minRestitution, maxRestitution);

    // Applies a force at a specific contact point, affecting both linear and angular velocity
    public static void ApplyImpulse(ref BallState ball, Vector3 impulse, Vector3 contactVector, float mass, float radius)
    {
        ball.LinearVelocity += impulse / mass;

        float inertia = GetInertia(mass, radius);
        Vector3 angularImpulse = Vector3.Cross(contactVector, impulse);
        ball.AngularVelocity += angularImpulse / inertia;
    }
}
