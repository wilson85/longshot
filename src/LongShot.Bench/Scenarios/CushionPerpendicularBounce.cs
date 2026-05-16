using System.Numerics;
using LongShot.Engine;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// Measures the cushion coefficient of restitution for a perpendicular head-on bounce.
/// The cue ball is struck directly into the head rail with no English. We compare the
/// speed in the rail-normal direction just before and just after impact.
///
/// Real-world reference: tournament-grade rubber cushions return 70-85% of incident speed
/// at typical playing speeds (Dr. Dave; Wayland/Marlow). Energy retention is 0.49-0.72.
/// Our <see cref="LongShot.Engine.CushionParameters.MaxRestitution"/> = 0.75 sits near the
/// middle of this band, so the measurement should land near 0.75 - some loss due to the
/// dynamic-restitution speed decay at higher impact speeds.
/// </summary>
public static class CushionPerpendicularBounce
{
    private const float ExpectedCorMin = 0.55f;
    private const float ExpectedCorMax = 0.85f;

    public static ScenarioResult Run()
    {
        var scenario = new Scenario("cushion_perp_bounce",
                "Perpendicular bounce off the head rail. Measures cushion coefficient of restitution.")
            .PlaceCue(new Vector3(0, R, 0.5f))
            .Strike(force: 0.5f, aim: -Forward)
            .RunFor(2.0f);

        // Find the bounce: cue starts moving -Z, ends up moving +Z after the head-rail impact.
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

        // Speed in the rail-normal direction immediately before and after the bounce.
        // (Cloth friction acts during the half-step after the bounce, so the post-speed is a
        // slight underestimate of the true post-impulse speed - good enough for tuning.)
        float speedBefore = System.MathF.Abs(samples[bounceIdx.Value - 1].LinearVelocity.Z);
        float speedAfter = System.MathF.Abs(samples[bounceIdx.Value].LinearVelocity.Z);
        float cor = speedAfter / speedBefore;

        return scenario
            .Expect($"Cushion COR within real-world range [{ExpectedCorMin:0.00}, {ExpectedCorMax:0.00}]",
                _ => cor >= ExpectedCorMin && cor <= ExpectedCorMax,
                _ => $"measured COR = {cor:0.000}  ({speedAfter:0.000} / {speedBefore:0.000} m/s)")
            .Expect("Cue ball rebounded (Z velocity flipped)",
                _ => true,
                _ => $"bounce at sample {bounceIdx.Value} (t={samples[bounceIdx.Value].Time:0.000}s)")
            .Finish();
    }
}
