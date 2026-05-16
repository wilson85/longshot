using System.Numerics;
using LongShot.Engine;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// A "lag stroke" is a medium-soft cue stroke - traditionally used to determine break order.
/// The ball should travel one full table length, hit the foot rail, and roll back to stop
/// approximately level with the head spot.
///
/// Real-world reference: total travel ≈ 1.5 × table length on a tournament-spec table.
/// On our 2.33 m table, that's about 3.5 m of total path length.
///
/// This is the single most informative cushion-physics test because it combines:
///   1. Cloth sliding friction
///   2. Slide-to-roll transition
///   3. Cushion bounce energy loss
///   4. Cloth rolling friction (including nap resistance at low speeds)
///
/// If lag distance is too long → cloth friction too low or cushion COR too high.
/// If too short → friction/COR too high.
/// </summary>
public static class LagStrokeTravel
{
    private const float ExpectedDistanceMin = 2.5f; // 1.1× length
    private const float ExpectedDistanceMax = 5.0f; // 2.1× length
    private const float TargetDistance = 3.5f;      // ~1.5× table length

    public static ScenarioResult Run()
    {
        var scenario = new Scenario("lag_stroke_travel",
                $"Lag stroke (medium force). Real reference: total path ≈ 1.5 × table length ({TargetDistance:0.0}m).")
            .PlaceCue(new Vector3(0, R, -1.05f))   // head spot
            .Strike(force: 0.45f, aim: Forward)
            .RunUntilRest(maxSeconds: 8f);

        float pathLength = scenario.CueTrajectory.TotalDistance;
        float tableLengths = pathLength / GameSettings.TableLength;
        float finalZ = scenario.CueTrajectory.FinalPosition.Z;

        return scenario
            .Expect($"Total path length within [{ExpectedDistanceMin:0.0}, {ExpectedDistanceMax:0.0}] m",
                _ => pathLength >= ExpectedDistanceMin && pathLength <= ExpectedDistanceMax,
                _ => $"measured = {pathLength:0.000} m ({tableLengths:0.00} table lengths; real ≈ 1.5)")
            .Expect("Cue ball ends in the half nearer to the start (lag came back)",
                _ => finalZ < 0.3f,
                _ => $"final Z = {finalZ:0.000} m (started at -1.050)")
            .Expect("Simulation reached rest within the budget",
                s => s.Engine.AreAllBallsAsleep(),
                s => $"elapsed {s.ElapsedSimTime:0.000}s, asleep = {s.Engine.AreAllBallsAsleep()}")
            .Finish();
    }
}
