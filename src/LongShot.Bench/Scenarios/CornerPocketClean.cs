using System.Numerics;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>Straight pot toward the top-right corner pocket. Object ball should be sunk.</summary>
public static class CornerPocketClean
{
    public static ScenarioResult Run()
    {
        // Top-right corner pocket centre is near (TableWidth/2, 0, TableLength/2).
        // Place the object ball on a clean line to that pocket.
        var objectPos = new Vector3(0.45f, R, 0.85f);
        var cuePos = new Vector3(-0.10f, R, -0.20f);

        // Aim from cue ball through the object ball at the pocket.
        // For a straight pot the aim line is cue -> ghost-ball position (object minus R toward pocket).
        var pocket = new Vector3(LongShot.Engine.GameSettings.TableWidth / 2f, 0, LongShot.Engine.GameSettings.TableLength / 2f);
        var objToPocket = Vector3.Normalize(pocket - objectPos);
        var ghost = objectPos - (objToPocket * 2f * R);
        var aim = Vector3.Normalize(ghost - cuePos);

        return new Scenario("corner_pocket_clean",
                "Straight pot to the top-right corner. Object ball should be sunk.")
            .PlaceCue(cuePos)
            .PlaceObjectBall(objectPos)
            .Strike(force: 0.7f, aim: aim)
            .RunUntilRest()
            .Expect("Object ball was pocketed",
                s => s.Pocketings.Exists(p => p.Id == 1),
                s => s.Pocketings.Count == 0 ? "no balls pocketed" : $"pocketed: {string.Join(", ", s.Pocketings)}")
            .Expect("Cue ball was NOT pocketed",
                s => !s.Pocketings.Exists(p => p.Id == 0))
            .Finish();
    }
}
