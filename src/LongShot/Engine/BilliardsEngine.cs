using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace LongShot.Engine;

// ==========================================
// 1. PURE PHYSICS DATA (AOT & Cache Friendly)
// ==========================================

public enum MotionState : byte
{
    Stationary = 0,
    Sliding = 1,
    Rolling = 2,
    Airborne = 3
}

public enum EventType : byte
{
    None = 0,
    StateTransition = 1, // Sliding -> Rolling -> Stationary
    BallBallCollision = 2,
    BallCushionCollision = 3,
    BallSlateBounce = 4
}

// Struct of purely mathematical state. No visual data here.
[StructLayout(LayoutKind.Sequential)]
public struct BallState
{
    public Vector3 Position;
    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;
    public MotionState State;
}

public readonly struct Cushion
{
    public readonly Vector3 Normal;
    public readonly float DistanceFromCenter;

    public Cushion(Vector3 normal, float distance)
    {
        Normal = Vector3.Normalize(normal);
        DistanceFromCenter = distance;
    }
}

public readonly struct PhysicsEvent : IComparable<PhysicsEvent>
{
    public readonly float Time;
    public readonly EventType Type;
    public readonly int BallA;
    public readonly int BallB; // -1 if not applicable
    public readonly int CushionIndex; // -1 if not applicable

    public PhysicsEvent(float time, EventType type, int ballA, int ballB = -1, int cushionIndex = -1)
    {
        Time = time;
        Type = type;
        BallA = ballA;
        BallB = ballB;
        CushionIndex = cushionIndex;
    }

    public int CompareTo(PhysicsEvent other) => Time.CompareTo(other.Time);
}

// ==========================================
// 2. RENDER & TRACKING DATA
// ==========================================

public struct TrailPoint
{
    public Vector3 Position;
    public float Spin;
    public Vector3 Direction;
}

public class BallRenderData
{
    public int Id;
    public BallType Type;
    public Quaternion Orientation = Quaternion.Identity;

    public TrailPoint[] Trail = new TrailPoint[128];
    public int TrailIndex = 0;
    public Vector3 LastTrailPosition;

    public Vector3[] HitMarksWorld = new Vector3[8];
    public int HitMarkIndex = 0;
    public Vector3? ChalkMarkLocal;
}

public enum BallType { Cue, Normal }

public class TableSnapshot
{
    public BallState[] PhysicsStates = new BallState[16];
    public Quaternion[] Orientations = new Quaternion[16];
}

public interface IPhysicsAudioListener
{
    void PlayCueImpact(float force, Vector3 position);
    void PlayBallImpact(float force, Vector3 position, float spinMagnitude);
    void PlayRailImpact(float force, Vector3 position);
}

// ==========================================
// 3. THE ANALYTICAL PHYSICS ENGINE
// ==========================================

public sealed class BilliardsEngine
{
    public const float TableWidth = 1.27f;
    public const float TableLength = 2.54f;
    public const float BallRadius = 0.028575f;
    public const float BallMass = 0.170f;

    private const int MaxBalls = 16;
    private const float EventEpsilon = 1e-5f; // Prevent infinite event loops from floating point errors

    // State Arrays (Data-Oriented)
    private readonly BallState[] _physicsStates = new BallState[MaxBalls];
    private readonly BallRenderData[] _renderData = new BallRenderData[MaxBalls];
    private readonly Cushion[] _cushions = new Cushion[4];

    private int _activeBallCount = 0;
    private IPhysicsAudioListener? _audioListener;

    public ReadOnlySpan<BallState> PhysicsStates => new ReadOnlySpan<BallState>(_physicsStates, 0, _activeBallCount);
    public IReadOnlyList<BallRenderData> RenderData => _renderData;

    public BilliardsEngine(IPhysicsAudioListener? audioListener = null)
    {
        _audioListener = audioListener;
        CreateTable();
        CreateBalls();
    }

    public bool AreAllBallsAsleep()
    {
        for (int i = 0; i < _activeBallCount; i++)
        {
            if (_physicsStates[i].State != MotionState.Stationary)
            {
                return false;
            }
        }
        return true;
    }

    public Vector3 GetBallPosition(int id) => _physicsStates[id].Position;

    private void CreateTable()
    {
        // Define cushions as infinite planes for mathematical intersection
        float halfWidth = TableWidth / 2f;
        float halfLength = TableLength / 2f;

        _cushions[0] = new Cushion(new Vector3(1, 0, 0), halfWidth);  // Left cushion (normal points right)
        _cushions[1] = new Cushion(new Vector3(-1, 0, 0), halfWidth); // Right cushion (normal points left)
        _cushions[2] = new Cushion(new Vector3(0, 0, 1), halfLength); // Bottom cushion
        _cushions[3] = new Cushion(new Vector3(0, 0, -1), halfLength);// Top cushion
    }

    private void CreateBalls()
    {
        AddBall(new Vector3(0, BallRadius, -0.8f), BallType.Cue);

        float spacing = BallRadius * 2.01f;
        int rows = 5;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c <= r; c++)
            {
                Vector3 pos = new Vector3((c - (r * 0.5f)) * spacing, BallRadius, 0.8f + (r * spacing * 0.866f));
                AddBall(pos, BallType.Normal);
            }
        }
    }

    private void AddBall(Vector3 position, BallType type)
    {
        int id = _activeBallCount++;

        _physicsStates[id] = new BallState
        {
            Position = position,
            LinearVelocity = Vector3.Zero,
            AngularVelocity = Vector3.Zero,
            State = MotionState.Stationary
        };

        _renderData[id] = new BallRenderData
        {
            Id = id,
            Type = type,
            LastTrailPosition = position
        };
    }

    public void ApplyImpulse(int id, Vector3 impulse, Vector3 hitOffset)
    {
        ref BallState ball = ref _physicsStates[id];

        // Convert physical impulse into linear and angular velocity
        ball.LinearVelocity += impulse / BallMass;

        // Simplified inertia tensor for a solid sphere: I = 2/5 * m * r^2
        float inertia = 0.4f * BallMass * (BallRadius * BallRadius);
        Vector3 torque = Vector3.Cross(hitOffset, impulse);
        ball.AngularVelocity += torque / inertia;

        ball.State = MotionState.Sliding;

        _audioListener?.PlayCueImpact(impulse.Length() * 3f, ball.Position);
    }

    // ==========================================
    // THE EVENT-BASED LOOP
    // ==========================================

    public void Tick(float dt)
    {
        float timeRemaining = dt;
        int safetyNet = 0;

        // 1. ANALYTICAL SOLVER
        // Process exact events until the frame delta time is consumed
        while (timeRemaining > EventEpsilon && safetyNet++ < 100)
        {
            PhysicsEvent nextEvent = FindNextEvent(timeRemaining);

            if (nextEvent.Type == EventType.None)
            {
                // No events happened this frame. Fast forward all balls to the end of the frame.
                FastForwardState(timeRemaining);
                break;
            }

            // An event happened! Move time exactly to the moment of impact.
            FastForwardState(nextEvent.Time);
            timeRemaining -= nextEvent.Time;

            // Resolve the event (change velocities, swap states)
            ResolveEvent(nextEvent);
        }

        // 2. VISUAL/RENDERER UPDATES
        // Update Quaternions, hit marks, and trails based on the final solved state
        UpdateVisuals(dt);
    }

    private void FastForwardState(float time)
    {
        if (time <= 0) return;

        Span<BallState> states = _physicsStates.AsSpan(0, _activeBallCount);

        // TODO: In a full implementation, this uses analytical equations 
        // to curve the position based on sliding friction.
        // For now, it's linear constant-velocity integration.
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].State != MotionState.Stationary)
            {
                states[i].Position += states[i].LinearVelocity * time;

                // Very basic rolling friction deceleration placeholder
                if (states[i].LinearVelocity.LengthSquared() > 0)
                {
                    Vector3 decel = Vector3.Normalize(states[i].LinearVelocity) * (0.8f * time);
                    if (decel.LengthSquared() > states[i].LinearVelocity.LengthSquared())
                    {
                        states[i].LinearVelocity = Vector3.Zero;
                        states[i].State = MotionState.Stationary;
                    }
                    else
                    {
                        states[i].LinearVelocity -= decel;
                    }
                }
            }
        }
    }

    // ==========================================
    // CONTINUOUS COLLISION DETECTION (CCD)
    // ==========================================

    private PhysicsEvent FindNextEvent(float maxTime)
    {
        PhysicsEvent earliestEvent = new PhysicsEvent(maxTime, EventType.None, -1);
        Span<BallState> states = _physicsStates.AsSpan(0, _activeBallCount);

        // 1. Check Ball-Ball collisions
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].State == MotionState.Stationary) continue;

            for (int j = i + 1; j < states.Length; j++)
            {
                float t = CalculateBallBallImpactTime(in states[i], in states[j]);
                // FIX: Changed from t > EventEpsilon to t >= 0 so we don't ignore overlapping balls
                if (t >= 0 && t < earliestEvent.Time)
                {
                    earliestEvent = new PhysicsEvent(t, EventType.BallBallCollision, i, j);
                }
            }

            // 2. Check Ball-Cushion collisions
            for (int c = 0; c < 4; c++)
            {
                float t = CalculateBallCushionImpactTime(in states[i], in _cushions[c]);
                // FIX: Changed from t > EventEpsilon to t >= 0
                if (t >= 0 && t < earliestEvent.Time)
                {
                    earliestEvent = new PhysicsEvent(t, EventType.BallCushionCollision, i, -1, c);
                }
            }

            // TODO: 3. Check Sliding -> Rolling transitions (Time to Grip)
        }

        return earliestEvent;
    }

    private float CalculateBallBallImpactTime(in BallState a, in BallState b)
    {
        Vector3 deltaP = a.Position - b.Position;
        Vector3 deltaV = a.LinearVelocity - b.LinearVelocity;

        // If they are moving away from each other, no collision
        if (Vector3.Dot(deltaP, deltaV) >= 0) return float.PositiveInfinity;

        // Quadratic equation: a*t^2 + b*t + c = 0
        float aQuad = deltaV.LengthSquared();
        float bQuad = 2.0f * Vector3.Dot(deltaP, deltaV);
        float cQuad = deltaP.LengthSquared() - (4.0f * BallRadius * BallRadius);

        float discriminant = (bQuad * bQuad) - (4.0f * aQuad * cQuad);

        if (discriminant < 0) return float.PositiveInfinity; // They miss each other

        // Find the smallest positive root
        float t = (-bQuad - MathF.Sqrt(discriminant)) / (2.0f * aQuad);
        return t >= 0 ? t : float.PositiveInfinity;
    }

    private float CalculateBallCushionImpactTime(in BallState ball, in Cushion cushion)
    {
        float velocityTowardsCushion = Vector3.Dot(ball.LinearVelocity, cushion.Normal);

        // If moving parallel or away from the cushion, no impact
        if (velocityTowardsCushion >= 0) return float.PositiveInfinity;

        // FIX: The correct plane distance equation. 
        // This calculates the exact distance from the ball's center to the cushion plane
        float distanceToPlane = Vector3.Dot(ball.Position, cushion.Normal) + cushion.DistanceFromCenter;

        // Subtract the radius because the edge of the ball hits the cushion, not the center
        float distanceRemaining = distanceToPlane - BallRadius;

        // Safety check: if the ball is already touching or slightly inside due to floating point math
        if (distanceRemaining <= 0) return 0f;

        // Time = distance / closing speed
        return distanceRemaining / -velocityTowardsCushion;
    }

    // ==========================================
    // EVENT RESOLVERS
    // ==========================================

    private void ResolveEvent(in PhysicsEvent e)
    {
        ref BallState ballA = ref _physicsStates[e.BallA];

        switch (e.Type)
        {
            case EventType.BallBallCollision:
                ref BallState ballB = ref _physicsStates[e.BallB];
                ResolveBallBall(e.BallA, e.BallB, ref ballA, ref ballB);
                break;

            case EventType.BallCushionCollision:
                ResolveBallCushion(ref ballA, in _cushions[e.CushionIndex]);
                break;
        }
    }

    private void ResolveBallBall(int idA, int idB, ref BallState a, ref BallState b)
    {
        Vector3 normal = Vector3.Normalize(b.Position - a.Position);
        Vector3 relativeVelocity = b.LinearVelocity - a.LinearVelocity;

        float velocityAlongNormal = Vector3.Dot(relativeVelocity, normal);
        if (velocityAlongNormal > 0) return; // Already separating

        float restitution = 0.98f;
        float impulseScalar = -(1 + restitution) * velocityAlongNormal;
        impulseScalar /= (1 / BallMass) + (1 / BallMass);

        Vector3 impulse = impulseScalar * normal;

        a.LinearVelocity -= impulse / BallMass;
        b.LinearVelocity += impulse / BallMass;

        a.State = MotionState.Sliding;
        b.State = MotionState.Sliding;

        // Audio and hitmarks
        Vector3 hitPos = (a.Position + b.Position) / 2f;
        _renderData[idA].HitMarksWorld[_renderData[idA].HitMarkIndex++] = hitPos;
        _renderData[idA].HitMarkIndex %= 8;

        _audioListener?.PlayBallImpact(impulse.Length(), hitPos, a.AngularVelocity.Length());
    }

    private void ResolveBallCushion(ref BallState ball, in Cushion cushion)
    {
        float velocityAlongNormal = Vector3.Dot(ball.LinearVelocity, cushion.Normal);
        if (velocityAlongNormal > 0) return;

        float restitution = 0.85f;
        Vector3 impulse = -(1 + restitution) * velocityAlongNormal * cushion.Normal;

        ball.LinearVelocity += impulse;

        _audioListener?.PlayRailImpact(impulse.Length(), ball.Position);
    }

    // ==========================================
    // VISUALS & SAVING
    // ==========================================

    private void UpdateVisuals(float dt)
    {
        for (int i = 0; i < _activeBallCount; i++)
        {
            ref BallState phys = ref _physicsStates[i];
            BallRenderData render = _renderData[i];

            if (phys.State == MotionState.Stationary) continue;

            // 1. Integrate visual rotation (Quaternions)
            if (phys.AngularVelocity.LengthSquared() > 0.0001f)
            {
                float spinMagnitude = phys.AngularVelocity.Length();
                Vector3 spinAxis = phys.AngularVelocity / spinMagnitude;
                Quaternion frameRotation = Quaternion.CreateFromAxisAngle(spinAxis, spinMagnitude * dt);
                render.Orientation = Quaternion.Normalize(frameRotation * render.Orientation);
            }

            // 2. Trail generation
            float distanceTraveled = Vector3.Distance(phys.Position, render.LastTrailPosition);
            if (distanceTraveled > 0.02f)
            {
                render.Trail[render.TrailIndex] = new TrailPoint
                {
                    Position = new Vector3(phys.Position.X, -0.02f, phys.Position.Z),
                    Spin = phys.AngularVelocity.Length(),
                    Direction = phys.LinearVelocity.LengthSquared() > 0 ? Vector3.Normalize(phys.LinearVelocity) : Vector3.UnitZ
                };

                render.TrailIndex = (render.TrailIndex + 1) % render.Trail.Length;
                render.LastTrailPosition = phys.Position;
            }
        }
    }

    public TableSnapshot TakeSnapshot()
    {
        var snap = new TableSnapshot();
        for (int i = 0; i < _activeBallCount; i++)
        {
            snap.PhysicsStates[i] = _physicsStates[i];
            snap.Orientations[i] = _renderData[i].Orientation;
        }
        return snap;
    }

    public void RestoreSnapshot(TableSnapshot snap)
    {
        for (int i = 0; i < _activeBallCount; i++)
        {
            _physicsStates[i] = snap.PhysicsStates[i];
            _renderData[i].Orientation = snap.Orientations[i];
            _renderData[i].TrailIndex = 0;
            _renderData[i].LastTrailPosition = snap.PhysicsStates[i].Position;
            Array.Clear(_renderData[i].Trail);
        }
    }
}