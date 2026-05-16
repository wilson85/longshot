using System.Numerics;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Three balls in a row, the last two touching (Newton's cradle style). The cue strikes
/// the first object; energy should transfer through the chain so the far ball flies away
/// while the middle ball barely moves.
///
/// Real-world reference: a frozen-ball chain is one of the cleanest physics demonstrations
/// in billiards - the middle ball stays almost stationary as energy passes through it.
/// </summary>
public static class MultiBallChain
{
    public static ScenarioResult Run()
    {
        var cuePos = new Vector3(0, R, -0.5f);
        var ballAPos = new Vector3(0, R, 0.0f);
        var ballBPos = new Vector3(0, R, 0.058f);   // 2*R apart - frozen against A

        return new Scenario("multi_ball_chain",
                "Cue strikes A, A is frozen against B. Energy passes through A to B (Newton's cradle).")
            .PlaceCue(cuePos)
            .PlaceObjectBall(ballAPos)
            .PlaceObjectBall(ballBPos)
            .Strike(force: 0.5f, aim: Forward)
            .RunFor(0.5f)
            .Expect("Ball B flew forward at least 0.3 m (received transferred energy)",
                s => s.Trajectory(2).FinalPosition.Z > ballBPos.Z + 0.3f,
                s => $"B Z travel = {s.Trajectory(2).FinalPosition.Z - ballBPos.Z:0.000} m")
            .Expect("Ball A barely moved (transferred energy through)",
                s => System.MathF.Abs(s.Trajectory(1).FinalPosition.Z - ballAPos.Z) < 0.10f,
                s => $"A Z displacement = {s.Trajectory(1).FinalPosition.Z - ballAPos.Z:0.000} m")
            .Expect("Cue ball stopped near A's start (gave its energy away)",
                s => System.MathF.Abs(s.CueTrajectory.FinalPosition.Z - ballAPos.Z) < 0.15f,
                s => $"cue final Z = {s.CueTrajectory.FinalPosition.Z:0.000}, A start = {ballAPos.Z:0.000}")
            .Finish();
    }
}
