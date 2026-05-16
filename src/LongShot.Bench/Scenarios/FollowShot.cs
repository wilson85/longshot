using System.Numerics;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>Top English. Cue ball should continue forward after contact.</summary>
public static class FollowShot
{
    public static ScenarioResult Run() => new Scenario("follow_shot",
            "Top English. Cue ball must continue past the contact point (follow-through).")
        .PlaceCue(new Vector3(0, R, -0.4f))
        .PlaceObjectBall(new Vector3(0, R, 0.4f))
        .Strike(force: 0.6f, aim: Forward, offset: new Vector3(0, 0.55f * R, 0))
        // Half a second - long enough to see follow-through, short enough that the object ball
        // hasn't yet bounced off the foot rail and come back to re-collide.
        .RunFor(0.5f)
        .Expect("Cue ball drives past the contact point (follow-through)",
            s => s.CueTrajectory.FinalPosition.Z > 0.4f - 2 * R + 0.05f,
            s => $"cue final Z = {s.CueTrajectory.FinalPosition.Z:0.000} (contact at z={0.4f - 2 * R:0.000})")
        .Expect("Object ball is moving forward (positive Z velocity)",
            s => s.ObjectTrajectory().FinalPosition.Z > s.ObjectTrajectory().InitialPosition.Z + 0.5f,
            s => $"object Z travel = {s.ObjectTrajectory().FinalPosition.Z - s.ObjectTrajectory().InitialPosition.Z:0.000}")
        .Finish();
}
