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
                UpdateAim(input);
                break;

            case GameStateMode.Power:
                UpdatePower(input, camera, deltaTime, engine);
                break;
        }
    }

    void UpdateAim(InputState input)
    {
        _cue.UpdateAim(input);

        if (input.Keys[(int)ConsoleKey.Spacebar])
        {
            input.Keys[(int)ConsoleKey.Spacebar] = false;

            _cue.BeginStroke();
            Mode = GameStateMode.Power;
        }
    }

    void UpdatePower(InputState input, Camera camera, float dt, BilliardsEngine engine)
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

            // 1. Calculate the 3D tip positions for the PREVIOUS frame and CURRENT frame
            Vector3 prevTipWorld = CalculateTipWorldPosition(camera, cueBallPos, _cue.PreviousCueOffset);
            Vector3 currentTipWorld = CalculateTipWorldPosition(camera, cueBallPos, _cue.CueOffset);

            // 2. Perform a mathematically perfect Line-Sphere sweep
            // We add a tiny 5mm radius for the cue tip itself to the ball's radius
            float effectiveRadius = GameSettings.StandardBallRadius + 0.005f;

            if (CheckTipImpact(prevTipWorld, currentTipWorld, cueBallPos, effectiveRadius))
            {
                var shot = _cue.BuildShot(camera);

                _cueBall.ApplyShot(shot);

                Mode = GameStateMode.Simulate;
            }
        }
    }

    private Vector3 CalculateTipWorldPosition(Camera camera, Vector3 cueBallPos, float stickOffset)
    {
        // Reconstruct the cue's coordinate system (same as your SceneBuilder!)
        Vector3 forward = new Vector3(-MathF.Sin(camera.Yaw), 0, -MathF.Cos(camera.Yaw));
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 up = Vector3.Cross(forward, right);

        // Apply the English offset (TipOffset is mapped 0.0 to 1.0, scale by ball radius)
        Vector3 tipWorldOffset = (right * _cue.TipOffset.X * GameSettings.StandardBallRadius) +
                                 (up * _cue.TipOffset.Y * GameSettings.StandardBallRadius);

        Vector3 targetPos = cueBallPos + tipWorldOffset;

        // Apply the camera Pitch to the forward vector so the cue angles downward
        Matrix4x4 pitchRot = Matrix4x4.CreateFromAxisAngle(right, camera.Pitch);
        Vector3 actualForward = Vector3.Transform(forward, pitchRot);

        // Add the stick offset (negative means pulled back, positive is follow-through)
        return targetPos + (actualForward * stickOffset);
    }

    private bool CheckTipImpact(Vector3 lineStart, Vector3 lineEnd, Vector3 sphereCenter, float radius)
    {
        Vector3 d = lineEnd - lineStart;
        Vector3 f = lineStart - sphereCenter;

        float a = Vector3.Dot(d, d);
        float b = 2 * Vector3.Dot(f, d);
        float c = Vector3.Dot(f, f) - (radius * radius);

        float discriminant = b * b - 4 * a * c;

        // No intersection
        if (discriminant < 0) return false;

        discriminant = MathF.Sqrt(discriminant);

        // t1 is the percentage along the line segment where it enters the sphere
        float t1 = (-b - discriminant) / (2 * a);

        // If t1 is between 0.0 and 1.0, the impact happened exactly between last frame and this frame!
        return t1 >= 0f && t1 <= 1f;
    }
}