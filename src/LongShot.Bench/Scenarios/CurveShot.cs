using System.Numerics;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Side English on a cue ball with no contact should curve its trajectory in the direction
/// of the spin (Magnus / swerve effect). Right English strikes the cue on the +X side, which
/// drives the ball to curve toward +X over the sliding phase.
/// </summary>
public static class CurveShot
{
    public static ScenarioResult Run() => new Scenario("curve_shot",
            "Right English on an open table. Cue ball should curve to the right (+X) during slide.")
        .PlaceCue(new Vector3(0f, R, -0.9f))
        .Strike(force: 0.8f, aim: Forward, offset: new Vector3(0.55f * R, 0, 0))
        .RunFor(1.5f)
        .Expect("Cue ball curves to the right (peak +X drift > 0.05 m)",
            s =>
            {
                float maxX = float.MinValue;
                foreach (var sm in s.CueTrajectory.Samples) if (sm.Position.X > maxX) maxX = sm.Position.X;
                return maxX > s.CueTrajectory.InitialPosition.X + 0.05f;
            },
            s =>
            {
                float maxX = float.MinValue;
                foreach (var sm in s.CueTrajectory.Samples) if (sm.Position.X > maxX) maxX = sm.Position.X;
                return $"peak X drift = {maxX - s.CueTrajectory.InitialPosition.X:0.000} m";
            })
        .Expect("Cue ball reached the far half of the table (peak Z > 0.5)",
            s => s.CueTrajectory.MaxZ > 0.5f,
            s => $"cue peak Z = {s.CueTrajectory.MaxZ:0.000}")
        .Finish();
}
