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
        SpinEfficiency = 0.70f
    };
}

/// <summary>
/// Root configuration for all physics constants.
/// </summary>
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
        Cue = CueParameters.Default
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
        SlateRestitution = 0.12f,
        LinearSleepSpeed = 0.005f,
        AngularSleepSpeed = 0.25f
    };
}

public readonly struct BallParameters
{
    public float Mass { get; init; }
    public float Radius { get; init; }
    public float Restitution { get; init; }
    public float Friction { get; init; }
    public float ThrowMassFactor { get; init; }

    public static BallParameters Default => new()
    {
        Mass = BilliardsConstants.BallMass,
        Radius = BilliardsConstants.BallRadius,
        Restitution = 0.98f,
        Friction = 0.025f,
        ThrowMassFactor = 7.0f // Divisor for effective mass when balls throw each other
    };
}

public readonly struct CushionParameters
{
    public required float MaxRestitution { get; init; }
    public required float MinRestitution { get; init; }
    public required float SpeedDecay { get; init; }
    public required float FrictionCoeff { get; init; }
    public required float ThrowMassFactor { get; init; }
    public required float NoseHeight { get; init; }
    public required float BaseSpinAbsorption { get; init; }
    public required float DynamicSpinMultiplier { get; init; }
    public required float MaxDynamicAbsorption { get; init; }
    public static CushionParameters Default => new()
    {
        MaxRestitution = 0.75f,
        MinRestitution = 0.50f,
        SpeedDecay = 0.03f,
        FrictionCoeff = 0.20f,
        ThrowMassFactor = 3.5f, // Divisor for effective mass against rail friction
        VerticalFrictionMultiplier = 0.15f,
        NoseHeight = BilliardsConstants.BallRadius * 1.28f,
        BaseSpinAbsorption = 0.20f,       // Rails naturally kill 20% of spin
        DynamicSpinMultiplier = 0.25f,    // How much extra spin is killed per unit of impact force
        MaxDynamicAbsorption = 0.60f      // Cap the dynamic loss at 60%
    };

    public float VerticalFrictionMultiplier { get; init; }
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
        NapResistanceMultiplier = 1f,
        SlidingMaxDeltaVFactor = 3.5f,
        SwerveFactor = 0.15f,
        ContactPatchDrag = 1.1f
    };
}