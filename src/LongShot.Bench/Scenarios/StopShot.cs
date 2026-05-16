using System.Numerics;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>Centre-ball straight contact between equal-mass balls. The cue should stop dead.</summary>
public static class StopShot
{
    public static ScenarioResult Run() => new Scenario("stop_shot",
            "Centre-ball straight. Cue ball must stop on contact (Newton's cradle).")
        .PlaceCue(new Vector3(0, R, -0.4f))
        .PlaceObjectBall(new Vector3(0, R, 0.4f))
        .Strike(force: 0.4f, aim: Forward)
        .RunUntilRest()
        .Expect("Cue ball stops within 0.15 m of the contact point",
            s => System.MathF.Abs(s.CueTrajectory.FinalPosition.Z - (0.4f - 2 * R)) < 0.15f,
            s => $"cue final Z = {s.CueTrajectory.FinalPosition.Z:0.000}, expected ~{0.4f - 2 * R:0.000}")
        .Expect("Object ball travels forward at least 0.5 m",
            s => s.ObjectTrajectory().FinalPosition.Z > s.ObjectTrajectory().InitialPosition.Z + 0.5f,
            s => $"object Z travel = {s.ObjectTrajectory().FinalPosition.Z - s.ObjectTrajectory().InitialPosition.Z:0.000}")
        .Expect("No balls pocketed",
            s => s.Pocketings.Count == 0)
        .Finish();
}
