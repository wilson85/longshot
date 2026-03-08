using System.Numerics;

namespace LongShot;

public static class CuePoseSolver
{
    public static Matrix4x4 Solve(
        Vector3 cueBall,
        float yaw,
        float cueOffset)
    {
        var rot = Matrix4x4.CreateRotationY(yaw);

        // 1. Subtract the offset (since cueOffset is negative when pulling back).
        // 2. Use ~0.53f instead of 0.5f so the stick rests on the *outside* //    surface of the ball (assuming a ball radius of ~0.03f).
        float visualOffset = 0.53f - cueOffset;

        var offset = Vector3.Transform(
            new Vector3(0, 0, visualOffset),
            rot);

        var pos = cueBall + offset;

        return
            Matrix4x4.CreateScale(0.015f, 0.015f, 1.0f) *
            rot *
            Matrix4x4.CreateTranslation(pos);
    }
}