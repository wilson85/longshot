using System.Numerics;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Side English into a rail. A pure perpendicular impact with right-hand spin should kick the
/// cue ball laterally on the way out instead of returning along the incoming line.
/// </summary>
public static class EnglishOffRail
{
    public static ScenarioResult Run() => new Scenario("english_off_rail",
            "Right-hand English perpendicular into the head rail. The rebound should kick to one side.")
        .PlaceCue(new Vector3(0, R, 0.4f))   // start near the foot, pointing toward the head rail
        .Strike(force: 0.55f, aim: -Forward, offset: new Vector3(0.55f * R, 0, 0))
        .RunUntilRest()
        .Expect("Cue ball moved to the rail and bounced back into play",
            s => s.CueTrajectory.MaxSpeed > 1f && s.CueTrajectory.FinalPosition.Z > -1.0f)
        .Expect("Lateral X offset after rebound > 0.05 m",
            s => System.MathF.Abs(s.CueTrajectory.FinalPosition.X - s.CueTrajectory.InitialPosition.X) > 0.05f,
            s => $"|dX| = {System.MathF.Abs(s.CueTrajectory.FinalPosition.X - s.CueTrajectory.InitialPosition.X):0.000}m")
        .Finish();
}
