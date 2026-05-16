namespace LongShot.Engine;

public readonly struct CueParameters
{
    public float Mass { get; init; }
    public float DeflectionMultiplier { get; init; }
    public required float MiscueLimit { get; init; }
    public float SpinEfficiency { get; init; }

    public static CueParameters Default => new()
    {
        Mass = 0.55f,
        DeflectionMultiplier = 0.13f,
        MiscueLimit = 0.60f,
        SpinEfficiency = 0.70f,
    };
}

/// <summary>Root configuration for all physics constants.</summary>
public readonly struct PhysicsConfig
{
    public EnvironmentParameters Env { get; init; }
    public BallParameters Ball { get; init; }
    public CushionParameters Cushion { get; init; }
    public ClothParameters Cloth { get; init; }
    public CueParameters Cue { get; init; }

    public static PhysicsConfig Default => new()
    {
        Env = EnvironmentParameters.Default,
        Ball = BallParameters.Default,
        Cushion = CushionParameters.Default,
        Cloth = ClothParameters.Tournament,
        Cue = CueParameters.Default,
    };
}

public readonly struct EnvironmentParameters
{
    public float Gravity { get; init; }
    public float SlateRestitution { get; init; }
    public float LinearSleepSpeed { get; init; }
    public float AngularSleepSpeed { get; init; }

    public static EnvironmentParameters Default => new()
    {
        Gravity = 9.81f,
        // Ball-on-slate coefficient of restitution. Real billiards: ~0.5 with tournament cloth
        // covering hard slate (slate is much stiffer than the ball so most bounce is elastic;
        // the cloth absorbs a little). The previous value of 0.12 was too dead - elevated cue
        // strikes barely produced a measurable jump.
        SlateRestitution = 0.5f,
        LinearSleepSpeed = 0.005f,
        AngularSleepSpeed = 0.25f,
    };
}

public readonly struct BallParameters
{
    public float Mass { get; init; }
    public float Radius { get; init; }

    /// <summary>Coefficient of restitution at very low impact speeds.</summary>
    public float MaxRestitution { get; init; }
    /// <summary>Floor of the coefficient of restitution at high impact speeds.</summary>
    public float MinRestitution { get; init; }
    /// <summary>How fast restitution drops with impact speed (per m/s). Phenolic resin loses energy faster at high speeds.</summary>
    public float RestitutionDecay { get; init; }

    public float Friction { get; init; }

    public static BallParameters Default => new()
    {
        Mass = GameSettings.BallMass,
        Radius = GameSettings.BallRadius,
        MaxRestitution = 0.96f,
        MinRestitution = 0.86f,
        RestitutionDecay = 0.012f,
        Friction = 0.025f,
    };
}

public readonly struct CushionParameters
{
    public required float MaxRestitution { get; init; }
    public required float MinRestitution { get; init; }
    public required float SpeedDecay { get; init; }
    public required float FrictionCoeff { get; init; }
    public required float NoseHeight { get; init; }
    public required float BaseSpinAbsorption { get; init; }
    public required float DynamicSpinMultiplier { get; init; }
    public required float MaxDynamicAbsorption { get; init; }
    public float VerticalFrictionMultiplier { get; init; }

    public static CushionParameters Default => new()
    {
        MaxRestitution = 0.75f,
        MinRestitution = 0.50f,
        SpeedDecay = 0.02f,            // tuned 2026-05-16: was 0.03, gave too-aggressive COR drop with speed
        FrictionCoeff = 0.15f,         // tuned 2026-05-16: was 0.20, gave 50.6° rebound at 45°; 0.12 gave 47° but killed rail-English kick
        VerticalFrictionMultiplier = 0.15f,
        NoseHeight = GameSettings.BallRadius * 1.28f,
        BaseSpinAbsorption = 0.20f,
        DynamicSpinMultiplier = 0.25f,
        MaxDynamicAbsorption = 0.60f,
    };
}

public readonly struct ClothParameters
{
    public float SlidingFriction { get; init; }
    public float RollingFriction { get; init; }
    public float SpinFriction { get; init; }
    public float SpinDrag { get; init; }
    public float NapBunchingFactor { get; init; }
    public float NapResistanceSpeed { get; init; }
    public float NapResistanceMultiplier { get; init; }
    public float SlidingMaxDeltaVFactor { get; init; }

    public required float SwerveFactor { get; init; }
    public required float ContactPatchDrag { get; init; }

    public static readonly ClothParameters Tournament = new()
    {
        SlidingFriction = 3.5f,
        RollingFriction = 0.20f,
        SpinFriction = 5.0f,
        SpinDrag = 1.3f,
        NapBunchingFactor = 0.9f,
        NapResistanceSpeed = 1.3f,
        NapResistanceMultiplier = 0.5f,  // tuned 2026-05-16: was 1.0, lag stroke fell short of 1.5 table lengths
        SlidingMaxDeltaVFactor = 3.5f,
        SwerveFactor = 0.15f,
        ContactPatchDrag = 1.1f,
    };
}
