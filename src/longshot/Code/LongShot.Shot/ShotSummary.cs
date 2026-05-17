using System.Collections.Generic;
using SnVector3 = System.Numerics.Vector3;
using LongShot.Engine;

namespace LongShot.Shot;

/// <summary>
/// Everything that happened during one shot, from cue strike to the moment all balls
/// came to rest. The single object that every <see cref="IShotObserver"/> reads.
///
/// Rules layers, scoring multipliers, power-up effects, achievements, and ML difficulty
/// estimators all consume this same data. The engine produces it; nothing above the
/// engine modifies it.
/// </summary>
public sealed class ShotSummary
{
    // --- Inputs ---
    public CueStrikeData Strike { get; init; }
    public BallState[] StateAtStrike { get; init; }
    public BallState[] StateAtRest { get; init; }
    public float TotalSimulatedTime { get; init; }
    public bool AllBallsAtRest { get; init; }

    // --- Event timeline ---
    public IReadOnlyList<BallContactEvent> BallContacts { get; init; }
    public IReadOnlyList<RailContactEvent> RailContacts { get; init; }
    public IReadOnlyList<JawContactEvent> JawContacts { get; init; }
    public IReadOnlyList<PocketingEvent> Pocketings { get; init; }

    // --- Derived metrics (computed once at finalisation) ---

    /// <summary>The first non-cue ball the cue ball touched, or -1 if it touched nothing.</summary>
    public int FirstContactBallId { get; init; }
    /// <summary>Time at which the first ball contact happened, or -1 if none.</summary>
    public float FirstContactTime { get; init; }

    /// <summary>Did the cue ball end up in a pocket (a scratch)?</summary>
    public bool CueBallPocketed { get; init; }

    /// <summary>Number of rail-cushion contacts by the cue ball during this shot.</summary>
    public int CueRailBounceCount { get; init; }

    /// <summary>Cue ball's maximum height above the slate at any sample (0 if never airborne).</summary>
    public float CueMaxAirborneHeight { get; init; }
    /// <summary>True if the cue ball was airborne at any sample.</summary>
    public bool CueWentAirborne { get; init; }

    /// <summary>Sum of distance travelled by the cue ball between samples.</summary>
    public float CueTotalDistance { get; init; }
    /// <summary>Peak speed reached by the cue ball.</summary>
    public float CueMaxSpeed { get; init; }

    /// <summary>Likely a jump shot - cue went airborne AND was struck with elevation > 15°.</summary>
    public bool WasJumpShot { get; init; }

    /// <summary>
    /// Likely a massé - high cue elevation + lateral curve. Detected when elevation &gt; 45°
    /// AND the cue ball's horizontal heading changes by &gt; 25° between launch and rest.
    /// </summary>
    public bool WasMasse { get; init; }

    /// <summary>
    /// Convenience: was the shot legal in the most basic sense? The cue ball was struck,
    /// the cue ball was not pocketed, and the cue ball made contact with at least one
    /// other ball. (Game-variant fouls go further; this is the floor.)
    /// </summary>
    public bool WasLegalBaseline => !CueBallPocketed && FirstContactBallId >= 0;
}
