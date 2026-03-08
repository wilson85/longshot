using LongShot.Engine;

namespace LongShot;

public sealed class MatchManager
{
    public GameStateMode Mode { get; private set; } = GameStateMode.Aim;

    readonly CueController _cue;
    readonly CueBallSystem _cueBall;

    public float CueStickOffset => _cue.CueOffset;

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
                UpdatePower(input, camera, deltaTime);
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

    void UpdatePower(
        InputState input,
        Camera camera,
        float dt)
    {
        var result = _cue.UpdateStroke(input, dt);

        if (result == ShotResult.Cancel)
        {
            Mode = GameStateMode.Aim;
            return;
        }

        if (result == ShotResult.Strike)
        {
            var shot = _cue.BuildShot(camera);

            _cueBall.ApplyShot(shot);

            Mode = GameStateMode.Simulate;
        }
    }
}