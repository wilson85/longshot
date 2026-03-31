using System;
using System.Numerics;

namespace LongShot;

public static class CuePoseSolver
{
    public static Matrix4x4 Solve(
        Vector3 cueBall,
        float pitch, // <--- ADDED PITCH!
        float yaw,
        float cueOffset)
    {
        // 1. Rotate the Y-up cylinder 90 degrees on the X-axis so it points along Z
        var meshAlignment = Matrix4x4.CreateRotationX(MathF.PI / 2f);

        // 2. Scale it (Because it's now aligned to Z, Z is length, X/Y are thickness)
        var scale = Matrix4x4.CreateScale(0.015f, 0.015f, 1.0f);

        // 3. Apply the player's aim rotation (Pitch to tilt down to the ball, Yaw to aim)
        var rot = Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(yaw);

        // 4. Calculate the physical distance from the ball
        float visualOffset = 0.53f - cueOffset;

        var offset = Vector3.Transform(
            new Vector3(0, 0, visualOffset),
            rot);

        var pos = cueBall + offset;

        // Multiply them all together in order!
        return meshAlignment * scale * rot * Matrix4x4.CreateTranslation(pos);
    }
}