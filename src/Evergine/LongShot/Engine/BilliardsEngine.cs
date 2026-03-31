using System;
using System.Numerics;
using Longshot.Engine;

namespace LongShot.Engine;

/// <summary>
/// Central manager for the billiards simulation. Controls the timeline, 
/// state arrays, and delegates physical math to dedicated modules.
/// </summary>
public sealed class BilliardsEngine
{
    public PhysicsConfig Config = PhysicsConfig.Default;

    private readonly BallState[] _physicsStates = new BallState[GameSettings.MaxBalls];
    private readonly BallRenderData[] _renderData = new BallRenderData[GameSettings.MaxBalls];

    public ReadOnlySpan<BallState> PhysicsStates => _physicsStates.AsSpan(0, ActiveBallCount);
    public ReadOnlySpan<BallRenderData> RenderData => _renderData.AsSpan(0, ActiveBallCount);
    public TableLayout TableLayout { get; } = new TableLayout();
    public int ActiveBallCount { get; private set; }

    public event Action<int, Vector3> OnBallPocketed;

    public void InitializeMatch(CushionSegment[] rails, PocketBeam[] pockets)
    {
        TableLayout.LoadProceduralData(rails, pockets);
        CreateBalls();
    }

    private void CreateBalls()
    {
        AddBall(new Vector3(0, Config.Ball.Radius, -0.8f), BallType.Cue);

        float spacing = Config.Ball.Radius * 2.001f; 
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c <= r; c++)
            {
                float jitterX = (float)(System.Random.Shared.NextDouble() - 0.5) * 0.0001f;
                float jitterZ = (float)(System.Random.Shared.NextDouble() - 0.5) * 0.0001f;

                Vector3 pos = new Vector3(
                    ((c - (r * 0.5f)) * spacing) + jitterX,
                    Config.Ball.Radius,
                    0.8f + (r * spacing * 0.866f) + jitterZ
                );

                AddBall(pos, BallType.Normal);
            }
        }
    }

    private void AddBall(Vector3 position, BallType type)
    {
        int id = ActiveBallCount++;
        _physicsStates[id] = new BallState { Position = position, State = MotionState.Stationary };
        _renderData[id] = new BallRenderData
        {
            Id = id,
            Type = type,
            LastTrailPosition = position,
            MotionState = MotionState.Stationary
        };
    }

    /// <summary>
    /// Strikes a ball with a cue stick, calculating off-center deflection (squirt) and spin.
    /// </summary>
    public void StrikeCueBall(int id, in Vector3 aimDirection, float force, in Vector3 hitOffset)
    {
        ref BallState ball = ref _physicsStates[id];

        float maxOffset = Config.Ball.Radius * Config.Cue.MiscueLimit;
        Vector3 safeOffset = hitOffset;
        if (safeOffset.LengthSquared() > maxOffset * maxOffset)
        {
            safeOffset = Vector3.Normalize(safeOffset) * maxOffset;
        }

        Vector3 baseImpulse = Vector3.Normalize(aimDirection) * force;
        Vector3 offsetOnAimPlane = safeOffset - (Vector3.Dot(safeOffset, aimDirection) * aimDirection);
        Vector3 finalImpulse = baseImpulse;

        if (offsetOnAimPlane.LengthSquared() > 0.00001f)
        {
            Vector3 squirtDirection = -Vector3.Normalize(offsetOnAimPlane);
            float offsetRatio = offsetOnAimPlane.Length() / Config.Ball.Radius;
            float squirtMagnitude = force * offsetRatio * Config.Cue.DeflectionMultiplier;
            finalImpulse += squirtDirection * squirtMagnitude;
        }

        Vector3 torqueOffset = safeOffset * Config.Cue.SpinEfficiency;

        PhysicsMath.ApplyImpulse(ref ball, finalImpulse, torqueOffset, Config.Ball.Mass, Config.Ball.Radius);

        ball.State = MotionState.Sliding;
        //RetroAudio.PlayCueImpact(force * 3f, ball.Position);
    }

    public void Tick(float dt)
    {
        float timeRemaining = dt;
        int safetyNet = 0;

        while (timeRemaining > GameSettings.EventEpsilon && safetyNet++ < 200)
        {
            float step = MathF.Min(timeRemaining, GameSettings.MaxPhysicsStep);

            PhysicsEvent nextEvent = FindNextEvent(step);
            float advanceTime = nextEvent.Type == EventType.None ? step : nextEvent.Time;

            if (advanceTime > 0)
            {
                AdvancePositions(advanceTime);
                ApplyContinuousPhysics(advanceTime);
                timeRemaining -= advanceTime;
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
            if (ball.State != MotionState.Stationary)
            {
                ball.Position += ball.LinearVelocity * time;
            }
        }
    }

    private void ApplyContinuousPhysics(float time)
    {
        Span<BallState> states = _physicsStates.AsSpan(0, ActiveBallCount);
        for (int i = 0; i < states.Length; i++)
        {
            // Fully decoupled physics routines parameterized by Config
            TablePhysics.UpdateBallMotion(ref states[i], time, in Config);
        }
    }

    private PhysicsEvent FindNextEvent(float maxTime)
    {
        PhysicsEvent earliest = new PhysicsEvent(maxTime, EventType.None, -1);
        Span<BallState> states = _physicsStates.AsSpan(0, ActiveBallCount);

        for (int i = 0; i < states.Length; i++)
        {
            for (int j = i + 1; j < states.Length; j++)
            {
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
                break;
            case EventType.BallCushionCollision:
                TablePhysics.ResolveCushionCollision(ref ballA, in TableLayout.Rails[e.CushionIndex], in Config);
                break;
            case EventType.BallJawCornerCollision:
                TablePhysics.ResolveJawCornerCollision(ref ballA, TableLayout.JawCorners[e.CushionIndex], in Config);
                break;
            case EventType.BallPocketed:
                ResolvePocketed(e.BallA, ref ballA);
                break;
        }
    }

    private void ResolvePocketed(int id, ref BallState ball)
    {
        Vector3 dropPos = ball.Position;
        float speed = ball.LinearVelocity.Length();

        ball.State = MotionState.Stationary;
        ball.LinearVelocity = Vector3.Zero;
        ball.AngularVelocity = Vector3.Zero;

        if (id == 0) // Cue Ball Reset
        {
            ball.Position = new Vector3(0, Config.Ball.Radius, -0.8f);
        }
        else
        {
            ball.Position = new Vector3(999f, -999f, 999f);
        }

        //RetroAudio.PlayPocketDrop(dropPos, speed);
        OnBallPocketed?.Invoke(id, dropPos);
    }

    public TableSnapshot TakeSnapshot() { return new TableSnapshot(); }
    public void RestoreSnapshot(TableSnapshot snap) { }
    public Vector3 GetBallPosition(int id) => _physicsStates[id].Position;

    public bool AreAllBallsAsleep()
    {
        for (int i = 0; i < ActiveBallCount; i++)
        {
            if (_physicsStates[i].State != MotionState.Stationary)
            {
                return false;
            }
        }
        return true;
    }
}