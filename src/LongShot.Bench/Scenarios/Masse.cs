using System.Numerics;
using LongShot.Engine;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Massé shot: steep cue elevation + extreme side English. The cue ball jumps briefly,
/// lands carrying strong Y-axis spin, then curves dramatically while sliding (Magnus
/// effect via <see cref="LongShot.Engine.ClothParameters.SwerveFactor"/>).
///
/// Real-world reference: a massé curves through 30-90° of horizontal angle across half a
/// table length. This is the show-off shot - the engine should at least produce a
/// pronounced curve, not a near-straight line.
/// </summary>
public static class Masse
{
    public static ScenarioResult Run()
    {
        // 70° cue elevation, maximum legal right English.
        const float ElevationDeg = 70f;
        float elevRad = ElevationDeg * System.MathF.PI / 180f;
        var aim = new Vector3(0, -System.MathF.Sin(elevRad), System.MathF.Cos(elevRad));

        return new Scenario("masse",
                $"{ElevationDeg:0}° cue elevation + max right English. Brief airborne, then sharp curve.")
            .PlaceCue(new Vector3(-0.3f, R, -0.9f))
            .Strike(force: 1.0f, aim: aim, offset: new Vector3(0.55f * R, 0, 0))
            .RunFor(3.0f)
            .Expect("Cue ball went airborne",
                s =>
                {
                    foreach (var sm in s.CueTrajectory.Samples)
                    {
                        if (sm.State == MotionState.Airborne) return true;
                    }
                    return false;
                })
            .Expect("Cue ball curved significantly (>0.10 m of X drift from start)",
                s =>
                {
                    float maxX = float.MinValue, minX = float.MaxValue;
                    foreach (var sm in s.CueTrajectory.Samples)
                    {
                        if (sm.Position.X > maxX) maxX = sm.Position.X;
                        if (sm.Position.X < minX) minX = sm.Position.X;
                    }
                    return (maxX - minX) > 0.10f;
                },
                s =>
                {
                    float maxX = float.MinValue, minX = float.MaxValue;
                    foreach (var sm in s.CueTrajectory.Samples)
                    {
                        if (sm.Position.X > maxX) maxX = sm.Position.X;
                        if (sm.Position.X < minX) minX = sm.Position.X;
                    }
                    return $"X range = {maxX - minX:0.000} m (min {minX:0.000}, max {maxX:0.000})";
                })
            .Finish();
    }
}
