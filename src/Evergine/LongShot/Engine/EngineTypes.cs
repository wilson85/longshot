using System;
using System.Numerics;
using Longshot.Engine;

namespace LongShot.Engine;

public enum MotionState : byte { Stationary = 0, Sliding = 1, Rolling = 2, Airborne = 3 }
public enum EventType : byte { None = 0, StateTransition = 1, BallBallCollision = 2, BallCushionCollision = 3, BallSlateBounce = 4, BallJawCornerCollision = 5, BallPocketed = 6 }
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
        Time = time; Type = type; BallA = ballA; BallB = ballB; CushionIndex = cushionIndex;
    }
    public int CompareTo(PhysicsEvent other) => Time.CompareTo(other.Time);
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
    public required MotionState MotionState;
}

public class TableSnapshot
{
    public BallState[] PhysicsStates = new BallState[25];
    public Quaternion[] Orientations = new Quaternion[25];
}