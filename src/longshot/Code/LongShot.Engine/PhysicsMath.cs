using System;
using SnVector3 = System.Numerics.Vector3;

namespace LongShot.Engine;

/// <summary>Reusable physics primitives that the rest of the engine builds on.</summary>
public static class PhysicsMath
{
    /// <summary>Solid sphere inertia: I = (2/5) m r².</summary>
    public static float GetInertia(float mass, float radius) =>
        0.4f * mass * (radius * radius);

    /// <summary>
    /// Effective mass at the contact patch for one solid sphere sliding against a fixed surface
    /// (rail nose, jaw corner). Closed form: <c>1 / (1/m + r²/I) = 2m/7</c> for I = (2/5)·m·r².
    /// </summary>
    public static float SingleSphereContactMass(float mass) => mass * (2f / 7f);

    /// <summary>
    /// Effective mass at the contact patch for two equal-mass solid spheres in friction contact.
    /// Closed form: <c>1 / (1/m_a + 1/m_b + r²/I_a + r²/I_b) = m/7</c>.
    /// </summary>
    public static float TwoSphereContactMass(float mass) => mass * (1f / 7f);

    /// <summary>V_surface = V_linear + (Omega × R_contact)</summary>
    public static SnVector3 GetSurfaceVelocity(SnVector3 linearVel, SnVector3 angularVel, SnVector3 contactVector) =>
        linearVel + SnVector3.Cross(angularVel, contactVector);

    /// <summary>
    /// Dynamic restitution: materials absorb more energy at higher impact speeds.
    /// Returns a value clamped between minRestitution and maxRestitution.
    /// </summary>
    public static float CalculateDynamicRestitution(float impactSpeed, float maxRestitution, float minRestitution, float speedDecay) =>
        Math.Clamp(maxRestitution - (impactSpeed * speedDecay), minRestitution, maxRestitution);

    /// <summary>
    /// Applies an impulse at a specific contact point, mutating both linear and angular velocity.
    /// </summary>
    public static void ApplyImpulse(ref BallState ball, SnVector3 impulse, SnVector3 contactVector, float mass, float radius)
    {
        ball.LinearVelocity += impulse / mass;
        float inertia = GetInertia(mass, radius);
        SnVector3 angularImpulse = SnVector3.Cross(contactVector, impulse);
        ball.AngularVelocity += angularImpulse / inertia;
    }
}
