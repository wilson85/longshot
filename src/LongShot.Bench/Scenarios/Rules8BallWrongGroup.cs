using System.Numerics;
using LongShot.Rules;
using LongShot.Shot;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Mid-game scenario: groups are already assigned, Player 1 has solids. Player 1's cue
/// ball first contacts a STRIPE (engine id 9-15) — illegal. The rules layer should:
///   - flag the foul ("opponent's group ball first")
///   - end Player 1's turn
///   - hand ball-in-hand to Player 2
/// </summary>
public static class Rules8BallWrongGroup
{
    public static ScenarioResult Run()
    {
        // Set engine ids so the visible object ball gets id 9 (a stripe).
        // Cue=0, then PadBallIdsTo(9) reserves ids 1..8, then PlaceObjectBall → id 9.
        var cuePos = new Vector3(0, R, -0.30f);
        var stripePos = new Vector3(0, R, 0.30f);   // directly ahead of cue
        var aim = Vector3.Normalize(stripePos - cuePos);

        var rules = new EightBallRules();
        rules.SeedAssignedGroups(player: 1, group: BallGroup.Solid);   // mid-game

        var scenario = new Scenario("rules_8ball_wrong_group",
                "Mid-game: P1 = Solid. Cue contacts a stripe first. Foul, ball-in-hand to P2.")
            .PlaceCue(cuePos)
            .PadBallIdsTo(9)
            .PlaceObjectBall(stripePos);             // engine id 9 → stripe

        rules.OnShotStart(new ShotContext { ShotNumber = 1, StateAtStart = scenario.Engine.SnapshotState() });

        scenario.Strike(force: 0.5f, aim: aim).RunFor(1.5f);

        var summary = scenario.Recorder.Finalize();
        rules.OnShotEnd(summary);
        var shot = rules.LastShot;

        return scenario
            .Expect("Cue first contact was a stripe (id 9)",
                _ => summary.FirstContactBallId == 9,
                _ => $"first contact ball id = {summary.FirstContactBallId}")
            .Expect("Foul flagged for opposing-group contact",
                _ => shot.Foul && shot.FoulReason.Contains("opponent"),
                _ => $"foul={shot.Foul}, reason='{shot.FoulReason}'")
            .Expect("Player 2 has ball-in-hand and the turn",
                _ => shot.BallInHand && shot.TurnChanged && rules.CurrentPlayer == 2)
            .Finish();
    }
}
