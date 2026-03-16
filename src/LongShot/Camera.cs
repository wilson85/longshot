using System.Numerics;

namespace LongShot;

public class Camera
{
    public Vector3 Position;
    public Vector3 Target;
    public Vector3 Up = Vector3.UnitY;
    public float Yaw = 0f;

    // We track the actual Pitch and Vertical Offset to smooth them over time
    public float Pitch = GameSettings.PlayerViewPitch;
    private float _currentVerticalOffset = GameSettings.CameraTargetVerticalOffset;

    public float Distance = GameSettings.CameraDefaultDistance;

    private Vector3 _lockedTargetPos;
    private bool _isLocked = false;

    public void Update(InputState input, GameStateMode mode, Vector3 cueBallPos, float deltaTime)
    {
        // 1. Calculate Target Values based on Mode
        Vector3 baseTargetPos;

        if (mode == GameStateMode.View)
        {
            baseTargetPos = Vector3.Zero;
            _isLocked = false;
        }
        else if (mode == GameStateMode.Simulate)
        {
            // Lock the camera target to where the cue ball WAS when we struck it
            if (!_isLocked)
            {
                _lockedTargetPos = cueBallPos;
                _isLocked = true;
            }
            baseTargetPos = _lockedTargetPos;
        }
        else // Aim or Power mode
        {
            baseTargetPos = cueBallPos;
            _isLocked = false;
        }

        float targetPitch = Pitch; // Default to current
        float targetVerticalOffset = GameSettings.CameraTargetVerticalOffset;

        // FIX: Include GameStateMode.Power so the camera stays down when you go to shoot!
        if (mode == GameStateMode.Aim || mode == GameStateMode.Power)
        {
            targetPitch = GameSettings.PlayerViewPitch;
            targetVerticalOffset = GameSettings.PlayerViewVerticalOffset;
        }
        else if (mode == GameStateMode.Simulate)
        {
            // NEW: Keep the camera low to the table after the shot is fired!
            targetVerticalOffset = GameSettings.PlayerViewVerticalOffset;
            // Note: We don't force targetPitch here so you can look around while balls roll
        }

        // 2. Smoothly Interpolate (Lerp) values to avoid jarring jumps
        // Lower 5.0f for slower transition, higher for faster.
        float transitionSpeed = 5.0f;
        float lerpFactor = 1.0f - MathF.Exp(-transitionSpeed * deltaTime);

        // We only force the pitch to the 'PlayerViewPitch' if we aren't manually rotating
        if (mode == GameStateMode.Aim || mode == GameStateMode.Power)
        {
            Pitch = MathHelper.Lerp(Pitch, targetPitch, lerpFactor);
        }

        _currentVerticalOffset = MathHelper.Lerp(_currentVerticalOffset, targetVerticalOffset, lerpFactor);
        Target = baseTargetPos + new Vector3(0, _currentVerticalOffset, 0);

        Up = Vector3.UnitY;

        // 3. Handle Rotation (Manual overrides)
        float baseSensitivity = GameSettings.MasterSensitivity;

        if (mode == GameStateMode.Aim && !input.Keys[(int)ConsoleKey.E])
        {
            float sensitivity = input.Keys[16]
                ? baseSensitivity * GameSettings.FineModifier
                : baseSensitivity;

            Yaw -= input.MouseDeltaX * sensitivity;

            // Allow subtle pitch adjustment even in aim mode if desired
            // Pitch += input.MouseDeltaY * sensitivity;
        }
        else if ((mode == GameStateMode.View || mode == GameStateMode.Simulate) && input.IsRightMouseDown)
        {
            float sensitivity = baseSensitivity * GameSettings.ViewModifier;
            Yaw -= input.MouseDeltaX * sensitivity;
            Pitch += input.MouseDeltaY * sensitivity;
        }

        // 4. Handle Zoom
        if (input.MouseWheelDelta != 0)
        {
            Distance -= (input.MouseWheelDelta / 120.0f) * 0.25f;
        }

        // 5. Constraints
        Pitch = Math.Clamp(Pitch, GameSettings.CameraMinPitch, GameSettings.CameraMaxPitch);
        Distance = Math.Clamp(Distance, GameSettings.CameraMinDistance, GameSettings.CameraMaxDistance);

        // 6. Calculate Final Position
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

    public Matrix4x4 ProjectionMatrix => Matrix4x4.CreatePerspectiveFieldOfView(
        GameSettings.CameraFieldOfView,
        1280f / 720f,
        0.01f,
        100f);
}

// Helper for Lerp if not available in your namespace
public static class MathHelper
{
    public static float Lerp(float start, float end, float amount) => start + (end - start) * amount;
}