using System.Numerics;

namespace LongShot;

public sealed class ShotPowerManager
{
    const int VelocitySamples = 4;

    const float PullBackThreshold = -0.15f;
    const float MaxPullback = -0.40f;

    readonly float[] _velocity = new float[VelocitySamples];

    int _velocityIndex;
    int _velocityCount;

    bool _hasPulledBack;
    Vector2 _tipOffset;

    public float CueStickOffset { get; private set; }

    public Vector2 TipOffset => _tipOffset;

    public void Reset()
    {
        _velocityIndex = 0;
        _velocityCount = 0;
        CueStickOffset = 0;
        _hasPulledBack = false;
    }

    public void BeginStroke()
    {
        Reset();
    }

    public void UpdateAim(dynamic input)
    {
        float tipSpeed = 0.003f;

        _tipOffset.X += input.MouseDeltaX * tipSpeed;
        _tipOffset.Y -= input.MouseDeltaY * tipSpeed;

        _tipOffset = Vector2.Clamp(
            _tipOffset,
            new Vector2(-0.9f),
            new Vector2(0.9f));
    }

    public ShotResult UpdateStroke(InputState input, float dt)
    {
        float movement = -input.MouseDeltaY;

        float velocity = movement / dt;

        TrackVelocity(velocity);

        float previousOffset = CueStickOffset;

        CueStickOffset += movement;

        CueStickOffset = Math.Clamp(CueStickOffset, MaxPullback, 0f);

        if (CueStickOffset < PullBackThreshold)
        {
            _hasPulledBack = true;
        }

        bool struck = previousOffset < 0f && CueStickOffset >= 0f;

        if (_hasPulledBack && struck)
        {
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

    void TrackVelocity(float v)
    {
        if (v > 0)
        {
            _velocity[_velocityIndex] = v;

            _velocityIndex = (_velocityIndex + 1) % VelocitySamples;

            if (_velocityCount < VelocitySamples)
            {
                _velocityCount++;
            }
        }
        else if (v < -0.5f)
        {
            _velocityCount = 0;
        }
    }

    public Shot BuildShot(dynamic camera)
    {
        float power = CalculateImpactSpeed();

        Vector3 dir = Vector3.Normalize(
            new Vector3(
                -MathF.Sin(camera.Yaw),
                0,
                -MathF.Cos(camera.Yaw)));

        var shot = new Shot(dir, power, _tipOffset);

        Reset();

        return shot;
    }

    float CalculateImpactSpeed()
    {
        if (_velocityCount == 0)
        {
            return 0;
        }

        float total = 0;

        for (int i = 0; i < _velocityCount; i++)
        {
            total += _velocity[i];
        }

        float avg = total / _velocityCount;

        return Math.Clamp(avg, 0f, 10f);
    }
}