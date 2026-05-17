namespace LongShot.Engine;

public static class GameSettings
{
    public const int MaxBalls = 25;

    public const float TableWidth = 1.33f;
    public const float TableLength = 2.33f;
    public const float Gravity = 9.81f;

    public const float EventEpsilon = 1e-5f;

    public const float MaxPhysicsStep = 0.008f; // ~120Hz physics base

    /// <summary>Quantum of the deterministic simulation tick. 125 Hz.</summary>
    public const float FixedStep = 0.008f;

    /// <summary>Maximum real time consumed by a single <c>Tick(dt)</c> call (prevents spiral of death).</summary>
    public const float MaxAccumulatedDt = 0.1f;

    public const float BallRadius = 0.028575f;
    public const float BallMass = 0.180f;
    public const float MaxImpactSpeed = 1000.0f;

    public const float RailWidth = 0.12f;
    public const float RailSlateOffset = BallRadius * 0.5f;
    public const float GateSlateOffset = RailSlateOffset * 1.5f;
    public const float RailHeight = BallRadius * 2.01f;

    // --- WPA POCKET SPECIFICATIONS ---
    public const float RailCornerMouthWidth = 0.1143f; // 4.5 inches
    public const float RailSideMouthWidth = 0.127f;    // 5.0 inches

    // --- WPA JAW ANGLES ---
    public const float RailCornerSweep = 39f;
    public const float RailSideSweep = 15f;
}
