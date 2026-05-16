using Evergine.Mathematics;

namespace Longshot.Gameplay.Match;

public class MatchSceneManager : UpdatableSceneManager
{
    [BindService]
    private readonly GraphicsPresenter graphicsPresenter;

    public GameStateMode Mode { get; private set; } = GameStateMode.Aim;

    [IgnoreEvergine]
    public BilliardsEngine Engine { get; private set; }

    [IgnoreEvergine]
    public TableDefinition CurrentTableData { get; private set; }

    [IgnoreEvergine]
    private CueControllerBehavior _cueController;

    [IgnoreEvergine]
    private Entity _cueStickEntity;

    private BallVisualizer[] _ballVisualizers;

    protected override void Start()
    {
        base.Start();

        // 1. Initialize the Engine and Pure Data
        Engine = new BilliardsEngine();
        Engine.OnBallPocketed += HandleBallPocketed;
        CurrentTableData = TableDefinition.BuildWpaStandard();

        // 2. Instruct the visual director to paint the scene based on the pure data
        var tableDirector = this.Managers.EntityManager.FindFirstComponentOfType<TronTableDirector>();
        if (tableDirector != null)
        {
            tableDirector.BuildVisualTable(CurrentTableData);
        }

        // 3. Setup Physics and Interactions
        _cueController = EnsureCueControllerBehavior();
        LoadTableCollisions(CurrentTableData);
        SpawnVisualBalls();
        SyncVisuals();
    }

    private void HandleBallPocketed(int id, System.Numerics.Vector3 dropPos)
    {
        // Cue ball scratch: drop it back on the head spot. Future game rules (ball-in-hand,
        // 8-ball respotting, foul accounting) belong here, not in the engine.
        if (id == 0)
        {
            Engine.RespawnBall(0, new System.Numerics.Vector3(0, Engine.Config.Ball.Radius, -0.8f));
        }
    }

    private void LoadTableCollisions(TableDefinition tableData)
    {
        var (rails, pockets) = TableBuilder.Build(tableData);
        Engine.InitializeMatch(rails, pockets);
        BuildStandardRack(Engine);
    }

    /// <summary>
    /// 8-ball triangle rack at the foot spot, cue ball at the head. Pure gameplay setup -
    /// the engine itself doesn't know what a rack is.
    /// </summary>
    private static void BuildStandardRack(BilliardsEngine engine)
    {
        float r = engine.Config.Ball.Radius;
        engine.AddBall(new System.Numerics.Vector3(0, r, -0.8f), BallType.Cue);

        float spacing = r * 2.001f;
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col <= row; col++)
            {
                float jitterX = (float)(System.Random.Shared.NextDouble() - 0.5) * 0.0001f;
                float jitterZ = (float)(System.Random.Shared.NextDouble() - 0.5) * 0.0001f;

                var pos = new System.Numerics.Vector3(
                    ((col - (row * 0.5f)) * spacing) + jitterX,
                    r,
                    0.8f + (row * spacing * 0.866f) + jitterZ);

                engine.AddBall(pos, BallType.Normal);
            }
        }
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
}
