namespace LongShot.Shot;

/// <summary>
/// The single extension point above the engine. Every rules system, power-up, scoring
/// modifier, achievement tracker, and difficulty evaluator implements this. They co-exist
/// without knowing about each other - the same shot can score under standard 8-ball rules,
/// trigger a "rail-kill bonus" power-up, and unlock an "ALL DAY" achievement, all from
/// one <see cref="ShotSummary"/>.
/// </summary>
public interface IShotObserver
{
    /// <summary>Called once at the very start of a shot, before the cue strike.</summary>
    void OnShotStart(ShotContext context) { }

    /// <summary>Called once when all balls come to rest (or the shot times out).</summary>
    void OnShotEnd(ShotSummary summary);
}

/// <summary>
/// Context handed to observers at the start of a shot. Game-variant state (whose turn,
/// what's the legal target ball, score-so-far) typically lives on the observer itself;
/// this is just the engine-level snapshot needed to interpret the upcoming shot.
/// </summary>
public sealed class ShotContext
{
    public int ShotNumber { get; init; }
    public LongShot.Engine.BallState[] StateAtStart { get; init; }
}
