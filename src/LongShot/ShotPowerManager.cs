using System;
using System.Numerics;

namespace LongShot;

public sealed class ShotPowerManager
{
    private struct StrokeSample
    {
        public float Velocity;
        public float DeltaTime;
    }

    private readonly StrokeSample[] _strokeBuffer = new StrokeSample[64];
    private int _bufferIndex;

    public bool HasPulledBack { get; private set; }
    public float PreviousCueStickOffset { get; private set; }
    public float CueStickOffset { get; private set; }

    public void Reset()
    {
        CueStickOffset = 0f;
        PreviousCueStickOffset = 0f;
        HasPulledBack = false;
        ClearBuffer();
    }

    public void BeginStroke()
    {
        Reset();
    }

    public ShotResult UpdateStroke(InputState input, float dt)
    {
        float movement = -input.MouseDeltaY * GameSettings.MouseSensitivity;
        float safeDt = Math.Max(dt, 0.0001f);
        float currentVelocity = movement / safeDt;

        _strokeBuffer[_bufferIndex] = new StrokeSample { Velocity = currentVelocity, DeltaTime = safeDt };
        _bufferIndex = (_bufferIndex + 1) % _strokeBuffer.Length;

        PreviousCueStickOffset = CueStickOffset;
        CueStickOffset += movement;

        float maxForwardAllowed = HasPulledBack ? 0.1f : 0f;
        CueStickOffset = Math.Clamp(CueStickOffset, GameSettings.MaxPullback, maxForwardAllowed);

        if (CueStickOffset < -0.05f)
        {
            HasPulledBack = true;
        }

        if (movement < -0.005f && CueStickOffset < -0.05f)
        {
            ClearBuffer();
        }

        if (input.IsKeyPressed(27))
        {
            Reset();
            return ShotResult.Cancel;
        }

        return ShotResult.None;
    }

    public Shot BuildShot(Camera camera, Vector2 tipOffset)
    {
        float impactVelocity = CalculateWeightedImpactVelocity();
        float power = CalculateImpactSpeed(impactVelocity);

        float yaw = camera.Yaw;
        float powerRatio = power / GameSettings.MaxImpactSpeed;

        if (powerRatio > 0.5f)
        {
            float instability = (powerRatio - 0.5f) * 2f;

            float yawError = (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * 0.035f * instability;
            yaw += yawError;

            tipOffset.X += (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * 0.1f * instability;
            tipOffset.Y += (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * 0.1f * instability;

            float maxRadius = 0.6f;
            if (tipOffset.LengthSquared() > maxRadius * maxRadius)
            {
                tipOffset = Vector2.Normalize(tipOffset) * maxRadius;
            }
        }

        Vector3 dir = Vector3.Normalize(
            new Vector3(
                -MathF.Sin(yaw),
                0,
                -MathF.Cos(yaw)));

        var shot = new Shot(dir, power, tipOffset);

        Reset();

        return shot;
    }

    private float CalculateWeightedImpactVelocity()
    {
        float targetWindowMs = 0.040f;
        float accumulatedTime = 0f;
        float weightedVelocitySum = 0f;
        float totalWeight = 0f;

        int index = (_bufferIndex - 1 + _strokeBuffer.Length) % _strokeBuffer.Length;

        for (int i = 0; i < _strokeBuffer.Length; i++)
        {
            var sample = _strokeBuffer[index];
            if (sample.DeltaTime <= 0) break;

            accumulatedTime += sample.DeltaTime;

            float weight = Math.Max(0f, 1f - (accumulatedTime / targetWindowMs));

            if (sample.Velocity > 0)
            {
                weightedVelocitySum += sample.Velocity * weight;
                totalWeight += weight;
            }

            if (accumulatedTime >= targetWindowMs) break;

            index = (index - 1 + _strokeBuffer.Length) % _strokeBuffer.Length;
        }

        return totalWeight > 0f ? weightedVelocitySum / totalWeight : 0f;
    }

    private float CalculateImpactSpeed(float rawImpactVelocity)
    {
        // --- THE COMPRESSION CURVE ---
        // Increased from 0.08f to 0.25f to clamp down much harder on fast mouse flicks.
        float compressionFactor = 0.25f;

        float compressedVelocity = rawImpactVelocity / (1.0f + (rawImpactVelocity * compressionFactor));

        // --- MASTER POWER SCALE ---
        // If you find that absolutely *everything* (even slow putts) is slightly too fast,
        // drop this value to 0.8f or 0.7f to globally reduce all stroke power.
        float masterPowerScale = 1.0f;
        compressedVelocity *= masterPowerScale;

        float cueweight = 0.538f;
        float ballw = 0.170f;
        float e = 0.85f;

        float velocityToPowerMultiplier = ((1.0f + e) * cueweight) / (cueweight + ballw);

        float finalPower = compressedVelocity * velocityToPowerMultiplier;

        return Math.Clamp(finalPower, 0.01f, GameSettings.MaxImpactSpeed);
    }

    private void ClearBuffer()
    {
        Array.Clear(_strokeBuffer, 0, _strokeBuffer.Length);
    }
}