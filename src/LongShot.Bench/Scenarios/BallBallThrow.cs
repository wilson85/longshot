using System.Numerics;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Side English produces two visible effects on a contact shot:
///   1. <b>Squirt</b>: the cue ball deflects opposite to the English side (right English
///      sends the cue slightly left).
///   2. <b>Throw + geometric deflection</b>: the object ball departs along a path that is
///      laterally displaced from the pure aim line - the combination of friction at the
///      contact patch and the squirt-induced off-centre geometry.
///
/// This scenario doesn't isolate either effect cleanly; it confirms that BOTH balls
/// pick up measurable lateral motion when the cue is struck with side English.
/// </summary>
public static class BallBallThrow
{
    public static ScenarioResult Run() => new Scenario("side_english_effects",
            "Side English at a center-on collision. Both balls should pick up lateral motion.")
        .PlaceCue(new Vector3(0, R, -0.2f))
        .PlaceObjectBall(new Vector3(0, R, 0.2f))
        .Strike(force: 0.5f, aim: Forward, offset: new Vector3(0.55f * R, 0, 0))
        .RunFor(0.5f)
        .Expect("Cue ball deflects from the aim line (squirt + post-contact friction)",
            s => System.MathF.Abs(s.CueTrajectory.FinalPosition.X) > 0.005f,
            s => $"cue X drift = {s.CueTrajectory.FinalPosition.X - s.CueTrajectory.InitialPosition.X:0.0000} m")
        .Expect("Object ball departs with a lateral component",
            s => System.MathF.Abs(s.ObjectTrajectory().FinalPosition.X) > 0.005f,
            s => $"object X drift = {s.ObjectTrajectory().FinalPosition.X - s.ObjectTrajectory().InitialPosition.X:0.0000} m")
        .Expect("Object ball reached at least 0.2 m past its start (collision transferred energy)",
            s => s.ObjectTrajectory().MaxZ > s.ObjectTrajectory().InitialPosition.Z + 0.2f,
            s => $"object peak Z travel = {s.ObjectTrajectory().MaxZ - s.ObjectTrajectory().InitialPosition.Z:0.000}")
        .Finish();
}
