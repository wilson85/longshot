using System.Numerics;
using LongShot.Engine;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// A hard break stroke. With no rack to absorb the cue, the ball just bounces between rails
/// and the table tests the combined cushion + cloth losses across multiple bounces.
///
/// Real-world reference: a strong stroke covers 4-6 table lengths before stopping
/// (Dr. Dave's "speed-normalisation" stroke is 4 lengths). On our 2.33 m table that's
/// roughly 9-14 m of total path. Higher impact speeds drop the cushion COR per the
/// dynamic-restitution model, so this is a different operating point from
/// <see cref="LagStrokeTravel"/>.
/// </summary>
public static class BreakStrokeTravel
{
    private const float ExpectedDistanceMin = 6.0f;   // ~2.5× length
    private const float ExpectedDistanceMax = 16.0f;  // ~7× length

    public static ScenarioResult Run()
    {
        var scenario = new Scenario("break_stroke_travel",
                "Hard break stroke with no rack. Real reference: 4-6 table lengths (~9-14m).")
            .PlaceCue(new Vector3(0, R, -1.05f))   // head spot
            .Strike(force: 1.6f, aim: Forward)
            .RunUntilRest(maxSeconds: 15f);

        float pathLength = scenario.CueTrajectory.TotalDistance;
        float tableLengths = pathLength / GameSettings.TableLength;

        // Count rail bounces by detecting Z-velocity sign flips.
        int bounceCount = 0;
        var samples = scenario.CueTrajectory.Samples;
        for (int i = 1; i < samples.Count; i++)
        {
            float prev = samples[i - 1].LinearVelocity.Z;
            float curr = samples[i].LinearVelocity.Z;
            if ((prev < -0.1f && curr > 0.1f) || (prev > 0.1f && curr < -0.1f))
            {
                bounceCount++;
            }
        }

        return scenario
            .Expect($"Total path within [{ExpectedDistanceMin:0.0}, {ExpectedDistanceMax:0.0}] m",
                _ => pathLength >= ExpectedDistanceMin && pathLength <= ExpectedDistanceMax,
                _ => $"measured = {pathLength:0.000} m ({tableLengths:0.00} table lengths)")
            .Expect("Multiple rail bounces occurred",
                _ => bounceCount >= 2,
                _ => $"observed {bounceCount} Z-rail bounces")
            .Expect("Simulation reached rest within budget",
                s => s.Engine.AreAllBallsAsleep(),
                s => $"elapsed {s.ElapsedSimTime:0.000}s, asleep = {s.Engine.AreAllBallsAsleep()}")
            .Finish();
    }
}
