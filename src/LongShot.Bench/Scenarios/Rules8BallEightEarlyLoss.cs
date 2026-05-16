using System.Numerics;
using LongShot.Rules;
using LongShot.Shot;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Mid-game: P1 has solids and still has at least one solid on the table. P1 pots the
/// 8-ball into a corner — instant loss. The rules layer should:
///   - mark <c>GameLost</c> for the shooting player
///   - finalise the game with Player 2 as the winner
/// </summary>
public static class Rules8BallEightEarlyLoss
{
    public static ScenarioResult Run()
    {
        // We want: cue at head spot, one solid (id 1) sitting harmlessly off to the side,
        // 8-ball (id 8) lined up with the top-right corner pocket.
        var pocket = new Vector3(0.624f, 0, 1.124f);
        var eightStart = new Vector3(0.42f, R, 0.82f);
        var objToPocket = Vector3.Normalize(pocket - eightStart);
        var ghost = eightStart - (objToPocket * 2f * R);
        var cuePos = new Vector3(0, R, -0.30f);
        var aim = Vector3.Normalize(ghost - cuePos);

        // Place a benign solid out of the way so P1 still has own-group balls remaining.
        var idleSolid = new Vector3(-0.40f, R, -0.80f);

        var rules = new EightBallRules();
        rules.SeedAssignedGroups(player: 1, group: BallGroup.Solid);

        var scenario = new Scenario("rules_8ball_eight_early_loss",
                "P1 = Solid with a solid still on table; P1 pots the 8 → game lost.")
            .PlaceCue(cuePos)
            .PlaceObjectBall(idleSolid)              // engine id 1 → solid
            .PadBallIdsTo(8)
            .PlaceObjectBall(eightStart);            // engine id 8 → eight

        rules.OnShotStart(new ShotContext { ShotNumber = 1, StateAtStart = scenario.Engine.SnapshotState() });

        scenario.Strike(force: 0.8f, aim: aim).RunFor(3.0f);

        var summary = scenario.Recorder.Finalize();
        rules.OnShotEnd(summary);
        var shot = rules.LastShot;

        return scenario
            .Expect("8-ball was pocketed",
                _ => shot.PocketedBalls.Contains(8),
                _ => $"pocketed: [{string.Join(",", shot.PocketedBalls)}]")
            .Expect("Game ended in a loss for the shooter (P1)",
                _ => shot.GameLost && rules.GameOver && rules.Winner == 2,
                _ => $"gameLost={shot.GameLost}, gameOver={rules.GameOver}, winner={rules.Winner}, reason='{rules.WinReason}'")
            .Finish();
    }
}
