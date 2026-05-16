using System.Numerics;
using LongShot.Rules;
using LongShot.Shot;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Player 1 fires the cue ball directly into the top-right corner pocket. Even with no
/// object-ball contact this is a foul ("cue ball pocketed (scratch)"). The rules layer
/// should:
///   - flag the foul
///   - end Player 1's turn
///   - hand ball-in-hand to Player 2
/// </summary>
public static class Rules8BallScratch
{
    public static ScenarioResult Run()
    {
        var pocket = new Vector3(0.624f, 0, 1.124f);
        var cuePos = new Vector3(0, R, -0.30f);
        var aim = Vector3.Normalize(pocket - cuePos);

        var rules = new EightBallRules();

        var scenario = new Scenario("rules_8ball_scratch",
                "Cue ball fired directly into a corner pocket. Scratch foul, ball-in-hand to P2.")
            .PlaceCue(cuePos);

        rules.OnShotStart(new ShotContext { ShotNumber = 1, StateAtStart = scenario.Engine.SnapshotState() });

        scenario.Strike(force: 1.0f, aim: aim).RunFor(2.5f);

        var summary = scenario.Recorder.Finalize();
        rules.OnShotEnd(summary);
        var shot = rules.LastShot;

        return scenario
            .Expect("Cue ball was pocketed",
                _ => summary.CueBallPocketed,
                _ => $"cuePocketed={summary.CueBallPocketed}")
            .Expect("Foul flagged with scratch reason",
                _ => shot.Foul && shot.FoulReason.Contains("scratch"),
                _ => $"foul={shot.Foul}, reason='{shot.FoulReason}'")
            .Expect("Player 2 has ball-in-hand",
                _ => shot.BallInHand && shot.TurnChanged && rules.CurrentPlayer == 2)
            .Finish();
    }
}
