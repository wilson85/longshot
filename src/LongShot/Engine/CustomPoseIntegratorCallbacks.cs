using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;

namespace LongShot.Engine;

public struct CustomPoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    private Vector3Wide _gravityWide;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Vector<float> Broadcast(float value)
    {
        return new Vector<float>(value);
    }

    public readonly AngularIntegrationMode AngularIntegrationMode
        => AngularIntegrationMode.Nonconserving;

    public readonly bool AllowSubstepsForUnconstrainedBodies => false;
    public readonly bool IntegrateVelocityForKinematics => false;

    public CustomPoseIntegratorCallbacks(Vector3 gravity)
    {
        _gravityWide = Vector3Wide.Broadcast(gravity);
    }

    public void Initialize(Simulation simulation) { }

    public void PrepareForIntegration(float dt) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IntegrateVelocity(
        Vector<int> bodyIndices,
        Vector3Wide position,
        QuaternionWide orientation,
        BodyInertiaWide localInertia,
        Vector<int> integrationMask,
        int workerIndex,
        Vector<float> dt,
        ref BodyVelocityWide velocity)
    {
        var linearDrag = Broadcast(0.98f);
        var angularDrag = Broadcast(0.97f);

        velocity.Linear *= linearDrag;
        velocity.Angular *= angularDrag;

        velocity.Linear.X += _gravityWide.X * dt;
        velocity.Linear.Y += _gravityWide.Y * dt;
        velocity.Linear.Z += _gravityWide.Z * dt;
    }
}

public struct CustomNarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    public StaticHandle FloorHandle;

    static readonly PairMaterialProperties BallBallMaterial = new()
    {
        FrictionCoefficient = 0.02f,
        MaximumRecoveryVelocity = 3f,
        SpringSettings = new SpringSettings(1400, 1f)
    };

    static readonly PairMaterialProperties ClothMaterial = new()
    {
        FrictionCoefficient = 0.6f,
        MaximumRecoveryVelocity = 1f,
        SpringSettings = new SpringSettings(120, 1f)
    };

    static readonly PairMaterialProperties CushionMaterial = new()
    {
        FrictionCoefficient = 0.5f,
        MaximumRecoveryVelocity = 3f,
        SpringSettings = new SpringSettings(1600, 0.8f)
    };

    public void Initialize(Simulation simulation) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(
        int workerIndex,
        CollidableReference a,
        CollidableReference b,
        ref float speculativeMargin)
    {
        speculativeMargin = MathF.Max(speculativeMargin, 0.001f);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold<TManifold>(
        int workerIndex,
        CollidablePair pair,
        ref TManifold manifold,
        out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        if (pair.A.Mobility == CollidableMobility.Dynamic &&
            pair.B.Mobility == CollidableMobility.Dynamic)
        {
            pairMaterial = BallBallMaterial;
            return true;
        }

        var staticHandle =
            pair.A.Mobility == CollidableMobility.Static
            ? pair.A.StaticHandle
            : pair.B.StaticHandle;

        pairMaterial =
            staticHandle == FloorHandle
            ? ClothMaterial
            : CushionMaterial;

        return true;
    }

    public bool AllowContactGeneration(
        int workerIndex,
        CollidablePair pair,
        int childIndexA,
        int childIndexB) => true;

    public bool ConfigureContactManifold(
        int workerIndex,
        CollidablePair pair,
        int childIndexA,
        int childIndexB,
        ref ConvexContactManifold manifold) => true;

    public void Dispose() { }
}