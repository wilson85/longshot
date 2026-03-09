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
        // Inside IntegrateVelocity:
        var linearDrag = Vector.Max(Vector<float>.Zero, Broadcast(1f) - Broadcast(0.02f) * dt);
        var angularDrag = Vector.Max(Vector<float>.Zero, Broadcast(1f) - Broadcast(0.05f) * dt);

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
    public CollisionTracker Tracker;

    // Phenolic Resin (Balls) - Very hard, perfectly elastic
    static readonly PairMaterialProperties BallBallMaterial = new()
    {
        FrictionCoefficient = 0.005f,
        MaximumRecoveryVelocity = float.MaxValue,
        SpringSettings = new SpringSettings(600, 0.0f)
    };

    // Vulcanized Rubber (Cushions) - Softer than balls, highly elastic
    static readonly PairMaterialProperties CushionMaterial = new()
    {
        // 0.25f friction allows the rubber to "catch" the spin and throw the ball 
        // at an angle, without making it roll up the rail.
        FrictionCoefficient = 0.25f,
        MaximumRecoveryVelocity = float.MaxValue,
        // 120 rad/s (~19 Hz). The spring compresses and violently expands 
        // over the course of exactly 2-3 physics frames, resulting in a massive bounce.
        SpringSettings = new SpringSettings(120, 0.0f)
    };

    // Cloth (Table Bed)
    static readonly PairMaterialProperties ClothMaterial = new()
    {
        FrictionCoefficient = 0.2f,
        MaximumRecoveryVelocity = 1f,
        SpringSettings = new SpringSettings(120, 1f)
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
    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
    {
        // 1. MATERIAL SETUP
        if (pair.A.Mobility == CollidableMobility.Dynamic && pair.B.Mobility == CollidableMobility.Dynamic)
        {
            pairMaterial = BallBallMaterial;
        }
        else
        {
            var staticHandle = pair.A.Mobility == CollidableMobility.Static ? pair.A.StaticHandle : pair.B.StaticHandle;
            pairMaterial = staticHandle == FloorHandle ? ClothMaterial : CushionMaterial;
        }

        // 2. REPORT TO COLLISION TRACKER
        // We do this during the narrow phase to guarantee we know *what* we are touching, 
        // before the solver instantly resolves the bouncing velocities!
        if (Tracker != null)
        {
            if (pair.A.Mobility == CollidableMobility.Dynamic)
                Tracker.RecordContact(pair.A.BodyHandle, pair.B, FloorHandle);
            if (pair.B.Mobility == CollidableMobility.Dynamic)
                Tracker.RecordContact(pair.B.BodyHandle, pair.A, FloorHandle);
        }

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

public class CollisionTracker
{
    public BodyHandle[] BallHandles;

    // Bitmask for tracking ball-to-ball collisions (Max 16 balls fits in an int)
    public int[] BallContactMask = new int[16];

    // Simple boolean flag for cushion hits
    public bool[] CushionContacts = new bool[16];

    public void Clear()
    {
        Array.Clear(BallContactMask);
        Array.Clear(CushionContacts);
    }

    public void RecordContact(BodyHandle dynamicBody, CollidableReference other, StaticHandle floorHandle)
    {
        if (BallHandles == null) return;

        int ballId = Array.IndexOf(BallHandles, dynamicBody);
        if (ballId == -1) return;

        if (other.Mobility == CollidableMobility.Dynamic)
        {
            int otherBallId = Array.IndexOf(BallHandles, other.BodyHandle);
            if (otherBallId != -1)
            {
                // Safely flag that these two balls are touching using bitwise logic
                Interlocked.Or(ref BallContactMask[ballId], 1 << otherBallId);
            }
        }
        else if (other.Mobility == CollidableMobility.Static && other.StaticHandle != floorHandle)
        {
            // If it hit a static body that ISN'T the floor, it's a cushion
            CushionContacts[ballId] = true;
        }
    }
}