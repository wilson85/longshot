using System.Numerics;
using LongShot.Engine;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Measures angle of reflection at 45° incidence into the head rail (no English).
/// Real-world reference: with no spin, a 45° incidence rebounds at ~45° (slight steepening
/// of a few degrees due to rail-induced friction). Strong deviations from mirror reflection
/// indicate the cushion friction tuning is off.
/// </summary>
public static class CushionAngleReflection
{
    private const float IncidenceAngleDeg = 45f;
    private const float ExpectedReflectionDegMin = 35f;
    private const float ExpectedReflectionDegMax = 55f;

    public static ScenarioResult Run()
    {
        // Aim 45° from the rail normal: equal -Z and -X components so the cue heads
        // toward the lower-left of the table, striking the head rail at 45°.
        float aimRad = (180f + IncidenceAngleDeg) * System.MathF.PI / 180f; // 225° from +X axis
        var aim = new Vector3(System.MathF.Sin(aimRad), 0, System.MathF.Cos(aimRad));

        var scenario = new Scenario("cushion_angle_45",
                $"45° angle of incidence into head rail. Real rebound is ~45° ± slight steepening.")
            .PlaceCue(new Vector3(0.3f, R, 0.0f))
            .Strike(force: 0.7f, aim: aim)
            .RunFor(1.5f);

        // Find the head-rail bounce (Z velocity flips from negative to positive).
        var samples = scenario.CueTrajectory.Samples;
        int? bounceIdx = null;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i - 1].LinearVelocity.Z < -0.05f && samples[i].LinearVelocity.Z > 0.05f)
            {
                bounceIdx = i;
                break;
            }
        }

        if (bounceIdx is null)
        {
            return scenario.Expect("Rail bounce occurred", _ => false, _ => "no Z-velocity sign flip found").Finish();
        }

        // Incoming heading (just before bounce) and outgoing heading (just after).
        var vIn = samples[bounceIdx.Value - 1].LinearVelocity;
        var vOut = samples[bounceIdx.Value].LinearVelocity;

        // Angle from the rail normal (which points +Z for the head rail). vIn.Z is negative, vOut.Z positive.
        float angleInDeg = System.MathF.Abs(System.MathF.Atan2(vIn.X, -vIn.Z)) * (180f / System.MathF.PI);
        float angleOutDeg = System.MathF.Abs(System.MathF.Atan2(vOut.X, vOut.Z)) * (180f / System.MathF.PI);

        return scenario
            .Expect($"Reflection angle within real-world range [{ExpectedReflectionDegMin:0.0}°, {ExpectedReflectionDegMax:0.0}°]",
                _ => angleOutDeg >= ExpectedReflectionDegMin && angleOutDeg <= ExpectedReflectionDegMax,
                _ => $"incidence {angleInDeg:0.0}° -> reflection {angleOutDeg:0.0}°  (real ≈ 45°)")
            .Expect("Incidence angle landed near 45°",
                _ => System.MathF.Abs(angleInDeg - 45f) < 8f,
                _ => $"actual incidence = {angleInDeg:0.0}° (target 45°)")
            .Finish();
    }
}
