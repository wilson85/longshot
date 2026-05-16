# LongShot Physics Bench

Headless scenario runner for the billiards engine. Builds the WPA table, runs
scripted shots, asserts on outcomes, and writes a top-down PNG per scenario.

## Run

```pwsh
dotnet run --project src/LongShot.Bench/LongShot.Bench.csproj
```

Exits with code 0 if all scenarios pass, 1 otherwise. PNGs land in
`src/LongShot.Bench/out/`.

## Writing a scenario

Each scenario lives in its own file under `Scenarios/`. The pattern:

```csharp
public static ScenarioResult Run() => new Scenario("scenario_name",
        "One-line description shown in the PNG header")
    .PlaceCue(new Vector3(0, R, -0.4f))
    .PlaceObjectBall(new Vector3(0, R, 0.4f))
    .Strike(force: 0.5f, aim: Forward, offset: new Vector3(0, 0.5f * R, 0))
    .RunFor(0.5f)            // or .RunUntilRest()
    .Expect("Object ball is moving forward",
        s => s.ObjectTrajectory().FinalPosition.Z > 0.7f,
        s => $"object final Z = {s.ObjectTrajectory().FinalPosition.Z:0.000}")
    .Finish();
```

Then add `YourScenario.Run` to the `AllScenarios` array in `Program.cs`.

## Conventions

- **Y is up.** The table plane is XZ. Z is along the table length (toward
  foot rail = +Z, toward head = -Z). The cue ball is typically placed at
  `(0, R, -0.8)` (head spot).
- **`Strike` offset** is relative to the ball centre. `(0, +0.5R, 0)` is a
  high (follow) hit. `(0, -0.5R, 0)` is a low (draw) hit. `(0.5R, 0, 0)` is
  side English. Capped at `Cue.MiscueLimit * R`.
- **`Force`** is the cue impulse in N·s. Initial cue speed = `force / Ball.Mass`,
  so `force = 0.5` gives ~2.8 m/s, `force = 1.6` gives a hard break.
- **`RunFor(N)`** captures the immediate post-strike behaviour without
  rail-bounce chaos. **`RunUntilRest()`** runs to a settled state (use this
  when balls need to bounce around, e.g. break shots and pocket tests).
- Trajectory samples are recorded after every 0.008 s fixed step. Inspect with
  `Trajectory.Samples`, `MaxZ`, `MinZ`, `MaxSpeed`, `TotalDistance`,
  `FinalPosition`, `FinalState`.

## Reading the PNG

- White disc = cue ball at start. Yellow disc = object ball at start.
- Hollow ring = ball at end (only drawn if the ball moved).
- Trajectory polyline coloured by motion state:
  - **Red** = sliding
  - **Green** = rolling
  - **Blue** = airborne
- Black circles around the rails = pocket centres.

## Architecture

- `Scenario.cs` - fluent builder, owns the engine, records trajectories,
  exposes `Expect()`.
- `Trajectory.cs` - per-ball sample list with derived metrics.
- `BenchRenderer.cs` - SkiaSharp top-down PNG renderer.
- `Program.cs` - runs every scenario in order, prints summary, returns exit code.

The bench references `LongShot.Engine` directly. It has zero dependency on
the Evergine game project - so a passing bench means the engine is healthy
regardless of what's happening on the rendering side.
