using LongShot.Engine;

namespace LongShot.App;

public sealed class LongShotApp(GameWindow window, DX12Renderer renderer) : GameApplication(window)
{
    private readonly BilliardsEngine _engine = new BilliardsEngine();
    private readonly MatchManager _match = new MatchManager();
    private readonly Camera _camera = new Camera();

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

        renderer.Render(_camera, _engine, _match);
    }

    public override void Dispose()
    {
        base.Dispose();
        ImGuiManager.Shutdown();
        _engine?.Dispose();
    }
}