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
        Reset();
    }

    public ShotResult UpdateStroke(InputState input, float dt)
    {
        // 1. Calculate physical movement with sensitivity
        float movement = -input.MouseDeltaY * GameSettings.MouseSensitivity;

        // 2. Calculate instantaneous velocity (protect against divide-by-zero)
        float currentVelocity = movement / Math.Max(dt, 0.0001f);

        // 3. Track peak forward velocity for realistic power
        if (currentVelocity > 0)
        {
            _peakForwardVelocity = Math.Max(_peakForwardVelocity, currentVelocity);
        }
        else if (currentVelocity < -0.001f)
        {
            // If the player starts pulling back again, reset the forward momentum
            _peakForwardVelocity = 0;
        }

        // 4. Apply movement to the cue stick
        float previousOffset = CueStickOffset;
        CueStickOffset += movement;

        // Only allow the cue to push into the ball (positive offset) 
        // IF the player has already pulled back to initiate a valid shot.
        float maxForwardAllowed = _hasPulledBack ? 0.1f : 0f;

        CueStickOffset = Math.Clamp(CueStickOffset, GameSettings.MaxPullback, maxForwardAllowed);

        if (CueStickOffset < -0.05f)
        {
            _hasPulledBack = true;
        }

        bool struck = _hasPulledBack && previousOffset < 0f && CueStickOffset >= 0f;

        if (struck)
        {
            // Snap it exactly back to 0 so it doesn't render inside the cue ball
            CueStickOffset = 0f;
            return ShotResult.Strike;
        }



        if (input.Keys[(int)ConsoleKey.Escape])
        {
            input.Keys[(int)ConsoleKey.Escape] = false;
            Reset();
            return ShotResult.Cancel;
        }

        return ShotResult.None;
    }

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
        // Tune this multiplier until a fast mouse swipe feels like a break shot
        float velocityToPowerMultiplier = 1.5f;

        float finalPower = _peakForwardVelocity * velocityToPowerMultiplier;

        return Math.Clamp(finalPower, 0.1f, GameSettings.MaxImpactSpeed);
    }
}