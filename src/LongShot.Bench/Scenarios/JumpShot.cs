using System.Numerics;
using LongShot.Engine;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Jump shot: the cue is elevated so the strike has a downward component. The slate
/// catches the ball and rebounds it upward, sending it on a ballistic arc that clears
/// an obstacle ball blocking the direct line.
///
/// Real-world reference: a 45° elevated cue at medium force jumps the cue ball about
/// 4-7 cm vertically - enough to clear a ball directly in its path. Pros can clear two
/// balls at steeper elevations. We're aiming for the easy case here.
///
/// Mechanics in the engine:
///   1. <see cref="BilliardsEngine.StrikeCueBall"/> applies an impulse along the elevated
///      aim direction (negative Y component).
///   2. The next fixed step drives the ball below slate level.
///   3. <see cref="TablePhysics.UpdateBallMotion"/> clamps Y back to BallRadius and
///      reflects vel.Y by <c>Env.SlateRestitution</c> - the ball is now travelling +Y.
///   4. Position.Y &gt; BallRadius+1mm flips the state to <c>MotionState.Airborne</c>;
///      cloth friction is skipped while airborne so the ball flies clean.
///   5. Gravity decelerates vel.Y until the ball lands and re-engages sliding.
/// </summary>
public static class JumpShot
{
    public static ScenarioResult Run()
    {
        // 45° cue elevation, aiming straight along +Z.
        const float ElevationDeg = 45f;
        float elevRad = ElevationDeg * System.MathF.PI / 180f;
        var aim = new Vector3(0, -System.MathF.Sin(elevRad), System.MathF.Cos(elevRad));

        var cuePos = new Vector3(0, R, -0.4f);
        var obstaclePos = new Vector3(0, R, -0.15f); // 25 cm ahead of cue - directly in the jump's path

        return new Scenario("jump_shot",
                $"{ElevationDeg:0}° elevated cue. Cue ball jumps over an obstacle 25 cm ahead.")
            .PlaceCue(cuePos)
            .PlaceObjectBall(obstaclePos)
            .Strike(force: 0.7f, aim: aim)
            // Short window: capture the jump + landing only. With a longer run the cue
            // ball would bounce off the foot rail and come back, eventually hitting the
            // obstacle. That doesn't invalidate the jump - the test just isolates it.
            .RunFor(0.5f)
            .Expect("Cue ball went airborne at some point",
                s =>
                {
                    foreach (var sm in s.CueTrajectory.Samples)
                    {
                        if (sm.State == MotionState.Airborne) return true;
                    }
                    return false;
                },
                s =>
                {
                    float maxY = float.MinValue;
                    foreach (var sm in s.CueTrajectory.Samples) if (sm.Position.Y > maxY) maxY = sm.Position.Y;
                    return $"peak cue Y = {maxY:0.000} m (slate = {R:0.000})";
                })
            .Expect("Cue ball cleared the obstacle's Z position",
                s => s.CueTrajectory.MaxZ > obstaclePos.Z + 0.05f,
                s => $"cue peak Z = {s.CueTrajectory.MaxZ:0.000}, obstacle Z = {obstaclePos.Z:0.000}")
            .Expect("Obstacle ball barely moved (cue flew OVER, not through)",
                s => Vector3.Distance(s.ObjectTrajectory().FinalPosition, obstaclePos) < 0.10f,
                s => $"obstacle moved {Vector3.Distance(s.ObjectTrajectory().FinalPosition, obstaclePos):0.000} m")
            .Finish();
    }
}
