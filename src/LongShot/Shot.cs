using System.Numerics;

namespace LongShot;

public readonly struct Shot
{
    public readonly Vector3 Direction;
    public readonly float Power;
    public readonly Vector2 TipOffset;

    public Shot(Vector3 dir, float power, Vector2 tip)
    {
        Direction = dir;
        Power = power;
        TipOffset = tip;
    }
}
