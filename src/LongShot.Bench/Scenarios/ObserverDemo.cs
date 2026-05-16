using System.Linq;
using System.Numerics;
using LongShot.Rules;
using LongShot.Shot;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// End-to-end exercise of the observer pattern. Replays the combination-pot setup, runs
/// the shot through a <see cref="ShotRecorder"/>, then feeds the resulting
/// <see cref="ShotSummary"/> to two stacked observers (a base scorer and a "trick shot
/// bonus" overlay). Asserts each observer produces the expected score independently.
///
/// This is the pattern the fantasy / power-up system will use: every effect is just a new
/// IShotObserver registered alongside the standard rules. No engine coupling, no
/// coordination between observers.
/// </summary>
public static class ObserverDemo
{
    public static ScenarioResult Run()
    {
        // Same geometry as CombinationPot.cs (cue → A → B → top-right corner).
        var pocket = new Vector3(0.624f, 0, 1.124f);
        var dirToPocket = Vector3.Normalize(pocket);
        var ballBPos = pocket - (dirToPocket * 0.30f); ballBPos.Y = R;
        var ballAPos = ballBPos - (dirToPocket * 0.30f); ballAPos.Y = R;
        var cuePos = ballAPos - (dirToPocket * 0.30f); cuePos.Y = R;
        var aim = Vector3.Normalize(ballAPos - cuePos);

        var scenario = new Scenario("observer_demo",
                "Combination pot routed through ShotRecorder + scoring observers.")
            .PlaceCue(cuePos)
            .PlaceObjectBall(ballAPos)
            .PlaceObjectBall(ballBPos);

        // Stack two observers. They share the same ShotSummary and don't know about each other.
        var scoring = new SimpleScoring();
        var bonuses = new TrickShotBonuses();
        var observers = new IShotObserver[] { scoring, bonuses };

        // Notify pre-strike. Real games would pass game-state context here.
        var startCtx = new ShotContext
        {
            ShotNumber = 1,
            StateAtStart = scenario.Engine.SnapshotState(),
        };
        foreach (var o in observers) o.OnShotStart(startCtx);

        scenario.Strike(force: 0.8f, aim: aim).RunFor(3.0f);

        // Finalize the recorder and fan the summary out to every observer.
        var summary = scenario.Recorder.Finalize();
        foreach (var o in observers) o.OnShotEnd(summary);

        return scenario
            .Expect("Object ball B was pocketed (recorder captured the event)",
                _ => summary.Pocketings.Any(p => p.BallId == 2),
                _ => $"pocketings: {summary.Pocketings.Count}, ball-contacts: {summary.BallContacts.Count}")
            .Expect("Cue's first contact was ball A (id 1)",
                _ => summary.FirstContactBallId == 1,
                _ => $"first contact ball id = {summary.FirstContactBallId}")
            .Expect("Combination pattern detected (cue → A → B → pocket)",
                _ => bonuses.BonusScore >= bonuses.CombinationBonusPoints,
                _ => $"bonus log: {string.Join(" | ", bonuses.Log)}")
            .Expect("Base scorer awarded +1 for the pot",
                _ => scoring.Score == 1,
                _ => $"score = {scoring.Score}, log: {string.Join(" | ", scoring.Log)}")
            .Expect("Cue ball was not pocketed (no scratch)",
                _ => !summary.CueBallPocketed)
            .Finish();
    }
}
