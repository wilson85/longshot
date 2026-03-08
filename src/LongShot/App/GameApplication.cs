using System.Diagnostics;

namespace LongShot.App;

public abstract class GameApplication(GameWindow window) : IDisposable
{
    protected GameWindow Window { get; } = window;

    public void Run()
    {
        Initialize();

        long lastTime = Stopwatch.GetTimestamp();
        double accumulator = 0.0;
        const double FixedDeltaTime = 1.0 / 120.0;

        while (Window.ProcessMessages())
        {
            long currentTime = Stopwatch.GetTimestamp();
            double frameTime = Stopwatch.GetElapsedTime(lastTime, currentTime).TotalSeconds;
            lastTime = currentTime;

            if (frameTime > 0.25)
            {
                frameTime = 0.25;
            }

            accumulator += frameTime;

            float dt = (float)frameTime;

            Update(dt);

            while (accumulator >= FixedDeltaTime)
            {
                FixedUpdate((float)FixedDeltaTime);
                accumulator -= FixedDeltaTime;
            }

            Draw();
        }
    }

    protected virtual void Initialize() { }
    protected virtual void Update(float dt) { }
    protected virtual void FixedUpdate(float dt) { }
    protected virtual void Draw() { }

    public virtual void Dispose()
    {
    }
}
