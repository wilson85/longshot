using System.Numerics;
using BepuPhysics;
using LongShot.Engine;

namespace LongShot;


public sealed class BallData
{
    public int Id;
    public BallType Type;
    public float Mass;

    public BodyHandle PhysicsHandle;

    public float LastSpeed;

    const int TrailSize = 150;

    readonly Vector3[] _trail = new Vector3[TrailSize];

    int _trailIndex;

    public ReadOnlySpan<Vector3> Trail => _trail;

    public void AddTrail(Vector3 p)
    {
        _trail[_trailIndex] = p;
        _trailIndex++;

        if (_trailIndex == TrailSize)
            _trailIndex = 0;
    }
}