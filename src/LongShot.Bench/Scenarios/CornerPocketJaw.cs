using System.Numerics;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Shallow-angle approach to a corner pocket. The rail fillets either side of the throat
/// should deflect the ball back into play. This regression-tests the pocket-commitment-depth
/// fix - a magnetic pocket would just consume any ball that gets near the mouth.
/// </summary>
public static class CornerPocketJaw
{
    public static ScenarioResult Run()
    {
        // Object travels along +X at z=1.04 - that's BELOW the top-right pocket's throat
        // (P1 at z=1.084, P2 at z=1.165). Geometrically, the ball's trajectory misses the
        // throat entirely and instead hits the right rail's playfield-facing edge below
        // the corner jaw, bouncing back. Real pocket geometry: a ball running along the
        // rail with z < 1.084 cannot enter the corner pocket - it's outside the mouth.
        var objectPos = new Vector3(0.0f, R, 1.04f);
        var cuePos = new Vector3(-0.7f, R, 1.04f);
        var aim = new Vector3(1, 0, 0);

        return new Scenario("corner_pocket_jaw",
                "Object grazes below the corner pocket throat. Right rail should reject it.")
            .PlaceCue(cuePos)
            .PlaceObjectBall(objectPos)
            .Strike(force: 0.4f, aim: aim)
            .RunFor(2.5f)
            .Expect("Object ball was NOT pocketed",
                s => !s.Pocketings.Exists(p => p.Id == 1),
                s => s.Pocketings.Count == 0 ? "" : $"pocketed: {string.Join(", ", s.Pocketings)}")
            .Expect("Object ball remained on the playable surface",
                s => System.MathF.Abs(s.ObjectTrajectory().FinalPosition.X) < 0.75f
                  && System.MathF.Abs(s.ObjectTrajectory().FinalPosition.Z) < 1.25f,
                s => $"object final = ({s.ObjectTrajectory().FinalPosition.X:0.000}, {s.ObjectTrajectory().FinalPosition.Z:0.000})")
            .Finish();
    }
}
