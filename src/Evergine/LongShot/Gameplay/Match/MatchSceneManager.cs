using System;
using System.Collections.Generic;
using Evergine.Common.Attributes;
using Evergine.Common.Input.Keyboard;
using Evergine.Common.Input.Mouse;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Managers;
using Evergine.Framework.Prefabs;
using Evergine.Framework.Services;
using Evergine.Mathematics;
using Longshot.Engine;
using Longshot.Gameplay.Cue;
using Longshot.Gameplay.Table;
using Longshot.Utils;
using Longshot.Visuals;
using LongShot.Engine;

namespace Longshot.Gameplay.Match;

public class MatchSceneManager : UpdatableSceneManager
{
    [BindService]
    private readonly GraphicsPresenter graphicsPresenter;

    public GameStateMode Mode { get; private set; } = GameStateMode.Aim;

    [IgnoreEvergine]
    public BilliardsEngine Engine { get; private set; }

    [IgnoreEvergine]
    private CueControllerBehavior _cueController;

    [IgnoreEvergine]
    private Entity _cueStickEntity;

    private BallVisualizer[] _ballVisualizers;

    protected override void Start()
    {
        base.Start();

        Engine = new BilliardsEngine();

        _cueController = EnsureCueControllerBehavior();
        LoadTableCollisions();
        SpawnVisualBalls();
        SyncVisuals();
    }

    public void SyncVisuals()
    {
        for (int i = 0; i < Engine.ActiveBallCount; i++)
        {
            var ballState = Engine.PhysicsStates[i];
            _ballVisualizers[i].SyncVisuals(ballState);
        }
    }


    private Entity EnsureCueStickExists()
    {
        var cueStickEntity = this.Managers.EntityManager.Find("Cue");
        if (cueStickEntity == null)
        {
            var cueStickPrefab = this.Managers.AssetSceneManager.Load<Prefab>(EvergineContent.Scenes.BaseCue_weprefab);
            cueStickEntity = cueStickPrefab.Instantiate();
            cueStickEntity.Name = "Cue";
            this.Managers.EntityManager.Add(cueStickEntity);
        }

        return cueStickEntity;
    }

    private CueControllerBehavior EnsureCueControllerBehavior()
    {
        if (_cueController == null)
        {
            _cueStickEntity = EnsureCueStickExists();
            _cueController = _cueStickEntity.FindComponent<CueControllerBehavior>();
            if (_cueController == null)
            {
                _cueController = new CueControllerBehavior();
                _cueStickEntity.AddComponent(_cueController);
            }
        }

        return _cueController;
    }

    private void SpawnVisualBalls()
    {
        var balls = this.Managers.EntityManager.Find("Balls");
        if (balls != null)
        {
            this.Managers.EntityManager.Remove(balls);
        }

        // container for our balls
        balls = new Entity()
        {
            Name = "Balls"
        };

        var ballPrefab = this.Managers.AssetSceneManager.Load<Prefab>(EvergineContent.Scenes.BaseBall_weprefab);
        _ballVisualizers = new BallVisualizer[Engine.ActiveBallCount];
        for (int i = 0; i < Engine.ActiveBallCount; i++)
        {
            Entity visualBall = ballPrefab.Instantiate();
            visualBall.Name = $"VisualBall_{i}";

            var visualizer = new BallVisualizer() { BallId = i };
            visualBall.AddComponent(visualizer);
            _ballVisualizers[i] = visualizer;

            balls.AddChild(visualBall);
        }

        this.Managers.EntityManager.Add(balls);
    }
    public override void Update(TimeSpan gameTime)
    {
        float dt = (float)gameTime.TotalSeconds;
        var input = graphicsPresenter.FocusedDisplay.KeyboardDispatcher;
        var mouse = graphicsPresenter.FocusedDisplay.MouseDispatcher;

        if (Mode == GameStateMode.Simulate && Engine.AreAllBallsAsleep())
        {
            Mode = GameStateMode.Aim;
            _cueStickEntity.IsEnabled = true;
        }

        switch (Mode)
        {
            case GameStateMode.Aim:
                UpdateAim(input, mouse);
                _cueController.UpdateVisualTransform(Engine.GetBallPosition(0).ToEvergine());
                break;

            case GameStateMode.Shoot:
                UpdatePower(dt);
                _cueController.UpdateVisualTransform(Engine.GetBallPosition(0).ToEvergine());
                break;

            case GameStateMode.Simulate:
                Engine.Tick(dt);
                break;
        }
    }

    private void UpdateAim(KeyboardDispatcher kb, MouseDispatcher mouse)
    {
        //  English/Pitch aiming logic here

        // Transition to Power stroke
        if (mouse.IsButtonDown(Evergine.Common.Input.Mouse.MouseButtons.Left))
        {
            _cueController.ResetStroke();
            Mode = GameStateMode.Shoot;
        }
    }

    private void UpdatePower(float dt)
    {
        var mouse = graphicsPresenter.FocusedDisplay.MouseDispatcher;

        if (!mouse.IsButtonDown(Evergine.Common.Input.Mouse.MouseButtons.Left))
        {
            Mode = GameStateMode.Aim;
            return;
        }

        _cueController.UpdateStroke(dt);

        if (_cueController.HasPulledBack)
        {
            var cueBallPos = Engine.GetBallPosition(0);

            var prevTipWorld = CalculateTipWorldPosition(cueBallPos, _cueController.PreviousCueOffset);
            var currentTipWorld = CalculateTipWorldPosition(cueBallPos, _cueController.CueOffset);

            float effectiveRadius = Engine.Config.Ball.Radius + 0.005f; // Small buffer

            if (CheckTipImpact(prevTipWorld, currentTipWorld, cueBallPos, effectiveRadius, out float hitT))
            {
                float impactPower = CalculateImpactSpeed(Math.Abs(_cueController.CurrentVelocity));

                // Build direction based on Pitch/Yaw
                Vector3 flatForward = new Vector3(-MathF.Sin(_cueController.Yaw), 0, -MathF.Cos(_cueController.Yaw));
                Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, flatForward));
                Matrix4x4 pitchRot = Matrix4x4.CreateFromAxisAngle(right, _cueController.Pitch);
                Vector3 trueDirection = Vector3.Transform(flatForward, pitchRot);

                Engine.StrikeCueBall(0, trueDirection.ToNumerics(), impactPower, new Vector3(_cueController.TipOffset, 0).ToNumerics());

                _cueStickEntity.IsEnabled = false; 
                Mode = GameStateMode.Simulate;
            }
        }
    }


    private System.Numerics.Vector3 CalculateTipWorldPosition(System.Numerics.Vector3 cueBallPos, float stickOffset)
    {
        var cueTransform = _cueStickEntity.FindComponent<Transform3D>();

        var actualForward = cueTransform.WorldTransform.Forward.ToNumerics();
        var right = cueTransform.WorldTransform.Right.ToNumerics();
        var actualUp = cueTransform.WorldTransform.Up.ToNumerics();

        var tipWorldOffset = (right * _cueController.TipOffset.X * GameSettings.BallRadius) +
                                     (actualUp * _cueController.TipOffset.Y * GameSettings.BallRadius);

        var targetPos = cueBallPos + tipWorldOffset;

        return targetPos + (actualForward * stickOffset);
    }

    private bool CheckTipImpact(System.Numerics.Vector3 lineStart, System.Numerics.Vector3 lineEnd, System.Numerics.Vector3 sphereCenter, float radius, out float hitT)
    {
        hitT = 0f;
        var d = lineEnd - lineStart;
        var f = lineStart - sphereCenter;

        float a = System.Numerics.Vector3.Dot(d, d);
        float b = 2 * System.Numerics.Vector3.Dot(f, d);
        float c = System.Numerics.Vector3.Dot(f, f) - (radius * radius);

        float discriminant = (b * b) - (4 * a * c);

        if (discriminant < 0)
        {
            return false;
        }

        discriminant = MathF.Sqrt(discriminant);
        float t1 = (-b - discriminant) / (2 * a);

        if (t1 is >= 0f and <= 1f)
        {
            hitT = t1;
            return true;
        }

        return false;
    }

    private float CalculateImpactSpeed(float rawImpactVelocity)
    {
        float cueWeight = 0.538f;  // ~19 oz cue
        float ballWeight = 0.170f; // ~6 oz cue ball
        float restitution = 0.85f; // Bounciness of the tip

        float massCoefficient = cueWeight * (1.0f + restitution) / (cueWeight + ballWeight);
        float finalPower = rawImpactVelocity * massCoefficient;

        return Math.Clamp(finalPower, 0.0f, GameSettings.MaxImpactSpeed);
    }

    private void LoadTableCollisions()
    {
        var tableSegments = new List<CushionSegment>();

        // Find every procedural rail currently in the scene
        var rails = this.Managers.EntityManager.FindComponentsOfType<ProceduralRail>();

        foreach (var rail in rails)
        {
            if (rail.LocalCollisionSegments == null)
            {
                continue;
            }

            var transform = rail.Owner.FindComponent<Transform3D>();

            var matrix = System.Numerics.Matrix4x4.CreateRotationY(-transform.LocalRotation.Y) * System.Numerics.Matrix4x4.CreateTranslation(transform.LocalPosition.X, 0, transform.LocalPosition.Z);

            foreach (var localSeg in rail.LocalCollisionSegments)
            {
                var localStart3D = new System.Numerics.Vector3(localSeg.Start.X, 0, localSeg.Start.Y);
                var localEnd3D = new System.Numerics.Vector3(localSeg.End.X, 0, localSeg.End.Y);
                var localNormal3D = new System.Numerics.Vector3(localSeg.Normal.X, 0, localSeg.Normal.Y);

                var tableStart = System.Numerics.Vector3.Transform(localStart3D, matrix);
                var tableEnd = System.Numerics.Vector3.Transform(localEnd3D, matrix);
                var tableNormal = System.Numerics.Vector3.TransformNormal(localNormal3D, matrix);

                tableSegments.Add(new CushionSegment(tableStart, tableEnd, tableNormal));
            }
        }

        var pocketBeams = new List<PocketBeam>();

        var gates = this.Managers.EntityManager.FindComponentsOfType<LaserPocketGate>();

        foreach (var gate in gates)
        {
            var transform = gate.Owner.FindComponent<Transform3D>();

            // We get the world properties directly from Evergine!
            var center = transform.Position.ToNumerics();
            var forward = transform.WorldTransform.Forward.ToNumerics(); // This is the Z axis (Length)
            var right = transform.WorldTransform.Right.ToNumerics();     // This is the X axis (Pull Direction)

            float halfLen = gate.Length / 2f;

            // Apply the depth offset along the local X axis
            var offsetCenter = center + right;

            // Reconstruct the P1 and P2 points in 3D Table Space
            var p1 = offsetCenter - (forward * halfLen);
            var p2 = offsetCenter + (forward * halfLen);

            // We pass the local right vector as the PullDirection
            pocketBeams.Add(new PocketBeam(p1, p2, right, GameSettings.BallRadius*2f));
        }

        Engine.InitializeMatch(tableSegments.ToArray(), pocketBeams.ToArray());
    }
}
