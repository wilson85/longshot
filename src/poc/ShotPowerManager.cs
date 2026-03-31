using System.Numerics;

namespace LongShot;

public sealed class ShotPowerManager
{
    public bool HasPulledBack { get; private set; }
    public float PreviousCueStickOffset { get; private set; }
    public float CueStickOffset { get; private set; }

    // Tracks the instantaneous speed of the mouse/cue every single frame
    public float CurrentVelocity { get; private set; }

    public void Reset()
    {
        CueStickOffset = 0f;
        PreviousCueStickOffset = 0f;
        HasPulledBack = false;
        CurrentVelocity = 0f;
    }

    public void BeginStroke()
    {
        Reset();
    }

    public void ForceCueOffset(float offset)
    {
        CueStickOffset = offset;
    }

    public ShotResult UpdateStroke(InputState input, float dt)
    {
        float safeDt = Math.Max(dt, 0.0001f);

        // 1:1 Visual and Physical mapping based purely on mouse movement
        float movement = -input.MouseDeltaY * (GameSettings.MasterSensitivity * GameSettings.CueVisualDrive);

        PreviousCueStickOffset = CueStickOffset;
        CueStickOffset += movement;

        // Clamp so you can't pull back infinitely
        CueStickOffset = Math.Max(CueStickOffset, GameSettings.MaxPullback);

        // Record instantaneous velocity (meters per second)
        CurrentVelocity = movement / safeDt;

        if (CueStickOffset < -0.05f)
        {
            HasPulledBack = true;
        }

        if (input.IsKeyPressed(27)) // Escape key
        {
            Reset();
            return ShotResult.Cancel;
        }

        return ShotResult.None;
    }

    public Shot BuildShot(float yaw, float pitch, Vector2 tipOffset)
    {
        float rawImpactVelocity = Math.Max(0f, CurrentVelocity);
        float power = CalculateImpactSpeed(rawImpactVelocity);
        float powerRatio = power / GameSettings.MaxImpactSpeed;

        // Mis-cue instability logic
        if (powerRatio > 0.8f)
        {
            float instability = (powerRatio - 0.8f) * 2f;
            yaw += (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * 0.035f * instability;
            pitch += (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * 0.01f * instability;

            tipOffset.X += (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * 0.1f * instability;
            tipOffset.Y += (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * 0.1f * instability;

            float maxRadius = 0.6f;
            if (tipOffset.LengthSquared() > maxRadius * maxRadius)
                tipOffset = Vector2.Normalize(tipOffset) * maxRadius;
        }

        // Generate the TRUE 3D direction vector
        Vector3 flatForward = new Vector3(-MathF.Sin(yaw), 0, -MathF.Cos(yaw));
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, flatForward));

        Matrix4x4 pitchRot = Matrix4x4.CreateFromAxisAngle(right, pitch);
        Vector3 trueDirection = Vector3.Transform(flatForward, pitchRot);

        var shot = new Shot(trueDirection, power, tipOffset);

        Reset();
        return shot;
    }

    private float CalculateImpactSpeed(float rawImpactVelocity)
    {
        float cueWeight = 0.538f;  // ~19 oz cue
        float ballWeight = 0.170f; // ~6 oz cue ball
        float restitution = 0.85f; // Bounciness of the tip

        float massCoefficient = (cueWeight * (1.0f + restitution)) / (cueWeight + ballWeight);
        float finalPower = rawImpactVelocity * massCoefficient;

        return Math.Clamp(finalPower, 0.0f, GameSettings.MaxImpactSpeed);
    }
}