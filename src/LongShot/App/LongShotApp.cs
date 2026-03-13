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
    // Holds the raw Bepu memory snapshot of the table
    private TableSnapshot _savedSnapshot;

    public LongShotApp(GameWindow window, DX12Renderer renderer) : base(window)
    {
        _renderer = renderer;
        _match = new MatchManager(_cueController, new CueBallSystem(_engine));
        _sceneBuilder = new BilliardsSceneBuilder(_engine, _camera, _match);
    }

    protected override void Update(float dt)
    {
        var input = Window.InputManager.State;

        // ==========================================
        // QUICK SAVE / QUICK LOAD
        // ==========================================
        // (Note: Adjust 'IsKeyPressed' and 'Key.F5' to match your specific Input framework's syntax)

        if (input.IsKeyPressed((int)ConsoleKey.F5))
        {
            _savedSnapshot = _engine.TakeSnapshot();

            // Optional: You could log to the console or trigger a UI flash here
            System.Console.WriteLine("Table state saved!");
        }

        if (input.IsKeyPressed((int)ConsoleKey.F8) && _savedSnapshot != null)
        {
            _engine.RestoreSnapshot(_savedSnapshot);
            System.Console.WriteLine("Table state loaded!");

            // IMPORTANT: If you load a still table while the game was simulating, 
            // you need to reset the game logic back to aiming!
            // _match.ForceAimMode(); // You will need to implement something like this in MatchManager
        }
        // ==========================================

        _camera.Update(input, _match.Mode, _engine.GetBallPosition(0), dt);

        RetroAudio.UpdateListener(_camera.Position, _camera.Target - _camera.Position);

        _match.Update(_engine, input, _camera, dt);
    }

    protected override void FixedUpdate(float dt)
    {
        if (_match.Mode == GameStateMode.Simulate)
        {
            _engine.Tick(dt);
        }
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