using System.Numerics;
using static LongShot.Bench.Scenario;

namespace LongShot.Bench.Scenarios;

/// <summary>
/// 30° cut with no English. The "tangent line rule" predicts the cue ball leaves the contact
/// along a tangent to the object ball; the object ball departs along the line of centres.
/// </summary>
public static class StunCut
{
    public static ScenarioResult Run()
    {
        // Cut angle ~30° to the right. Object ball is offset 1*R to the right at the same Z forward range.
        var cuePos = new Vector3(0, R, -0.4f);
        var objectPos = new Vector3(R, R, 0.4f);   // contact line tilts ~14° from straight forward
        return new Scenario("stun_cut_30",
                "Off-centre hit with no English. Cue and object should split apart in different directions.")
            .PlaceCue(cuePos)
            .PlaceObjectBall(objectPos)
            .Strike(force: 0.5f, aim: Forward)
            .RunUntilRest()
            .Expect("Object ball deflects to the right",
                s => s.ObjectTrajectory().FinalPosition.X > objectPos.X + 0.05f,
                s => $"object final X = {s.ObjectTrajectory().FinalPosition.X:0.000} (started at {objectPos.X:0.000})")
            .Expect("Cue ball deflects to the left of the aim line",
                s => s.CueTrajectory.FinalPosition.X < cuePos.X - 0.02f,
                s => $"cue final X = {s.CueTrajectory.FinalPosition.X:0.000} (started at {cuePos.X:0.000})")
            .Expect("Cue and object split at roughly perpendicular angles",
                s =>
                {
                    var cueDelta = s.CueTrajectory.FinalPosition - cuePos;
                    var objDelta = s.ObjectTrajectory().FinalPosition - objectPos;
                    var cueXz = new Vector2(cueDelta.X, cueDelta.Z);
                    var objXz = new Vector2(objDelta.X, objDelta.Z);
                    if (cueXz.LengthSquared() < 1e-3f || objXz.LengthSquared() < 1e-3f) return false;
                    float cos = Vector2.Dot(Vector2.Normalize(cueXz), Vector2.Normalize(objXz));
                    return cos < 0.8f;
                },
                s =>
                {
                    var cueDelta = s.CueTrajectory.FinalPosition - cuePos;
                    var objDelta = s.ObjectTrajectory().FinalPosition - objectPos;
                    var cueXz = Vector2.Normalize(new Vector2(cueDelta.X, cueDelta.Z));
                    var objXz = Vector2.Normalize(new Vector2(objDelta.X, objDelta.Z));
                    float ang = System.MathF.Acos(System.Math.Clamp(Vector2.Dot(cueXz, objXz), -1f, 1f)) * (180f / System.MathF.PI);
                    return $"split angle = {ang:0.0}°";
                })
            .Finish();
    }
}
