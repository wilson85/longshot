using System.Numerics;
using LongShot.Engine;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Full-power break into the 15-ball triangle rack. Smoke test for solver stability:
/// nothing should leave the table, the simulation should reach rest, and at least some balls
/// should scatter visibly.
/// </summary>
public static class HardBreak
{
    public static ScenarioResult Run()
    {
        var scenario = new Scenario("hard_break",
            "Full-power break into a 15-ball triangle. Solver stability + scatter check.");

        scenario.PlaceCue(new Vector3(0, R, -0.8f));

        // Tight triangle at the foot spot. No jitter - bench is for deterministic regression.
        float spacing = R * 2.001f;
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col <= row; col++)
            {
                var pos = new Vector3(
                    (col - (row * 0.5f)) * spacing,
                    R,
                    0.8f + (row * spacing * 0.866f));
                scenario.PlaceObjectBall(pos);
            }
        }

        return scenario
            .Strike(force: 1.6f, aim: Forward)
            .RunUntilRest(maxSeconds: 15f)
            .Expect("Simulation reached rest within budget",
                s => s.Engine.AreAllBallsAsleep(),
                s => $"elapsed {s.ElapsedSimTime:0.000}s, sleep={s.Engine.AreAllBallsAsleep()}")
            .Expect("No ball escaped the table",
                s =>
                {
                    foreach (var t in s.Trajectories)
                    {
                        if (t.FinalState == MotionState.Pocketed) continue;
                        var p = t.FinalPosition;
                        if (System.MathF.Abs(p.X) > 0.80f || System.MathF.Abs(p.Z) > 1.30f) return false;
                        if (float.IsNaN(p.X) || float.IsNaN(p.Z)) return false;
                    }
                    return true;
                })
            .Expect("Rack scattered (max object-ball speed > 0.5 m/s)",
                s =>
                {
                    float maxSpeed = 0f;
                    for (int i = 1; i < s.Trajectories.Count; i++)
                    {
                        if (s.Trajectories[i].MaxSpeed > maxSpeed) maxSpeed = s.Trajectories[i].MaxSpeed;
                    }
                    return maxSpeed > 0.5f;
                },
                s =>
                {
                    float maxSpeed = 0f;
                    int maxId = -1;
                    for (int i = 1; i < s.Trajectories.Count; i++)
                    {
                        if (s.Trajectories[i].MaxSpeed > maxSpeed)
                        {
                            maxSpeed = s.Trajectories[i].MaxSpeed;
                            maxId = i;
                        }
                    }
                    return $"fastest object ball: id={maxId} @ {maxSpeed:0.000} m/s";
                })
            .Finish();
    }
}
