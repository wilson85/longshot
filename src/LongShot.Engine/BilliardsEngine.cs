using System;
using System.Numerics;

namespace LongShot.Engine;

public sealed class BilliardsEngine
{
    public PhysicsConfig Config = PhysicsConfig.Default;

    private readonly BallState[] _physicsStates = new BallState[GameSettings.MaxBalls];

    private readonly System.Random _random;
    private float _accumulator;

    public ReadOnlySpan<BallState> PhysicsStates => _physicsStates.AsSpan(0, ActiveBallCount);
    public TableLayout TableLayout { get; } = new TableLayout();
    public int ActiveBallCount { get; private set; }

    /// <summary>
    /// Fraction of the next fixed step that the accumulator has already consumed.
    /// Use for visual interpolation: <c>lerp(prevState, currentState, InterpolationAlpha)</c>.
    /// </summary>
    public float InterpolationAlpha => _accumulator / GameSettings.FixedStep;

    public event Action<int, Vector3> OnBallPocketed;

    /// <summary>
    /// Fires immediately after a ball-ball collision is resolved, with the two ball IDs
    /// involved. Hosts use this to drive game rules (e.g. "what did the cue ball hit first
    /// this shot?", "was the legal target ball contacted?") without coupling the engine
    /// to any particular variant's rule set.
    /// </summary>
    public event Action<int, int> OnBallContact;

    /// <summary>Fires AFTER a cue strike has been applied. Args: cueBallId, aimDirectionRaw, force, hitOffset.</summary>
    public event Action<int, Vector3, float, Vector3> OnCueStrike;

    /// <summary>
    /// Fires AFTER a ball bounces off a rail cushion. Args: ballId, railSegmentIndex, impactSpeed (m/s,
    /// the magnitude of the velocity component along the rail normal at the moment of impact).
    /// </summary>
    public event Action<int, int, float> OnRailContact;

    /// <summary>Fires AFTER a ball clips a jaw corner. Args: ballId, jawCornerIndex, impactSpeed.</summary>
    public event Action<int, int, float> OnJawContact;

    /// <summary>Total simulated time consumed by <see cref="Tick"/>, in seconds. Useful as an event timestamp.</summary>
    public float TotalSimulatedTime { get; private set; }

    /// <param name="seed">Seed for deterministic simulation. Same seed + same inputs = identical results.</param>
    public BilliardsEngine(int seed = 0)
    {
        _random = new System.Random(seed);
    }

    /// <summary>Installs the table geometry and resets simulation state. Balls are NOT created - the host calls <see cref="AddBall"/> for each.</summary>
    public void InitializeMatch(CushionSegment[] rails, PocketBeam[] pockets)
    {
        TableLayout.LoadProceduralData(rails, pockets);
        _accumulator = 0f;
        ActiveBallCount = 0;
    }

    /// <summary>
    /// Adds a ball at the given position. Returns its assigned ID (cue ball is conventionally ID 0).
    /// The <see cref="BallType"/> is a host-side hint; the engine doesn't store it or branch on it.
    /// </summary>
    public int AddBall(Vector3 position, BallType type = BallType.Normal)
    {
        int id = ActiveBallCount++;
        _physicsStates[id] = new BallState { Position = position, State = MotionState.Stationary };
        return id;
    }

    /// <summary>
    /// Captures the current ball states (positions, velocities, spins, motion states) as a
    /// flat array. Use with <see cref="RestoreState"/> to roll the simulation back to a
    /// previous moment - essential for ML rollouts (try a shot, restore, try another).
    /// Does NOT include table geometry or the RNG state.
    /// </summary>
    public BallState[] SnapshotState() => _physicsStates.AsSpan(0, ActiveBallCount).ToArray();

    /// <summary>
    /// Overwrites the current ball states with those from <see cref="SnapshotState"/>.
    /// Ball count must match. The fixed-step accumulator is reset so the next Tick starts
    /// from a clean boundary.
    /// </summary>
    public void RestoreState(ReadOnlySpan<BallState> states)
    {
        if (states.Length != ActiveBallCount)
            throw new ArgumentException($"State length {states.Length} != ActiveBallCount {ActiveBallCount}", nameof(states));
        states.CopyTo(_physicsStates.AsSpan(0, ActiveBallCount));
        _accumulator = 0f;
    }

    /// <summary>
    /// Returns an independent engine instance sharing the same table layout, config, and
    /// current ball states. Use for parallel rollouts from a common starting position
    /// (e.g. Monte-Carlo tree search, batched RL policy evaluation). Event subscribers
    /// are NOT copied - the clone fires its own <see cref="OnBallPocketed"/>.
    /// </summary>
    public BilliardsEngine Clone(int seed = 0)
    {
        var copy = new BilliardsEngine(seed)
        {
            Config = this.Config,
        };
        copy.TableLayout.Rails = this.TableLayout.Rails;
        copy.TableLayout.Pockets = this.TableLayout.Pockets;
        copy.TableLayout.JawCorners = this.TableLayout.JawCorners;
        copy.ActiveBallCount = this.ActiveBallCount;
        this._physicsStates.AsSpan(0, ActiveBallCount).CopyTo(copy._physicsStates.AsSpan(0, ActiveBallCount));
        return copy;
    }

    /// <summary>
    /// Returns a pocketed ball to play at the given table-space position. Velocity and spin
    /// are zeroed and the motion state becomes <see cref="MotionState.Stationary"/>. Intended
    /// for host-driven game rules (cue-ball scratch respawn, foul re-spotting).
    /// </summary>
    public void RespawnBall(int id, Vector3 position)
    {
        ref BallState ball = ref _physicsStates[id];
        ball.Position = position;
        ball.LinearVelocity = Vector3.Zero;
        ball.AngularVelocity = Vector3.Zero;
        ball.State = MotionState.Stationary;
    }

    /// <summary>
    /// Strikes a ball with a cue stick, calculating off-center deflection (squirt) and spin.
    /// </summary>
    public void StrikeCueBall(int id, in Vector3 aimDirection, float force, in Vector3 hitOffset)
    {
        ref BallState ball = ref _physicsStates[id];

        Vector3 aimDir = Vector3.Normalize(aimDirection);

        float maxOffset = Config.Ball.Radius * Config.Cue.MiscueLimit;
        Vector3 safeOffset = hitOffset;
        if (safeOffset.LengthSquared() > maxOffset * maxOffset)
        {
            safeOffset = Vector3.Normalize(safeOffset) * maxOffset;
        }

        // Squirt rotates the velocity vector away from the offset side; it does NOT add energy.
        Vector3 offsetOnAimPlane = safeOffset - (Vector3.Dot(safeOffset, aimDir) * aimDir);
        Vector3 finalDirection = aimDir;

        if (offsetOnAimPlane.LengthSquared() > 0.00001f)
        {
            Vector3 squirtDirection = -Vector3.Normalize(offsetOnAimPlane);
            float offsetRatio = offsetOnAimPlane.Length() / Config.Ball.Radius;
            float squirtAngle = offsetRatio * Config.Cue.DeflectionMultiplier;
            finalDirection = Vector3.Normalize(
                (aimDir * MathF.Cos(squirtAngle)) + (squirtDirection * MathF.Sin(squirtAngle)));
        }

        Vector3 finalImpulse = finalDirection * force;
        Vector3 torqueOffset = safeOffset * Config.Cue.SpinEfficiency;

        PhysicsMath.ApplyImpulse(ref ball, finalImpulse, torqueOffset, Config.Ball.Mass, Config.Ball.Radius);

        ball.State = MotionState.Sliding;
        OnCueStrike?.Invoke(id, aimDirection, force, hitOffset);
    }

    public void Tick(float dt)
    {
        // Clamp absurdly long frames (system pause, debug break) to keep the accumulator
        // from spiralling and trying to simulate seconds of real time in one call.
        dt = MathF.Min(dt, GameSettings.MaxAccumulatedDt);

        _accumulator += dt;
        while (_accumulator >= GameSettings.FixedStep)
        {
            StepFixed(GameSettings.FixedStep);
            _accumulator -= GameSettings.FixedStep;
        }
    }

    private void StepFixed(float step)
    {
        float timeRemaining = step;
        int safetyNet = 0;

        while (timeRemaining > GameSettings.EventEpsilon && safetyNet++ < 200)
        {
            PhysicsEvent nextEvent = FindNextEvent(timeRemaining);
            float advanceTime = nextEvent.Type == EventType.None ? timeRemaining : nextEvent.Time;

            if (advanceTime > 0)
            {
                AdvancePositions(advanceTime);
                ApplyContinuousPhysics(advanceTime);
                timeRemaining -= advanceTime;
                TotalSimulatedTime += advanceTime;
            }

            if (nextEvent.Type != EventType.None)
            {
                ResolveEvent(in nextEvent);
            }
        }
    }

    private void AdvancePositions(float time)
    {
        Span<BallState> states = _physicsStates.AsSpan(0, ActiveBallCount);
        for (int i = 0; i < states.Length; i++)
        {
            ref BallState ball = ref states[i];
            if (ball.State == MotionState.Stationary || ball.State == MotionState.Pocketed)
            {
                continue;
            }
            ball.Position += ball.LinearVelocity * time;
        }
    }

    private void ApplyContinuousPhysics(float time)
    {
        Span<BallState> states = _physicsStates.AsSpan(0, ActiveBallCount);
        for (int i = 0; i < states.Length; i++)
        {
            TablePhysics.UpdateBallMotion(ref states[i], time, in Config);
        }
    }

    private PhysicsEvent FindNextEvent(float maxTime)
    {
        PhysicsEvent earliest = new PhysicsEvent(maxTime, EventType.None, -1);
        Span<BallState> states = _physicsStates.AsSpan(0, ActiveBallCount);

        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].State == MotionState.Pocketed)
            {
                continue;
            }

            for (int j = i + 1; j < states.Length; j++)
            {
                if (states[j].State == MotionState.Pocketed)
                {
                    continue;
                }
                if (states[i].State == MotionState.Stationary && states[j].State == MotionState.Stationary)
                {
                    continue;
                }

                float t = CollisionDetection.CalculateBallBallImpactTime(in states[i], in states[j]);
                if (t >= 0 && t < earliest.Time)
                {
                    earliest = new PhysicsEvent(t, EventType.BallBallCollision, i, j);
                }
            }

            if (states[i].State == MotionState.Stationary)
            {
                continue;
            }

            for (int r = 0; r < TableLayout.Rails.Length; r++)
            {
                float t = CollisionDetection.CalculateBallSegmentImpactTime(in states[i], in TableLayout.Rails[r]);
                if (t >= 0 && t < earliest.Time)
                {
                    earliest = new PhysicsEvent(t, EventType.BallCushionCollision, i, -1, r);
                }
            }

            for (int c = 0; c < TableLayout.JawCorners.Length; c++)
            {
                float t = CollisionDetection.CalculateBallPointImpactTime(in states[i], TableLayout.JawCorners[c]);
                if (t >= 0 && t < earliest.Time)
                {
                    earliest = new PhysicsEvent(t, EventType.BallJawCornerCollision, i, -1, c);
                }
            }

            for (int p = 0; p < TableLayout.Pockets.Length; p++)
            {
                float t = CollisionDetection.CalculatePocketCrossTime(in states[i], in TableLayout.Pockets[p], GameSettings.BallRadius);
                if (t >= 0 && t < earliest.Time)
                {
                    earliest = new PhysicsEvent(t, EventType.BallPocketed, i, -1, p);
                }
            }
        }
        return earliest;
    }

    private void ResolveEvent(in PhysicsEvent e)
    {
        ref BallState ballA = ref _physicsStates[e.BallA];
        switch (e.Type)
        {
            case EventType.BallBallCollision:
                TablePhysics.ResolveBallBallCollision(ref ballA, ref _physicsStates[e.BallB], in Config);
                OnBallContact?.Invoke(e.BallA, e.BallB);
                break;
            case EventType.BallCushionCollision:
            {
                // Capture pre-impact normal speed for the event payload.
                float impactSpeed = MathF.Abs(Vector3.Dot(ballA.LinearVelocity, TableLayout.Rails[e.CushionIndex].Normal));
                TablePhysics.ResolveCushionCollision(ref ballA, in TableLayout.Rails[e.CushionIndex], in Config);
                OnRailContact?.Invoke(e.BallA, e.CushionIndex, impactSpeed);
                break;
            }
            case EventType.BallJawCornerCollision:
            {
                Vector3 jaw = TableLayout.JawCorners[e.CushionIndex];
                Vector3 normal = Vector3.Normalize(ballA.Position - jaw);
                float impactSpeed = MathF.Abs(Vector3.Dot(ballA.LinearVelocity, normal));
                TablePhysics.ResolveJawCornerCollision(ref ballA, jaw, in Config);
                OnJawContact?.Invoke(e.BallA, e.CushionIndex, impactSpeed);
                break;
            }
            case EventType.BallPocketed:
                ResolvePocketed(e.BallA, ref ballA);
                break;
        }
    }

    private void ResolvePocketed(int id, ref BallState ball)
    {
        Vector3 dropPos = ball.Position;

        ball.LinearVelocity = Vector3.Zero;
        ball.AngularVelocity = Vector3.Zero;
        ball.State = MotionState.Pocketed;

        // Game rules (scratch, ball-in-hand, re-spotting the 8-ball, etc.) live in the host
        // scene. The host should subscribe to OnBallPocketed and call RespawnBall when appropriate.
        OnBallPocketed?.Invoke(id, dropPos);
    }

    public Vector3 GetBallPosition(int id) => _physicsStates[id].Position;

    public bool AreAllBallsAsleep()
    {
        for (int i = 0; i < ActiveBallCount; i++)
        {
            MotionState s = _physicsStates[i].State;
            if (s != MotionState.Stationary && s != MotionState.Pocketed)
            {
                return false;
            }
        }
        return true;
    }
}
