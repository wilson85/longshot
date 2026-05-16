using System.Numerics;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Combination pot: the cue strikes object A which strikes object B, sending B into the
/// top-right corner pocket. All three balls + the pocket are collinear so the geometry
/// is straightforward.
/// </summary>
public static class CombinationPot
{
    public static ScenarioResult Run()
    {
        // Top-right corner pocket centre (approximate).
        var pocket = new Vector3(0.624f, 0, 1.124f);
        var dirToPocket = Vector3.Normalize(pocket);

        // Place B near the pocket, A in front of B, cue further back - all on the same line.
        var ballBPos = pocket - (dirToPocket * 0.30f);
        ballBPos.Y = R;
        var ballAPos = ballBPos - (dirToPocket * 0.30f);
        ballAPos.Y = R;
        var cuePos = ballAPos - (dirToPocket * 0.30f);
        cuePos.Y = R;

        var aim = Vector3.Normalize(ballAPos - cuePos);

        return new Scenario("combination_pot",
                "Cue → A → B → top-right corner pocket. All collinear.")
            .PlaceCue(cuePos)
            .PlaceObjectBall(ballAPos)
            .PlaceObjectBall(ballBPos)
            .Strike(force: 0.8f, aim: aim)
            .RunFor(3.0f)
            .Expect("Object ball B was pocketed",
                s => s.Pocketings.Exists(p => p.Id == 2),
                s => s.Pocketings.Count == 0
                    ? "no balls pocketed"
                    : $"pocketed: {string.Join(", ", s.Pocketings)}")
            .Expect("Cue ball was NOT pocketed",
                s => !s.Pocketings.Exists(p => p.Id == 0))
            .Expect("Object ball A was NOT pocketed (it passed energy to B)",
                s => !s.Pocketings.Exists(p => p.Id == 1))
            .Finish();
    }
}
