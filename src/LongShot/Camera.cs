using System.Numerics;

namespace LongShot;

public class Camera
{
    public Vector3 Position;
    public Vector3 Target;
    public Vector3 Up = Vector3.UnitY;
    public float Yaw = 0f;
    public float Pitch = 0.5f;
    public float Distance = 1.5f;

    public void Update(InputState input, GameStateMode mode, Vector3 cueBallPos, float deltaTime)
    {
        if (mode != GameStateMode.Simulate)
        {
            Target = (mode == GameStateMode.View) ? Vector3.Zero : cueBallPos;
        }

        Up = Vector3.UnitY;

        // Handle Aiming (No mouse button required, Shift for fine adjust)
        if (mode == GameStateMode.Aim && !input.Keys[(int)ConsoleKey.E])
        {
            float sensitivity = input.Keys[16] 
                ? GameSettings.MouseSensitivityAimFine 
                : GameSettings.MouseSensitivityAim;

            Yaw -= input.MouseDeltaX * sensitivity;
            Pitch += input.MouseDeltaY * sensitivity;
        }
        else if ((mode == GameStateMode.View || mode == GameStateMode.Simulate) && input.IsRightMouseDown)
        {
            Yaw -= input.MouseDeltaX * GameSettings.MouseSensitivityView;
            Pitch += input.MouseDeltaY * GameSettings.MouseSensitivityView;
        }



        if (input.MouseWheelDelta != 0)
        {
            Distance -= (input.MouseWheelDelta / 120.0f) * 0.25f;
        }

        Pitch = Math.Clamp(Pitch, 0.1f, MathF.PI / 2.0f - 0.05f);

        // Allow zooming in to 20cm away from the target in ANY mode
        float minDistance = 0.2f;
        Distance = Math.Clamp(Distance, minDistance, 6.0f);

        Position = Target + new Vector3(
            Distance * MathF.Cos(Pitch) * MathF.Sin(Yaw),
            Distance * MathF.Sin(Pitch),
            Distance * MathF.Cos(Pitch) * MathF.Cos(Yaw)
        );

        if (input.Keys[(int)ConsoleKey.O])
        {
            Position = new Vector3(0, 4.0f, 0);
            Target = Vector3.Zero;
            Up = new Vector3(0, 0, 1);
        }
    }

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Up);
    public Matrix4x4 ProjectionMatrix => Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, 1280f / 720f, 0.01f, 100f);
}