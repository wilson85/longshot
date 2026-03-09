
using System.Numerics;
using System.Runtime.InteropServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
namespace LongShot.Engine;

public struct TrailPoint
{
    public Vector3 Position;
    public float Spin;
    public Vector3 Direction; 
}

public struct Ball
{
    public int Id;
    public BallType Type;

    public Vector3 Position;
    public Quaternion Orientation;

    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;

    public float LastSpeed;
    public Vector3? ChalkMarkLocal;

    // Change these from Vector3[] to TrailPoint[]
    public TrailPoint[] Trail;
    public int TrailIndex;
    public Vector3 LastTrailPosition;

    public Vector3[] HitMarksWorld;
    public int HitMarkIndex;
    public Vector3 LastLinearVelocity { get; internal set; }
}

public enum BallType { Cue, Normal }

public sealed class BilliardsEngine : IDisposable
{
    
    public const float TableWidth = 1.27f;
    public const float TableLength = 2.54f;
    public const float CushionWidth = 0.05f;

    private const int MaxBalls = 16;

    private readonly BufferPool _pool = new();
    private readonly Simulation _simulation;

    private readonly BodyHandle[] _ballHandles = new BodyHandle[MaxBalls];
    private readonly List<Ball> _balls = new(MaxBalls);

    public ReadOnlySpan<Ball> ActiveBalls => CollectionsMarshal.AsSpan(_balls);

    private int _nextId = 0;

    public BilliardsEngine()
    {
        var narrow = new CustomNarrowPhaseCallbacks();
        // Assign the FloorHandle before creating the simulation.
        // The first static added will receive Handle 0.
        narrow.FloorHandle = new StaticHandle(0);

        // FIX: We are now passing actual Earth gravity (-9.81) to the integrator
        var integrator = new CustomPoseIntegratorCallbacks(new Vector3(0, -9.81f, 0));

        _simulation = Simulation.Create(
            _pool,
            narrow,
            integrator,
            new SolveDescription(4, 16));

        CreateTable();
        CreateBalls();
    }

    private void CreateTable()
    {
        var floorShape = new Box(TableWidth, 0.1f, TableLength);

        var floorHandle = _simulation.Statics.Add(
            new StaticDescription(
                new Vector3(0, -0.05f, 0),
                _simulation.Shapes.Add(floorShape)));

        // Sanity check to ensure our Cloth material logic works.
        if (floorHandle.Value != 0)
        {
            throw new Exception("Floor static handle is not 0. NarrowPhase callbacks will not apply cloth friction correctly!");
        }

        var cushionShapeX = new Box(CushionWidth, 0.1f, TableLength);
        var cushionShapeZ = new Box(TableWidth + (CushionWidth * 2), 0.1f, CushionWidth);

        _simulation.Statics.Add(new StaticDescription(
            new Vector3((-TableWidth / 2) - (CushionWidth / 2), 0, 0),
            _simulation.Shapes.Add(cushionShapeX)));

        _simulation.Statics.Add(new StaticDescription(
            new Vector3((TableWidth / 2) + (CushionWidth / 2), 0, 0),
            _simulation.Shapes.Add(cushionShapeX)));

        _simulation.Statics.Add(new StaticDescription(
            new Vector3(0, 0, (-TableLength / 2) - (CushionWidth / 2)),
            _simulation.Shapes.Add(cushionShapeZ)));

        _simulation.Statics.Add(new StaticDescription(
            new Vector3(0, 0, (TableLength / 2) + (CushionWidth / 2)),
            _simulation.Shapes.Add(cushionShapeZ)));
    }

    private void CreateBalls()
    {
        var sphere = new Sphere(GameSettings.StandardBallRadius);
        var shape = _simulation.Shapes.Add(sphere);
        var inertia = sphere.ComputeInertia(0.17f);

        CreateBall(new Vector3(0, GameSettings.StandardBallRadius, -0.8f), shape, inertia, BallType.Cue);

        float spacing = GameSettings.StandardBallRadius * 2.01f;
        int rows = 5;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c <= r; c++)
            {
                Vector3 pos = new(
                    (c - (r * 0.5f)) * spacing,
                    GameSettings.StandardBallRadius,
                    0.8f + (r * spacing * 0.866f));

                CreateBall(pos, shape, inertia, BallType.Normal);
            }
        }
    }

    private void CreateBall(Vector3 position, TypedIndex shape, BodyInertia inertia, BallType type)
    {
        var collidable = new CollidableDescription(shape, 0.1f, ContinuousDetection.Continuous(1e-3f, 1e-3f));
        var activity = new BodyActivityDescription(0.01f);

        var bodyDesc = BodyDescription.CreateDynamic(new RigidPose(position), inertia, collidable, activity);
        var bodyHandle = _simulation.Bodies.Add(bodyDesc);

        int id = _nextId++;
        _ballHandles[id] = bodyHandle;

        _balls.Add(new Ball
        {
            Id = id,
            Type = type,
            Position = position,
            Orientation = Quaternion.Identity,
            Trail = new TrailPoint[128],
            TrailIndex = 0,
            LastSpeed = 0f,
            HitMarksWorld = new Vector3[8],
            HitMarkIndex = 0
        });
    }

    public Vector3 GetBallPosition(int id)
    {
        var handle = _ballHandles[id];
        return _simulation.Bodies.GetBodyReference(handle).Pose.Position;
    }

    public void ApplyImpulse(int id, Vector3 impulse)
    {
        var body = _simulation.Bodies.GetBodyReference(_ballHandles[id]);

        // Velocity Change = Impulse * (1 / Mass)
        body.Velocity.Linear += impulse * body.LocalInertia.InverseMass;
        body.Awake = true;
    }

    public void ApplyAngularImpulse(int id, Vector3 angularImpulse)
    {
        var body = _simulation.Bodies.GetBodyReference(_ballHandles[id]);

        // Bepu stores the inverse inertia tensor as a symmetric 3x3 matrix.
        // We transform the angular impulse by this matrix to get the rotational velocity change.
        BepuUtilities.Symmetric3x3.TransformWithoutOverlap(
            angularImpulse,
            body.LocalInertia.InverseInertiaTensor,
            out var velocityChange);

        body.Velocity.Angular += velocityChange;
        body.Awake = true;
    }

    public bool AreAllBallsAsleep()
    {
        return _simulation.Bodies.ActiveSet.Count == 0;
    }

    public void Tick(float dt)
    {
        _simulation.Timestep(dt);

        Span<Ball> ballsSpan = CollectionsMarshal.AsSpan(_balls);

        for (int i = 0; i < ballsSpan.Length; i++)
        {
            ref Ball ball = ref ballsSpan[i];
            var body = _simulation.Bodies.GetBodyReference(_ballHandles[ball.Id]);

            Vector3 currentVel = body.Velocity.Linear;
            Vector3 lastVel = ball.LastLinearVelocity;

            // --- IMPACT DETECTION ---
            Vector3 deltaV = currentVel - lastVel;

            if (deltaV.LengthSquared() > 0.25f)
            {
                // Verify the impulse came from another ball, not a cushion bounce
                for (int j = 0; j < ballsSpan.Length; j++)
                {
                    if (i == j) continue;

                    ref Ball otherBall = ref ballsSpan[j];

                    if (Vector3.Distance(ball.Position, otherBall.Position) < GameSettings.StandardBallRadius * 4.0f)
                    {
                        Vector3 floorHit = new Vector3(ball.Position.X, 0.0015f, ball.Position.Z);

                        ball.HitMarksWorld[ball.HitMarkIndex] = floorHit;
                        ball.HitMarkIndex = (ball.HitMarkIndex + 1) % ball.HitMarksWorld.Length;

                        RetroAudio.PlayBallImpact(deltaV.Length() / 2f);

                        break;
                    }
                }
            }

            // 1. CUSHION DEFORMATION (Spin Killer)
            // If the velocity on the X or Z axis suddenly flipped direction, it hit a rail!
            bool hitRailX = MathF.Sign(currentVel.X) != MathF.Sign(lastVel.X) && MathF.Abs(lastVel.X) > 0.1f;
            bool hitRailZ = MathF.Sign(currentVel.Z) != MathF.Sign(lastVel.Z) && MathF.Abs(lastVel.Z) > 0.1f;

            if (hitRailX || hitRailZ)
            {
                // Slash the spin by 50% to simulate the rubber wrapping around the ball!
                // (Tune this! 0.5f kills half the spin. 0.3f kills 70% of the spin).
                body.Velocity.Angular *= 0.5f;
            }

            // 2. Apply our standard table friction
            ApplyTableFriction(body, dt);

            // Sync state
            ball.Position = body.Pose.Position;
            ball.Orientation = body.Pose.Orientation;
            ball.LinearVelocity = body.Velocity.Linear;
            ball.AngularVelocity = body.Velocity.Angular;

            // Track the velocity for the next frame's bounce detection
            ball.LastLinearVelocity = currentVel;

            // Trail tracking
            ball.LastSpeed = currentVel.Length();

            float distanceTraveled = Vector3.Distance(ball.Position, ball.LastTrailPosition);

            // Drop a point exactly every 2 centimeters
            if (distanceTraveled > 0.02f)
            {
                Vector3 vel = body.Velocity.Linear;
                float speed = vel.Length();

                // Protect against divide-by-zero if the ball is barely moving
                Vector3 dir = speed > 0.001f ? vel / speed : Vector3.UnitZ;

                ball.Trail[ball.TrailIndex] = new TrailPoint
                {
                    Position = new Vector3(ball.Position.X, 0.001f, ball.Position.Z),
                    Spin = body.Velocity.Angular.Length(),
                    Direction = dir 
                };

                ball.TrailIndex = (ball.TrailIndex + 1) % ball.Trail.Length;
                ball.LastTrailPosition = ball.Position;
            }
        }
    }

    private void ApplyTableFriction(BodyReference body, float dt)
    {
        float linearSpeed = body.Velocity.Linear.Length();
        float angularSpeed = body.Velocity.Angular.Length();

        // 1. STRICTLY ISOLATED SWERVE (Only curve if spinning on the Y-axis)
        float sideSpin = body.Velocity.Angular.Y;
        if (MathF.Abs(sideSpin) > 0.1f && linearSpeed > 0.01f)
        {
            Vector3 forward = body.Velocity.Linear / linearSpeed;
            Vector3 swerveDir = Vector3.Cross(forward, Vector3.UnitY);

            // Apply the Magnus effect curve
            body.Velocity.Linear += swerveDir * sideSpin * 0.05f * dt;
        }

        // 2. SYNCHRONIZED ROLLING DECAY
        // We must slow down both linear and angular momentum at the exact same rate.
        float baseFriction = 0.4f * dt;

        if (linearSpeed > 0)
        {
            float newSpeed = MathF.Max(0, linearSpeed - baseFriction);
            body.Velocity.Linear *= (newSpeed / linearSpeed);
        }

        if (angularSpeed > 0)
        {
            // To keep the ball from sliding, the angular friction must be scaled 
            // relative to the ball's radius so it matches the linear decay.
            float angularFriction = baseFriction / GameSettings.StandardBallRadius;
            float newAngularSpeed = MathF.Max(0, angularSpeed - angularFriction);
            body.Velocity.Angular *= (newAngularSpeed / angularSpeed);
        }
    }

    public void SetChalkMark(int id, Vector3 worldOffset)
    {
        Span<Ball> ballsSpan = CollectionsMarshal.AsSpan(_balls);
        ref Ball ball = ref ballsSpan[id];

        // We transform the world-space hit coordinate by the inverse of the ball's 
        // current orientation so the mark physically sticks to the surface as it rolls!
        ball.ChalkMarkLocal = Vector3.Transform(worldOffset, Quaternion.Inverse(ball.Orientation));
    }

    public void ClearTrails()
    {
        Span<Ball> ballsSpan = CollectionsMarshal.AsSpan(_balls);
        for (int i = 0; i < ballsSpan.Length; i++)
        {
            // Wipe the trails...
            ballsSpan[i].TrailIndex = 0;
            Array.Clear(ballsSpan[i].Trail, 0, ballsSpan[i].Trail.Length);
            ballsSpan[i].ChalkMarkLocal = null;

            // Wipe the hit markers!
            if (ballsSpan[i].HitMarksWorld != null)
            {
                Array.Clear(ballsSpan[i].HitMarksWorld, 0, ballsSpan[i].HitMarksWorld.Length);
                ballsSpan[i].HitMarkIndex = 0;
            }
        }
    }

    public void Dispose()
    {
        _simulation.Dispose();
        _pool.Clear();
    }
}