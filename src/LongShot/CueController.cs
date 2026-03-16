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

    // The Cue now owns its own orientation independent of the Camera
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }

    public void ForceCueOffset(float offset) => _power.ForceCueOffset(offset);

    public void UpdateAim(InputState input, Camera camera)
    {
        if (input.Keys[(int)ConsoleKey.E])
        {
            // Adjust English
            float speed = 0.0005f;
            var offset = TipOffset;

            offset.X -= input.MouseDeltaX * speed;
            offset.Y -= input.MouseDeltaY * speed;

            float maxRadius = 0.6f;
            if (offset.LengthSquared() > maxRadius * maxRadius)
            {
                offset = Vector2.Normalize(offset) * maxRadius;
            }

            TipOffset = offset;
        }
        else if (input.Keys[(int)ConsoleKey.B])
        {
            // Adjust Cue Elevation / Pitch
            float speed = 0.005f;
            Pitch += input.MouseDeltaY * speed;

            // Clamp pitch between flat (0) and almost vertical (masse)
            Pitch = Math.Clamp(Pitch, 0f, MathF.PI / 2.2f);
        }
        else
        {
            // Normal aiming: Sync the cue's yaw with the camera
            Yaw = camera.Yaw;
        }
    }

    public ShotResult UpdateStroke(InputState input, float dt)
        => _power.UpdateStroke(input, dt);

    public Shot BuildShot()
    {
        // Pass the cue's internal angles down to the shot builder
        var shot = _power.BuildShot(Yaw, Pitch, TipOffset);

        // Reset the shot parameters for the next turn
        TipOffset = Vector2.Zero;
        Pitch = 0f;

        return shot;
    }

    public void BeginStroke()
    {
        _power.BeginStroke();
    }
}