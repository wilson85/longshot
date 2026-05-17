using System;
using SnVector3 = System.Numerics.Vector3;
using LongShot.Engine;

namespace LongShot.Shot;

/// <summary>
/// Per-shot input data captured at the moment <see cref="BilliardsEngine.StrikeCueBall"/>
/// is called. The aim is stored normalized so power-ups and difficulty evaluators can read
/// the elevation angle without renormalising.
/// </summary>
public sealed class CueStrikeData
{
    public int CueBallId { get; init; }
    /// <summary>Raw aim direction as passed to StrikeCueBall (pre-normalisation).</summary>
    public SnVector3 AimRaw { get; init; }
    /// <summary>Normalised aim direction. <c>AimNormalized.Y &lt; 0</c> means the cue is elevated.</summary>
    public SnVector3 AimNormalized { get; init; }
    /// <summary>Impulse magnitude in N·s. Initial cue speed = Force / Ball.Mass.</summary>
    public float Force { get; init; }
    /// <summary>Strike offset on the cue tip relative to ball centre. Length up to <c>BallRadius * Cue.MiscueLimit</c>.</summary>
    public SnVector3 HitOffset { get; init; }

    /// <summary>Cue elevation in radians from horizontal. 0 = level, π/2 = straight down.</summary>
    public float ElevationRadians => MathF.Asin(MathF.Max(-1f, MathF.Min(1f, -AimNormalized.Y)));
    public float ElevationDegrees => ElevationRadians * (180f / MathF.PI);

    /// <summary>True if the strike used non-zero English (any side/top/draw offset).</summary>
    public bool HasEnglish => HitOffset.LengthSquared() > 1e-6f;

    /// <summary>How much offset, as a fraction of ball radius. 1.0 = at the edge; 0.0 = centre-ball.</summary>
    public float EnglishMagnitudeRatio => HitOffset.Length() / GameSettings.BallRadius;
}

public sealed record BallContactEvent(int BallA, int BallB, float Time);
public sealed record RailContactEvent(int BallId, int RailSegmentIndex, float ImpactSpeed, float Time);
public sealed record JawContactEvent(int BallId, int JawCornerIndex, float ImpactSpeed, float Time);
public sealed record PocketingEvent(int BallId, SnVector3 DropPosition, float Time);
