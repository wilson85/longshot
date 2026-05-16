using System.Numerics;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>Bottom English (draw). Cue ball should reverse direction after contact.</summary>
public static class DrawShot
{
    // For draw to actually reverse the cue, the backspin must still be intact at the moment
    // of impact - which means the slide phase must not end before contact. Cue close to
    // object + strong impulse + maximum legal bottom offset.
    public static ScenarioResult Run() => new Scenario("draw_shot",
            "Bottom English at short range. Cue ball must reverse direction after contact.")
        .PlaceCue(new Vector3(0, R, 0.1f))
        .PlaceObjectBall(new Vector3(0, R, 0.5f))
        .Strike(force: 1.0f, aim: Forward, offset: new Vector3(0, -0.6f * R, 0))
        .RunFor(0.8f)
        .Expect("Cue ball reverses past its starting Z",
            s => s.CueTrajectory.FinalPosition.Z < 0.1f - 0.05f,
            s => $"cue final Z = {s.CueTrajectory.FinalPosition.Z:0.000} (started at 0.100)")
        .Expect("Object ball at some point reaches at least 0.4 m past its start",
            s => s.ObjectTrajectory().MaxZ > s.ObjectTrajectory().InitialPosition.Z + 0.4f,
            s => $"object peak forward = {s.ObjectTrajectory().MaxZ - s.ObjectTrajectory().InitialPosition.Z:0.000} m")
        .Finish();
}
