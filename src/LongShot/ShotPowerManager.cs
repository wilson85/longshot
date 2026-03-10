using System;
using System.Numerics;

namespace LongShot;

public sealed class ShotPowerManager
{
    float _peakForwardVelocity;
    bool _hasPulledBack;

    public float CueStickOffset { get; private set; }

    public void Reset()
    {
        CueStickOffset = 0f;
        _peakForwardVelocity = 0f;
        _hasPulledBack = false;
    }

    public void BeginStroke()
    {
        CueStickOffset = 0f;
        _peakForwardVelocity = 0f;
        _hasPulledBack = false;
    }

    public ShotResult UpdateStroke(InputState input, float dt)
    {
        float movement = -input.MouseDeltaY * GameSettings.MouseSensitivity;
        float currentVelocity = movement / Math.Max(dt, 0.0001f);

        if (currentVelocity > 0)
        {
            _peakForwardVelocity = Math.Max(_peakForwardVelocity, currentVelocity);
        }
        else if (currentVelocity < -0.001f)
        {
            _peakForwardVelocity = 0;
        }

        float previousOffset = CueStickOffset;
        CueStickOffset += movement;

        float maxForwardAllowed = _hasPulledBack ? 0.1f : 0f;
        CueStickOffset = Math.Clamp(CueStickOffset, GameSettings.MaxPullback, maxForwardAllowed);

        if (CueStickOffset < -0.05f)
        {
            _hasPulledBack = true;
        }

        bool struck = _hasPulledBack && previousOffset < 0f && CueStickOffset >= 0f;

        if (struck)
        {
            CueStickOffset = 0f;
            return ShotResult.Strike;
        }

        // 27 is Escape
        if (input.IsKeyPressed(27))
        {
            Reset();
            return ShotResult.Cancel;
        }

        return ShotResult.None;
    }

    // UPDATED: Now accepts the tipOffset from the CueController!
    public Shot BuildShot(Camera camera, Vector2 tipOffset)
    {
        float power = CalculateImpactSpeed();

        Vector3 dir = Vector3.Normalize(
            new Vector3(
                -MathF.Sin(camera.Yaw),
                0,
                -MathF.Cos(camera.Yaw)));

        var shot = new Shot(dir, power, tipOffset);

        Reset();

        return shot;
    }

    float CalculateImpactSpeed()
    {
        float velocityToPowerMultiplier = 1.5f;
        float finalPower = _peakForwardVelocity * velocityToPowerMultiplier;
        return Math.Clamp(finalPower, 0.1f, GameSettings.MaxImpactSpeed);
    }
}