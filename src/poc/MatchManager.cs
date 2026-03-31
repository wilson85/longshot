using System;
using System.Numerics;
using LongShot.Engine;

namespace LongShot;

public sealed class MatchManager
{
    public GameStateMode Mode { get; private set; } = GameStateMode.Aim;

    readonly CueController _cue;
    readonly CueBallSystem _cueBall;

    public float CueStickOffset => _cue.CueOffset;
    public Vector2 TipOffset => _cue.TipOffset;

    public float CueYaw => _cue.Yaw;
    public float CuePitch => _cue.Pitch;

    public MatchManager(
        CueController cue,
        CueBallSystem cueBall)
    {
        _cue = cue;
        _cueBall = cueBall;
    }

    public void Update(
        BilliardsEngine engine,
        InputState input,
        Camera camera,
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

        switch (Mode)
        {
            case GameStateMode.Aim:
                UpdateAim(input, camera);
                break;

            case GameStateMode.Power:
                UpdatePower(input, deltaTime, engine);
                break;
        }
    }

    void UpdateAim(InputState input, Camera camera)
    {
        // Pass camera here just to sync Yaw during normal aiming
        _cue.UpdateAim(input, camera);

        if (input.Keys[(int)ConsoleKey.Spacebar])
        {
            input.Keys[(int)ConsoleKey.Spacebar] = false;

            _cue.BeginStroke();
            Mode = GameStateMode.Power;
        }
    }

    void UpdatePower(InputState input, float dt, BilliardsEngine engine)
    {
        var result = _cue.UpdateStroke(input, dt);

        if (result == ShotResult.Cancel)
        {
            Mode = GameStateMode.Aim;
            return;
        }

        // --- TRUE GEOMETRIC IMPACT DETECTION ---
        if (_cue.HasPulledBack)
        {
            Vector3 cueBallPos = engine.GetBallPosition(0);

            // Calculate the 3D tip positions using the Cue's angles, not the camera!
            Vector3 prevTipWorld = CalculateTipWorldPosition(cueBallPos, _cue.PreviousCueOffset);
            Vector3 currentTipWorld = CalculateTipWorldPosition(cueBallPos, _cue.CueOffset);

            float effectiveRadius = GameSettings.StandardBallRadius + 0.005f;

            if (CheckTipImpact(prevTipWorld, currentTipWorld, cueBallPos, effectiveRadius, out float hitT))
            {
                var shot = _cue.BuildShot();
                _cueBall.ApplyShot(shot);

                Mode = GameStateMode.Simulate;
            }
        }
    }

    private Vector3 CalculateTipWorldPosition(Vector3 cueBallPos, float stickOffset)
    {
        // Reconstruct the cue's coordinate system purely from the CueController
        Vector3 flatForward = new Vector3(-MathF.Sin(_cue.Yaw), 0, -MathF.Cos(_cue.Yaw));
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, flatForward));

        Matrix4x4 pitchRot = Matrix4x4.CreateFromAxisAngle(right, _cue.Pitch);
        Vector3 actualForward = Vector3.Transform(flatForward, pitchRot);
        Vector3 actualUp = Vector3.Cross(actualForward, right);

        // Apply the English offset relative to the actual 3D orientation
        Vector3 tipWorldOffset = (right * _cue.TipOffset.X * GameSettings.StandardBallRadius) +
                                 (actualUp * _cue.TipOffset.Y * GameSettings.StandardBallRadius);

        Vector3 targetPos = cueBallPos + tipWorldOffset;

        return targetPos + (actualForward * stickOffset);
    }

    private bool CheckTipImpact(Vector3 lineStart, Vector3 lineEnd, Vector3 sphereCenter, float radius, out float hitT)
    {
        hitT = 0f;
        Vector3 d = lineEnd - lineStart;
        Vector3 f = lineStart - sphereCenter;

        float a = Vector3.Dot(d, d);
        float b = 2 * Vector3.Dot(f, d);
        float c = Vector3.Dot(f, f) - (radius * radius);

        float discriminant = b * b - 4 * a * c;

        if (discriminant < 0) return false;

        discriminant = MathF.Sqrt(discriminant);
        float t1 = (-b - discriminant) / (2 * a);

        if (t1 >= 0f && t1 <= 1f)
        {
            hitT = t1;
            return true;
        }

        return false;
    }
}