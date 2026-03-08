using System.Numerics;
using ImGuiNET;
using LongShot.Engine;

namespace LongShot;

public enum GameStateMode
{
    View,
    Aim,
    Power,
    Simulate
}
public sealed class MatchManager
{
    public GameStateMode Mode { get; private set; } = GameStateMode.Aim;

    public float CueStickOffset { get; private set; }

    readonly ShotPowerManager _shotPower = new();

    Vector2 _tipOffset;

    bool _hasPulledBack;

    // The physical distance of the stick from the cue ball's surface.
    // 0 = touching. Negative = pulled back. Positive = pushed through.
    float _physicalStickOffset;

    const float PullBackThreshold = -0.05f;
    const float MaxPullback = -0.4f;

    public void Update(
        BilliardsEngine engine,
        dynamic input,
        dynamic camera,
        float deltaTime)
    {
        if (Mode == GameStateMode.Simulate &&
            engine.AreAllBallsAsleep())
        {
            Mode = GameStateMode.Aim;
        }

        if (input.Keys[(int)ConsoleKey.V] && Mode != GameStateMode.Simulate)
        {
            Mode = GameStateMode.View;
        }

        if (input.Keys[(int)ConsoleKey.A] && Mode != GameStateMode.Simulate)
        {
            Mode = GameStateMode.Aim;
        }

        if (Mode == GameStateMode.Aim)
        {
            UpdateAim(input);

            if (input.Keys[(int)ConsoleKey.Spacebar])
            {
                input.Keys[(int)ConsoleKey.Spacebar] = false;
                StartPowerStroke();
            }
        }
        else if (Mode == GameStateMode.Power)
        {
            UpdatePowerStroke(engine, input, camera, deltaTime);
        }
    }

    void UpdateAim(dynamic input)
    {
        float tipSpeed = 0.003f;

        _tipOffset.X += input.MouseDeltaX * tipSpeed;
        _tipOffset.Y -= input.MouseDeltaY * tipSpeed;

        _tipOffset = Vector2.Clamp(
            _tipOffset,
            new Vector2(-0.9f),
            new Vector2(0.9f));
    }

    void StartPowerStroke()
    {
        Mode = GameStateMode.Power;

        // Reset all physical state
        _physicalStickOffset = 0f;
        CueStickOffset = 0f;
        _hasPulledBack = false;

        _shotPower.Reset();
    }

    void UpdatePowerStroke(
        BilliardsEngine engine,
        dynamic input,
        dynamic camera,
        float deltaTime)
    {
        float frameMovement = input.MouseDeltaY * 0.002f;

        float instantaneousVelocity = frameMovement / deltaTime;
        _shotPower.TrackVelocity(instantaneousVelocity);

        float previousOffset = _physicalStickOffset;
        _physicalStickOffset += frameMovement;

        // We clamp exactly at 0.0f so the stick physically cannot penetrate the ball.
        _physicalStickOffset = Math.Clamp(_physicalStickOffset, MaxPullback, 0.0f);

        // 1:1 Visual Mapping (No Lerping during the stroke!)
        CueStickOffset = _physicalStickOffset;

        if (_physicalStickOffset < PullBackThreshold)
        {
            _hasPulledBack = true;
        }

        // STRIKE DETECTION
        bool struckBall = previousOffset < 0f && _physicalStickOffset >= 0f;

        if (_hasPulledBack && struckBall)
        {
            FireShot(engine, camera);
            return;
        }

        if (input.Keys[(int)ConsoleKey.Escape])
        {
            input.Keys[(int)ConsoleKey.Escape] = false;
            CancelShot();
        }
    }

    void FireShot(
        BilliardsEngine engine,
        dynamic camera)
    {
        float cueSpeed = _shotPower.CalculateImpactSpeed();

        // Prevent ghost hits if the user just slowly bumped the ball
        //if (cueSpeed < 0.15f)
        //{
        //    CancelShot();
        //    return;
        //}

        Vector3 shotDir = Vector3.Normalize(new Vector3(-MathF.Sin(camera.Yaw), 0, -MathF.Cos(camera.Yaw)));

        engine.StrikeCueBall(shotDir, cueSpeed, _tipOffset);

        // Reset visual and physical state
        CueStickOffset = 0;
        _physicalStickOffset = 0;
        _shotPower.Reset();

        Mode = GameStateMode.Simulate;
    }

    void CancelShot()
    {
        CueStickOffset = 0;
        _physicalStickOffset = 0;
        _shotPower.Reset();

        Mode = GameStateMode.Aim;
    }
}