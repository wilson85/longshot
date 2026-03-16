namespace LongShot.Engine;

public static class BilliardsConstants
{
    // Table Dimensions
    public const float TableWidth = 1.33f;
    public const float TableLength = 2.33f;

    // Ball Properties
    public const float BallRadius = 0.043f;
    public const float BallMass = 0.180f;

    // Calculated inertia for a solid sphere: 2/5 * m * r^2
    public const float BallInertia = 0.4f * BallMass * (BallRadius * BallRadius);

    // World Properties
    public const float Gravity = 9.81f;

    // Engine Limitations
    public const int MaxBalls = 16;
    public const float EventEpsilon = 1e-5f;
    public const float MaxPhysicsStep = 0.008f; // ~120hz physics base
}
