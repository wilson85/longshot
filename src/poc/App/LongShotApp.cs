using System.Numerics;
using LongShot.Engine;
using LongShot.Rendering;

namespace LongShot.App;

public sealed class LongShotApp : GameApplication
{
    private readonly BilliardsEngine _engine = new BilliardsEngine();
    private readonly CueController _cueController = new CueController();
    private readonly Camera _camera = new Camera();
    private readonly MatchManager _match;
    private readonly DX12Renderer _renderer;
    private readonly BilliardsSceneBuilder _sceneBuilder;
    private readonly RenderQueue _renderQueue = new RenderQueue();

    // --- SAVE STATE ---
    // Holds the raw memory snapshot of the table
    private TableSnapshot _savedSnapshot;

    public LongShotApp(GameWindow window, DX12Renderer renderer) : base(window)
    {
        _renderer = renderer;

        // 1. Initialize the Hardware Audio Engine!
        RetroAudio.Init();

        // 2. Initialize the Game Logic & Visuals
        _match = new MatchManager(_cueController, new CueBallSystem(_engine));
        _sceneBuilder = new BilliardsSceneBuilder(_engine, _camera, _match);

        // 3. GENERATE THE 3D RAILS!
        // This takes the exact physics geometry and extrudes it into solid 3D meshes.
        // The widths (0.035f) and heights (0.07f) exactly match the SceneBuilder offsets!
        _renderer.LoadTableGeometry(_engine.TableLayout, 0.035f, 0.07f);
    }

    protected override void Update(float dt)
    {
        var input = Window.InputManager.State;

        // ==========================================
        // QUICK SAVE / QUICK LOAD
        // ==========================================
        if (input.IsKeyPressed((int)ConsoleKey.F5))
        {
            _savedSnapshot = _engine.TakeSnapshot();
            System.Console.WriteLine("Table state saved!");
        }

        if (input.IsKeyPressed((int)ConsoleKey.F8) && _savedSnapshot != null)
        {
            _engine.RestoreSnapshot(_savedSnapshot);
            System.Console.WriteLine("Table state loaded!");

            // If you load a still table while the game was simulating, 
            // you need to reset the game logic back to aiming!
            // _match.ForceAimMode(); 
        }
        // ==========================================

        _camera.Update(input, _match.Mode, _engine.GetBallPosition(0), dt);

        // Update 3D Audio Panning based on where the camera is looking
        RetroAudio.UpdateListener(_camera.Position, _camera.Target - _camera.Position);

        _match.Update(_engine, input, _camera, dt);
    }

    protected override void FixedUpdate(float dt)
    {
        if (_match.Mode == GameStateMode.Simulate)
        {
            _engine.Tick(dt);
            UpdateVisuals(dt, _engine);
        }
    }

    private static void UpdateVisuals(float dt, in BilliardsEngine engine)
    {
        for (int i = 0; i < engine.ActiveBallCount; i++)
        {
            ref readonly BallState phys = ref engine.PhysicsStates[i];
            BallRenderData render = engine.RenderData[i];

            // 1. Update Rotation (Ball Rolling/Spinning)
            float spinSq = phys.AngularVelocity.LengthSquared();
            if (spinSq > 0.0001f)
            {
                float spinMagnitude = MathF.Sqrt(spinSq);
                Vector3 spinAxis = phys.AngularVelocity / spinMagnitude;
                render.Orientation = Quaternion.Normalize(
                    Quaternion.CreateFromAxisAngle(spinAxis, spinMagnitude * dt) * render.Orientation
                );
            }

            // 2. State-Based Color Calculation
            // We define distinct colors for each motion state and scale by intensity
            Vector4 stateColor = GetTrailColor(phys, render);

            // 3. Trail Point Placement Logic
            float distToLast = Vector3.Distance(phys.Position, render.LastTrailPosition);

            // Only skip if stationary AND we haven't moved since the last point was placed
            if (phys.State == MotionState.Stationary && distToLast < 0.001f) continue;

            // Distance-based placement + "Force Close" on stop
            if (distToLast > 0.02f || (phys.State == MotionState.Stationary && distToLast > 0.005f))
            {
                render.Trail[render.TrailIndex] = new TrailPoint
                {
                    Position = new Vector3(phys.Position.X, 0.0f, phys.Position.Z),
                    Spin = phys.AngularVelocity.Length(),
                    Direction = phys.LinearVelocity.LengthSquared() > 0.0001f
                                ? Vector3.Normalize(phys.LinearVelocity)
                                : render.Trail[(render.TrailIndex + render.Trail.Length - 1) % render.Trail.Length].Direction,
                    // Assuming TrailPoint has a Color field now
                    Color = stateColor
                };

                render.TrailIndex = (render.TrailIndex + 1) % render.Trail.Length;
                render.LastTrailPosition = phys.Position;
            }
        }
    }
    private static Vector4 GetTrailColor(in BallState phys, BallRenderData render)
    {
        Vector3 baseColor;
        float intensity = 1.0f;

        switch (phys.State)
        {
            case MotionState.Rolling:
                // Neon Green for standard rolling
                baseColor = new Vector3(0.0f, 1.0f, 0.4f);
                // Intensity based on speed (maxing out at 3.0m/s)
                intensity = Math.Clamp(phys.LinearVelocity.Length() / 3.0f, 0.2f, 1.0f);
                break;

            case MotionState.Sliding:
                // Hot Cyan/Blue for friction/sliding
                baseColor = new Vector3(0.0f, 0.6f, 1.0f);
                // Intensity based on spin (the more it 'digs' into the cloth)
                intensity = Math.Clamp(phys.AngularVelocity.Length() / 20.0f, 0.4f, 1.2f);
                break;

            case MotionState.Airborne:
                // Purple/Magenta for jumps/airborne
                baseColor = new Vector3(0.8f, 0.0f, 1.0f);
                intensity = 1.5f; // Extra bright while in the air
                break;

            default:
                baseColor = new Vector3(0.5f, 0.5f, 0.5f);
                intensity = 0.0f;
                break;
        }

        // Return as Vector4 (RGBA), applying intensity to the RGB channels
        // We use W (Alpha) to handle the trail's overall transparency
        return new Vector4(baseColor * intensity, 0.8f);
    }

    protected override void Draw()
    {
        string modeText = _match.Mode switch
        {
            GameStateMode.Aim => "Press SPACE to lock aim",
            GameStateMode.Power => "Pull mouse BACK, then thrust FORWARD to shoot!",
            GameStateMode.Simulate => "Wait for balls to stop...",
            _ => "Right-Click to look around"
        };

        Window.SetTitle($"LongShot - Mode: {_match.Mode} | {modeText}");

        _sceneBuilder.Build(_renderQueue);

        _renderer.Render(_camera, _renderQueue);
    }

    public override void Dispose()
    {
        base.Dispose();
        // ImGuiManager.Shutdown(); // (If you are still using ImGui)
    }
}