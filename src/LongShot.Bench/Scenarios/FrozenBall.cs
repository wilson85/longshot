using System.Numerics;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Frozen-ball strike: the cue ball is touching the object ball at the moment of the
/// strike. The collision must fire immediately (the engine's CCD must handle the
/// already-overlapping initial state) and the object should fly off cleanly while the
/// cue ball stops near contact.
///
/// This is a known edge case for CCD - balls already in contact need to resolve their
/// collision on the first fixed step without weird interpenetration or stuck states.
/// </summary>
public static class FrozenBall
{
    public static ScenarioResult Run()
    {
        var cuePos = new Vector3(0, R, -0.4f);
        // Place object exactly one ball-diameter ahead - balls are mathematically touching.
        // Tiny epsilon avoids hitting the cQuad <= 0 immediate-collision branch in CCD.
        var objectPos = new Vector3(0, R, -0.4f + 2 * R + 0.0001f);

        return new Scenario("frozen_ball",
                "Cue ball frozen against an object ball at the moment of strike.")
            .PlaceCue(cuePos)
            .PlaceObjectBall(objectPos)
            .Strike(force: 0.5f, aim: Forward)
            .RunFor(0.6f)
            .Expect("Object ball flew forward at least 0.3 m",
                s => s.ObjectTrajectory().FinalPosition.Z > objectPos.Z + 0.3f,
                s => $"object Z travel = {s.ObjectTrajectory().FinalPosition.Z - objectPos.Z:0.000} m")
            .Expect("Cue ball stopped near contact (stop-shot equivalent)",
                s => System.MathF.Abs(s.CueTrajectory.FinalPosition.Z - cuePos.Z) < 0.20f,
                s => $"cue Z travel = {s.CueTrajectory.FinalPosition.Z - cuePos.Z:0.000} m")
            .Expect("No balls pocketed",
                s => s.Pocketings.Count == 0)
            .Finish();
    }
}
