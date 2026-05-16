using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using LongShot.Engine;
using LongShot.Shot;

namespace LongShot.Bench;

/// <summary>
/// Fluent scenario builder. Composes table setup, ball placement, the cue strike, the
/// simulation run, and assertions in one chain. Auto-renders a diagnostic PNG so the
/// trajectory is inspectable without launching the game.
/// </summary>
public sealed class Scenario
{
    public const float R = GameSettings.BallRadius;
    public const float DefaultMaxSimSeconds = 12f;
    public static readonly Vector3 Forward = new(0, 0, 1);

    public string Name { get; }
    public string Description { get; }
    public BilliardsEngine Engine { get; }
    public List<Trajectory> Trajectories { get; } = new();
    public List<(string Label, bool Pass, string Detail)> Assertions { get; } = new();
    public List<(int Id, Vector3 Where)> Pocketings { get; } = new();
    public float ElapsedSimTime { get; private set; }

    private int? _strikeSampleIndex;

    /// <summary>Always-on shot recorder, hooked into the engine before the strike.</summary>
    public ShotRecorder Recorder { get; }

    public Scenario(string name, string description = "")
    {
        Name = name;
        Description = description;
        Engine = new BilliardsEngine(seed: 0);
        var def = TableDefinition.BuildWpaStandard();
        var (rails, pockets) = TableBuilder.Build(def);
        Engine.InitializeMatch(rails, pockets);
        Engine.OnBallPocketed += (id, pos) => Pocketings.Add((id, pos));
        Recorder = new ShotRecorder(Engine);
    }

    public Scenario PlaceCue(Vector3 position)
    {
        int id = Engine.AddBall(position, BallType.Cue);
        Trajectories.Add(new Trajectory(id, BallType.Cue));
        return this;
    }

    public Scenario PlaceObjectBall(Vector3 position)
    {
        int id = Engine.AddBall(position, BallType.Normal);
        Trajectories.Add(new Trajectory(id, BallType.Normal));
        return this;
    }

    /// <summary>
    /// Reserves engine ball ids 1..targetId-1 by placing them off the table, so the next
    /// <see cref="PlaceObjectBall"/> gets the desired engine id. Useful for 8-ball rules
    /// tests where the rules layer cares about the id-to-group convention (id 8 = the
    /// eight, etc).
    /// </summary>
    public Scenario PadBallIdsTo(int nextDesiredId)
    {
        while (Engine.ActiveBallCount < nextDesiredId)
        {
            // Place a placeholder ball off the table, then immediately set its state to
            // Pocketed so it's invisible to physics and to the rules layer.
            var offTable = new Vector3(999f + Engine.ActiveBallCount * 0.1f, R, 999f);
            int id = Engine.AddBall(offTable, BallType.Normal);
            Engine.RespawnBall(id, offTable);
            // Mark pocketed via the snapshot/restore trick.
            var snap = Engine.SnapshotState();
            snap[id].State = LongShot.Engine.MotionState.Pocketed;
            Engine.RestoreState(snap);
            Trajectories.Add(new Trajectory(id, BallType.Normal));
        }
        return this;
    }

    /// <summary>Sample initial state then apply the strike. Records the strike sample index for rendering arrows.</summary>
    public Scenario Strike(float force, Vector3 aim, Vector3 offset)
    {
        SampleAll(0f);
        _strikeSampleIndex = Trajectories[0].Samples.Count - 1;
        Engine.StrikeCueBall(0, aim, force, offset);
        return this;
    }

    /// <summary>Strike with no offset (centre-ball hit).</summary>
    public Scenario Strike(float force, Vector3 aim) => Strike(force, aim, Vector3.Zero);

    /// <summary>
    /// Tick the engine in fixed steps until every ball is at rest, or the timeout fires.
    /// Records one trajectory sample per fixed step.
    /// </summary>
    public Scenario RunUntilRest(float maxSeconds = DefaultMaxSimSeconds)
    {
        const float dt = GameSettings.FixedStep;
        while (!Engine.AreAllBallsAsleep() && ElapsedSimTime < maxSeconds)
        {
            Engine.Tick(dt);
            ElapsedSimTime += dt;
            SampleAll(ElapsedSimTime);
            Recorder.Sample();
        }
        return this;
    }

    /// <summary>
    /// Tick the engine for exactly <paramref name="seconds"/> of simulated time. Stops early
    /// if all balls come to rest. Use this instead of <see cref="RunUntilRest"/> to capture
    /// the immediate post-strike behaviour before rail bounces re-shuffle everything.
    /// </summary>
    public Scenario RunFor(float seconds)
    {
        const float dt = GameSettings.FixedStep;
        float deadline = ElapsedSimTime + seconds;
        while (!Engine.AreAllBallsAsleep() && ElapsedSimTime < deadline)
        {
            Engine.Tick(dt);
            ElapsedSimTime += dt;
            SampleAll(ElapsedSimTime);
            Recorder.Sample();
        }
        return this;
    }

    private void SampleAll(float time)
    {
        var states = Engine.PhysicsStates;
        for (int i = 0; i < Trajectories.Count; i++)
        {
            Trajectories[i].Record(time, in states[i]);
        }
    }

    /// <summary>Hook for inline diagnostic prints between fluent calls.</summary>
    public Scenario Then(Action<Scenario> action) { action(this); return this; }

    public Trajectory Trajectory(int ballId) => Trajectories[ballId];
    public Trajectory CueTrajectory => Trajectories[0];
    public Trajectory ObjectTrajectory(int slot = 1) => Trajectories[slot];
    public int? StrikeSampleIndex => _strikeSampleIndex;

    public Scenario Expect(string label, Func<Scenario, bool> predicate, Func<Scenario, string>? detail = null)
    {
        bool pass;
        string detailText;
        try
        {
            pass = predicate(this);
            detailText = detail?.Invoke(this) ?? string.Empty;
        }
        catch (Exception ex)
        {
            pass = false;
            detailText = $"threw {ex.GetType().Name}: {ex.Message}";
        }
        Assertions.Add((label, pass, detailText));
        return this;
    }

    public ScenarioResult Finish()
    {
        EnsureOutputDir();
        string png = Path.Combine(OutputDir, $"{Name}.png");
        BenchRenderer.Render(this, png);
        return new ScenarioResult(this, png);
    }

    public static string OutputDir { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out"));
    private static void EnsureOutputDir() => Directory.CreateDirectory(OutputDir);
}

public sealed class ScenarioResult
{
    public Scenario Scenario { get; }
    public string ImagePath { get; }
    public bool AllPassed { get; }

    public ScenarioResult(Scenario scenario, string imagePath)
    {
        Scenario = scenario;
        ImagePath = imagePath;
        AllPassed = scenario.Assertions.TrueForAll(a => a.Pass);
    }
}
