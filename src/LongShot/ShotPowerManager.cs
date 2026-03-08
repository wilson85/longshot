using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace LongShot;

public sealed class ShotPowerManager
{
    // Reduced from 5 to 3. 5 frames was blending too much of the slow 
    // acceleration phase, making the shot feel delayed and weak!
    const int SmoothingFrames = 3;

    readonly Queue<float> _velocityBuffer = new();

    public void Reset()
    {
        _velocityBuffer.Clear();
    }

    public void TrackVelocity(float stickVelocity)
    {
        // Only track forward moving velocity to calculate impact power
        if (stickVelocity > 0)
        {
            _velocityBuffer.Enqueue(stickVelocity);

            if (_velocityBuffer.Count > SmoothingFrames)
            {
                _velocityBuffer.Dequeue();
            }
        }
        else if (stickVelocity < -0.5f)
        {
            // If they pull back forcefully, clear the forward momentum buffer
            _velocityBuffer.Clear();
        }
    }

    public float CalculateImpactSpeed()
    {
        if (_velocityBuffer.Count == 0) return 0f;

        // Shooterspool trick: Use the AVERAGE of the last few frames before impact,
        // rather than the peak. This prevents a single 1000Hz USB mouse polling spike 
        // from launching the ball at lightspeed.
        float totalVelocity = 0f;
        foreach (var v in _velocityBuffer)
        {
            totalVelocity += v;
        }

        float averageVelocity = totalVelocity / _velocityBuffer.Count;

        // Add a tuning multiplier. 1.0 means true 1:1 physical speed. 
        // Increase if the mouse feels too heavy to get a good break shot.
        float finalImpactSpeed = averageVelocity * 1.2f;

        return Math.Clamp(finalImpactSpeed, 0f, 10f);
    }

    public void DrawDebug()
    {
        ImGui.Text("--- Shot Power Manager ---");

        float currentV = 0f;
        if (_velocityBuffer.Count > 0)
        {
            var arr = _velocityBuffer.ToArray();
            currentV = arr[^1];
        }

        ImGui.Text($"Current Forward Velocity: {currentV:F3} m/s");
        ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Calculated Impact: {CalculateImpactSpeed():F3} m/s");
    }
}