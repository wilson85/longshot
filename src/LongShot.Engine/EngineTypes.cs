using System;
using System.Numerics;

namespace LongShot.Engine;

public enum MotionState : byte
{
    Stationary = 0,
    Sliding = 1,
    Rolling = 2,
    Airborne = 3,
    Pocketed = 4,
}

public enum EventType : byte
{
    None = 0,
    StateTransition = 1,
    BallBallCollision = 2,
    BallCushionCollision = 3,
    BallSlateBounce = 4,
    BallJawCornerCollision = 5,
    BallPocketed = 6,
}

/// <summary>
/// Optional hint passed when adding a ball - lets the host distinguish the cue ball from
/// numbered balls without bookkeeping a separate map. The engine itself doesn't branch on
/// this; physics is identical for every ball.
/// </summary>
public enum BallType { Cue, Normal }

public readonly struct PhysicsEvent : IComparable<PhysicsEvent>
{
    public readonly float Time;
    public readonly EventType Type;
    public readonly int BallA;
    public readonly int BallB;
    public readonly int CushionIndex;

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
