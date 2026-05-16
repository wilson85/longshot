using System.Numerics;
using LongShot.Rules;
using LongShot.Shot;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Open-table 8-ball pot. Player 1 strikes a solid ball into the corner pocket. The
/// rules layer should:
///   - mark the shot as legal (no foul)
///   - assign Player 1 = Solid, Player 2 = Stripe
///   - keep Player 1 on the table (continue shooting)
/// </summary>
public static class Rules8BallLegalPot
{
    public static ScenarioResult Run()
    {
        // Standard ghost-ball geometry for the top-right corner pocket.
        var pocket = new Vector3(0.624f, 0, 1.124f);
        var solidStart = new Vector3(0.42f, R, 0.82f);    // engine id 1, ball "1" (solid)
        var objToPocket = Vector3.Normalize(pocket - solidStart);
        var ghost = solidStart - (objToPocket * 2f * R);
        var cuePos = new Vector3(0, R, -0.30f);
        var aim = Vector3.Normalize(ghost - cuePos);

        var rules = new EightBallRules();

        var scenario = new Scenario("rules_8ball_legal_pot",
                "Open-table pot of a solid. Rules should assign P1 = Solid and continue P1's turn.")
            .PlaceCue(cuePos)
            .PlaceObjectBall(solidStart);   // engine id 1 → solid

        // Notify rules observer about shot start, then take the shot.
        rules.OnShotStart(new ShotContext { ShotNumber = 1, StateAtStart = scenario.Engine.SnapshotState() });

        scenario.Strike(force: 0.8f, aim: aim).RunFor(2.5f);

        var summary = scenario.Recorder.Finalize();
        rules.OnShotEnd(summary);
        var shot = rules.LastShot;

        return scenario
            .Expect("Solid ball 1 was pocketed",
                _ => shot.PocketedBalls.Contains(1),
                _ => $"pocketed: [{string.Join(",", shot.PocketedBalls)}]")
            .Expect("No foul",
                _ => !shot.Foul,
                _ => shot.FoulReason)
            .Expect("Group assigned: Player 1 = Solid, Player 2 = Stripe",
                _ => rules.Player1Group == BallGroup.Solid && rules.Player2Group == BallGroup.Stripe,
                _ => $"P1 = {rules.Player1Group}, P2 = {rules.Player2Group}")
            .Expect("Player 1 keeps shooting",
                _ => !shot.TurnChanged && rules.CurrentPlayer == 1,
                _ => $"turnChanged={shot.TurnChanged}, currentPlayer={rules.CurrentPlayer}")
            .Finish();
    }
}
