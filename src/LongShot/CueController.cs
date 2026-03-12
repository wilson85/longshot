using System;
using System.Numerics;

namespace LongShot;

public sealed class CueController
{
    readonly ShotPowerManager _power = new();
    public float PreviousCueOffset => _power.PreviousCueStickOffset;
    public bool HasPulledBack => _power.HasPulledBack;
    public Vector2 TipOffset { get; private set; }
    public float CueOffset => _power.CueStickOffset;

    public void UpdateAim(InputState input)
    {
        // ONLY move the tip offset if the player is holding E
        if (input.Keys[(int)ConsoleKey.E])
        {
            // 1. FIXED SPEED: Lowered from 0.003f for finer control
            float speed = 0.0005f;
            var offset = TipOffset;

            // 2. FIXED INVERSION: Subtracted DeltaX to flip left/right
            offset.X -= input.MouseDeltaX * speed;
            offset.Y -= input.MouseDeltaY * speed;

            // 3. FIXED CLAMPING: Use a circular clamp instead of a square clamp!
            // This prevents the tip from going into the corners past the edge of the ball.
            // 0.85f means the max english is 85% of the way to the edge (prevents miscueing).
            float maxRadius = 0.6f;
            if (offset.LengthSquared() > maxRadius * maxRadius)
            {
                offset = Vector2.Normalize(offset) * maxRadius;
            }

            TipOffset = offset;
        }
    }

    public ShotResult UpdateStroke(InputState input, float dt)
        => _power.UpdateStroke(input, dt);

    public Shot BuildShot(Camera camera)
    {
        var shot = _power.BuildShot(camera, TipOffset);

        // Reset the spin back to dead-center after the strike!
        TipOffset = Vector2.Zero;

        return shot;
    }

    public void BeginStroke()
    {
        _power.BeginStroke();
    }
}