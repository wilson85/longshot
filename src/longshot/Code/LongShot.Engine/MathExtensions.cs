using SnVector3 = System.Numerics.Vector3;

namespace LongShot.Engine;

/// <summary>
/// Axis-convention helpers for porting between coordinate systems. The engine's internal
/// coordinate system is <b>Y-up, right-handed</b>: +X across the table (right), +Y up, +Z
/// along the table length (foot rail at +Z, head rail at -Z).
///
/// Source 2 / s&amp;box is <b>Z-up, right-handed</b>: +X forward, +Y left, +Z up. When the
/// engine drops into s&amp;box, every SnVector3 crossing the boundary needs the swap below.
/// Keep all axis-convention knowledge in this one file so the port is a single grep target.
/// </summary>
public static class MathExtensions
{
    /// <summary>
    /// Engine (Y-up) → s&amp;box (Z-up). Mapping:
    /// <list type="bullet">
    ///   <item>engine.X (right)   → s&amp;box.Y inverted (s&amp;box +Y is LEFT, so right is -Y)</item>
    ///   <item>engine.Y (up)      → s&amp;box.Z (up)</item>
    ///   <item>engine.Z (forward) → s&amp;box.X (forward)</item>
    /// </list>
    /// </summary>
    public static SnVector3 ToZUp(SnVector3 yUp) => new(yUp.Z, -yUp.X, yUp.Y);

    /// <summary>
    /// s&amp;box (Z-up) → engine (Y-up). Inverse of <see cref="ToZUp"/>.
    /// </summary>
    public static SnVector3 FromZUp(SnVector3 zUp) => new(-zUp.Y, zUp.Z, zUp.X);

    /// <summary>
    /// Apply the Y-up → Z-up swap to every ball state in place. Use only at the engine /
    /// s&amp;box boundary - the engine's own physics expects Y-up throughout.
    /// </summary>
    public static BallState ToZUp(BallState yUp) => new()
    {
        Position = ToZUp(yUp.Position),
        LinearVelocity = ToZUp(yUp.LinearVelocity),
        AngularVelocity = ToZUp(yUp.AngularVelocity),
        State = yUp.State,
    };

    /// <summary>Inverse of <see cref="ToZUp(BallState)"/>.</summary>
    public static BallState FromZUp(BallState zUp) => new()
    {
        Position = FromZUp(zUp.Position),
        LinearVelocity = FromZUp(zUp.LinearVelocity),
        AngularVelocity = FromZUp(zUp.AngularVelocity),
        State = zUp.State,
    };
}
