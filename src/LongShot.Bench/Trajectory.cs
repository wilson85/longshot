using System.Collections.Generic;
using System.Numerics;
using LongShot.Engine;

namespace LongShot.Bench;

/// <summary>Sampled trajectory of a single ball through one scenario.</summary>
public sealed class Trajectory
{
    public int BallId { get; }
    public BallType Type { get; }
    public List<Sample> Samples { get; } = new();

    public Trajectory(int ballId, BallType type)
    {
        BallId = ballId;
        Type = type;
    }

    public void Record(float time, in BallState state)
    {
        Samples.Add(new Sample
        {
            Time = time,
            Position = state.Position,
            LinearVelocity = state.LinearVelocity,
            AngularVelocity = state.AngularVelocity,
            State = state.State,
        });
    }

    public Vector3 InitialPosition => Samples.Count > 0 ? Samples[0].Position : Vector3.Zero;
    public Vector3 FinalPosition => Samples.Count > 0 ? Samples[^1].Position : Vector3.Zero;
    public Vector3 InitialAngularVelocity => Samples.Count > 0 ? Samples[0].AngularVelocity : Vector3.Zero;
    public MotionState FinalState => Samples.Count > 0 ? Samples[^1].State : MotionState.Stationary;

    public float MaxSpeed
    {
        get
        {
            float max = 0f;
            foreach (var s in Samples)
            {
                float spd = s.LinearVelocity.Length();
                if (spd > max) max = spd;
            }
            return max;
        }
    }

    /// <summary>Maximum Z value the ball reached at any point in its trajectory.</summary>
    public float MaxZ
    {
        get
        {
            float max = float.MinValue;
            foreach (var s in Samples) if (s.Position.Z > max) max = s.Position.Z;
            return max;
        }
    }

    /// <summary>Minimum Z value the ball reached at any point in its trajectory.</summary>
    public float MinZ
    {
        get
        {
            float min = float.MaxValue;
            foreach (var s in Samples) if (s.Position.Z < min) min = s.Position.Z;
            return min;
        }
    }

    public float TotalDistance
    {
        get
        {
            float d = 0f;
            for (int i = 1; i < Samples.Count; i++)
            {
                d += Vector3.Distance(Samples[i - 1].Position, Samples[i].Position);
            }
            return d;
        }
    }

    /// <summary>Horizontal travel angle in degrees (0° = +Z, +90° = +X), measured between two samples.</summary>
    public static float HeadingDegrees(Vector3 a, Vector3 b)
    {
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        if (dx * dx + dz * dz < 1e-10f) return float.NaN;
        return System.MathF.Atan2(dx, dz) * (180f / System.MathF.PI);
    }

    public struct Sample
    {
        public float Time;
        public Vector3 Position;
        public Vector3 LinearVelocity;
        public Vector3 AngularVelocity;
        public MotionState State;
    }
}
