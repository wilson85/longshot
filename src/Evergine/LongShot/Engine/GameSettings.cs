namespace Longshot.Engine;

public static class GameSettings
{
    public const int MaxBalls = 25;

    public const float TableWidth = 1.33f;

    public const float TableLength = 2.33f;

    public const float Gravity = 9.81f;

    public const float EventEpsilon = 1e-5f;

    public const float MaxPhysicsStep = 0.008f; // ~120hz physics base

    public const float BallRadius = 0.028575f;

    public const float BallMass = 0.180f;

    public const float MaxImpactSpeed = 1000.0f;
}
