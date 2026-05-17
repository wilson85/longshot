using Sandbox;
using SnVec3 = System.Numerics.Vector3;
using LongShotEngine = LongShot.Engine;

namespace Longshot;

/// <summary>
/// Single seam between LongShot.Engine's coordinate system (Y-up, metres, System.Numerics)
/// and s&amp;box's world space (Z-up, source units = inches, Sandbox.Vector3).
///
/// Per the port skill: never let raw System.Numerics.Vector3 reach the inspector or
/// networked state — every direction goes through these two helpers.
/// </summary>
internal static class Conversions
{
    /// <summary>One metre in source units (one source unit = one inch).</summary>
    public const float UnitsPerMetre = 39.3701f;

    /// <summary>Engine (Y-up, metres) → s&amp;box (Z-up, source units).</summary>
    public static Vector3 EngineToWorld(SnVec3 engineYUp)
    {
        // Axis swap (Y-up → Z-up) lives in MathExtensions; the type swap and unit scale happen here.
        var sboxMetres = LongShotEngine.MathExtensions.ToZUp(engineYUp);
        return new Vector3(sboxMetres.X, sboxMetres.Y, sboxMetres.Z) * UnitsPerMetre;
    }

    /// <summary>s&amp;box (Z-up, source units) → engine (Y-up, metres).</summary>
    public static SnVec3 WorldToEngine(Vector3 worldUnits)
    {
        var metres = new SnVec3(worldUnits.x, worldUnits.y, worldUnits.z) / UnitsPerMetre;
        return LongShotEngine.MathExtensions.FromZUp(metres);
    }

    /// <summary>Scalar metres → source units. Use for radii, lengths.</summary>
    public static float MetresToUnits(float metres) => metres * UnitsPerMetre;

    /// <summary>Scalar source units → metres. Use for distances picked from the world.</summary>
    public static float UnitsToMetres(float units) => units / UnitsPerMetre;
}
