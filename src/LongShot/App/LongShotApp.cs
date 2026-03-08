using LongShot.Engine;

namespace LongShot.App;

public sealed class LongShotApp : GameApplication
{
    private readonly BilliardsEngine _engine = new BilliardsEngine();
    private readonly CueController _cueController = new CueController();
    private readonly Camera _camera = new Camera();
    private readonly MatchManager _match;
    private readonly DX12Renderer _renderer;

    public LongShotApp(GameWindow window, DX12Renderer renderer) : base(window)
    {
        _renderer = renderer;
        _match = new MatchManager(_cueController, new CueBallSystem(_engine));
    }

    protected override void Update(float dt)
    {
        _camera.Update(Window.InputManager.State, _match.Mode, _engine.GetBallPosition(0), dt);
        _match.Update(_engine, Window.InputManager.State, _camera, dt);
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

        _renderer.Render(_camera, _engine, _match);
    }

    public override void Dispose()
    {
        base.Dispose();
        ImGuiManager.Shutdown();
        _engine?.Dispose();
    }
}