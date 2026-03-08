using System.Numerics;

namespace LongShot;

public sealed class CueController
{
    readonly ShotPowerManager _power = new();

    Vector2 _tipOffset;

    public float CueOffset => _power.CueStickOffset;

    public void UpdateAim(InputState input)
    {
        float speed = 0.003f;

        _tipOffset.X += input.MouseDeltaX * speed;
        _tipOffset.Y -= input.MouseDeltaY * speed;

        _tipOffset = Vector2.Clamp(_tipOffset, new(-0.9f), new(0.9f));
    }

    public ShotResult UpdateStroke(InputState input, float dt)
        => _power.UpdateStroke(input, dt);

    public Shot BuildShot(Camera camera)
        => _power.BuildShot(camera);

    public void BeginStroke()
    {
        _power.BeginStroke();
    }
}

public static class CuePoseSolver
{
    public static Matrix4x4 Solve(
        Vector3 cueBall,
        float yaw,
        float cueOffset)
    {
        var rot = Matrix4x4.CreateRotationY(yaw);

        float visualOffset = 0.5f + cueOffset;

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

