
using System.Numerics;
using System.Runtime.InteropServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
namespace LongShot.Engine;

public struct BallSnapshot
{
    public Vector3 Position;
    public Quaternion Orientation;
    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;
}

public class TableSnapshot
{
    public readonly BallSnapshot[] Balls = new BallSnapshot[16];
}

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
    private readonly CollisionTracker _collisionTracker = new();
    private readonly BodyHandle[] _ballHandles = new BodyHandle[MaxBalls];
    private readonly List<Ball> _balls = new(MaxBalls);

    public ReadOnlySpan<Ball> ActiveBalls => CollectionsMarshal.AsSpan(_balls);

    private int _nextId = 0;

    public BilliardsEngine()
    {
        _collisionTracker.BallHandles = _ballHandles;

        var narrow = new CustomNarrowPhaseCallbacks();
        narrow.FloorHandle = new StaticHandle(0);
        narrow.Tracker = _collisionTracker;

        var integrator = new CustomPoseIntegratorCallbacks(new Vector3(0, -9.81f, 0));

        _simulation = Simulation.Create(
            _pool,
            narrow,
            integrator,
            new SolveDescription(12, 2));

        CreateTable();
        CreateBalls();
    }

    // ==========================================
    // SAVE / LOAD SYSTEM
    // ==========================================
    public TableSnapshot TakeSnapshot()
    {
        var snapshot = new TableSnapshot();

        // We read directly from Bepu's memory to ensure 100% accuracy
        for (int i = 0; i < _balls.Count; i++)
        {
            var body = _simulation.Bodies.GetBodyReference(_ballHandles[i]);

            snapshot.Balls[i] = new BallSnapshot
            {
                Position = body.Pose.Position,
                Orientation = body.Pose.Orientation,
                LinearVelocity = body.Velocity.Linear,
                AngularVelocity = body.Velocity.Angular
            };
        }
        return snapshot;
    }

    public void RestoreSnapshot(TableSnapshot snapshot)
    {
        if (snapshot == null) return;

        for (int i = 0; i < _balls.Count; i++)
        {
            var body = _simulation.Bodies.GetBodyReference(_ballHandles[i]);
            ref BallSnapshot snap = ref snapshot.Balls[i];

            // 1. Restore Bepu's raw physical state
            body.Pose.Position = snap.Position;
            body.Pose.Orientation = snap.Orientation;
            body.Velocity.Linear = snap.LinearVelocity;
            body.Velocity.Angular = snap.AngularVelocity;

            // 2. Force the physics engine to wake the body up, just in case it went to sleep
            body.Awake = true;

            // 3. Restore our tracking logic so delta-V calculations don't glitch on the first frame
            ref Ball ball = ref CollectionsMarshal.AsSpan(_balls)[i];
            ball.Position = snap.Position;
            ball.Orientation = snap.Orientation;
            ball.LinearVelocity = snap.LinearVelocity;
            ball.AngularVelocity = snap.AngularVelocity;
            ball.LastLinearVelocity = snap.LinearVelocity;
            ball.LastSpeed = snap.LinearVelocity.Length();
            ball.LastTrailPosition = snap.Position;
        }

        // Wipe visual trails and hit markers so they don't linger from the future!
        ClearTrails();

        // Wipe the tracker so it doesn't trigger audio on frame 1 of the load
        _collisionTracker.Clear();
    }
    // ==========================================

    private void CreateTable()
    {
        var floorShape = new Box(TableWidth, 0.1f, TableLength);
        var floorHandle = _simulation.Statics.Add(new StaticDescription(new Vector3(0, -0.05f, 0), _simulation.Shapes.Add(floorShape)));

        if (floorHandle.Value != 0) throw new Exception("Floor static handle is not 0.");

        var cushionShapeX = new Box(CushionWidth, 0.1f, TableLength);
        var cushionShapeZ = new Box(TableWidth + (CushionWidth * 2), 0.1f, CushionWidth);

        _simulation.Statics.Add(new StaticDescription(new Vector3((-TableWidth / 2) - (CushionWidth / 2), 0, 0), _simulation.Shapes.Add(cushionShapeX)));
        _simulation.Statics.Add(new StaticDescription(new Vector3((TableWidth / 2) + (CushionWidth / 2), 0, 0), _simulation.Shapes.Add(cushionShapeX)));
        _simulation.Statics.Add(new StaticDescription(new Vector3(0, 0, (-TableLength / 2) - (CushionWidth / 2)), _simulation.Shapes.Add(cushionShapeZ)));
        _simulation.Statics.Add(new StaticDescription(new Vector3(0, 0, (TableLength / 2) + (CushionWidth / 2)), _simulation.Shapes.Add(cushionShapeZ)));
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
                Vector3 pos = new((c - (r * 0.5f)) * spacing, GameSettings.StandardBallRadius, 0.8f + (r * spacing * 0.866f));
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

    public Vector3 GetBallPosition(int id) => _simulation.Bodies.GetBodyReference(_ballHandles[id]).Pose.Position;

    public void ApplyImpulse(int id, Vector3 impulse)
    {
        var body = _simulation.Bodies.GetBodyReference(_ballHandles[id]);
        body.Velocity.Linear += impulse * body.LocalInertia.InverseMass;
        body.Awake = true;
        RetroAudio.PlayCueImpact(impulse.Length() * 3f, body.Pose.Position);
    }

    public void ApplyAngularImpulse(int id, Vector3 angularImpulse)
    {
        var body = _simulation.Bodies.GetBodyReference(_ballHandles[id]);
        BepuUtilities.Symmetric3x3.TransformWithoutOverlap(angularImpulse, body.LocalInertia.InverseInertiaTensor, out var velocityChange);
        body.Velocity.Angular += velocityChange;

        const float maxSpin = 120f;

        var spin = body.Velocity.Angular.Length();
        if (spin > maxSpin)
        {
            body.Velocity.Angular *= maxSpin / spin;
        }

        body.Awake = true;
    }

    public bool AreAllBallsAsleep() => _simulation.Bodies.ActiveSet.Count == 0;

    public void Tick(float dt)
    {
        _collisionTracker.Clear();
        _simulation.Timestep(dt);

        Span<Ball> ballsSpan = CollectionsMarshal.AsSpan(_balls);

        for (int i = 0; i < ballsSpan.Length; i++)
        {
            ref Ball ball = ref ballsSpan[i];
            var body = _simulation.Bodies.GetBodyReference(_ballHandles[ball.Id]);

            Vector3 currentVel = body.Velocity.Linear;
            Vector3 lastVel = ball.LastLinearVelocity;

            Vector3 deltaV = currentVel - lastVel;
            float force = deltaV.Length();

            if (force > 0.05f)
            {
                for (int j = i + 1; j < ballsSpan.Length; j++)
                {
                    if ((_collisionTracker.BallContactMask[i] & (1 << j)) != 0)
                    {
                        // ONLY record the hit mark if the Cue Ball (Id == 0) is involved
                        if (ball.Id == 0 || ballsSpan[j].Id == 0)
                        {
                            // 1. Drop a hit mark directly under the first ball
                            ball.HitMarksWorld[ball.HitMarkIndex] = new Vector3(ball.Position.X, -0.02f, ball.Position.Z);
                            ball.HitMarkIndex = (ball.HitMarkIndex + 1) % ball.HitMarksWorld.Length;

                            // 2. Drop a hit mark directly under the second ball
                            ref Ball otherBall = ref ballsSpan[j];
                            otherBall.HitMarksWorld[otherBall.HitMarkIndex] = new Vector3(otherBall.Position.X, -0.02f, otherBall.Position.Z);
                            otherBall.HitMarkIndex = (otherBall.HitMarkIndex + 1) % otherBall.HitMarksWorld.Length;
                        }

                        Vector3 hitPos = (ball.Position + ballsSpan[j].Position) / 2f;


                        var bodyA = _simulation.Bodies.GetBodyReference(_ballHandles[i]);
                        var bodyB = _simulation.Bodies.GetBodyReference(_ballHandles[j]);

                        Vector3 spinA = bodyA.Velocity.Angular;

                        Vector3 contactNormal = Vector3.Normalize(ballsSpan[j].Position - ball.Position);

                        // tangential spin component
                        Vector3 tangentialSpin = Vector3.Cross(spinA, contactNormal);

                        // transfer small portion
                        const float spinTransfer = 0.02f;

                        bodyB.Velocity.Linear += tangentialSpin * spinTransfer;
                        bodyA.Velocity.Angular *= 0.98f;


                        float spinMagnitude = ball.AngularVelocity.Length();
                        RetroAudio.PlayBallImpact(force * 1.5f, hitPos, spinMagnitude);
                    }
                }

                if (_collisionTracker.CushionContacts[i])
                {
                    RetroAudio.PlayRailImpact(force * 1.5f, ball.Position);

                    Vector3 v = body.Velocity.Linear;

                    Vector3 tangent = Vector3.Cross(Vector3.UnitY, v);

                    body.Velocity.Linear += tangent * body.Velocity.Angular.Y * 0.02f;

                    body.Velocity.Angular *= 0.8f;
                }
            }

            ApplyTableFriction(body, dt);

            ball.Position = body.Pose.Position;
            ball.Orientation = body.Pose.Orientation;
            ball.LinearVelocity = body.Velocity.Linear;
            ball.AngularVelocity = body.Velocity.Angular;
            ball.LastLinearVelocity = currentVel;
            ball.LastSpeed = currentVel.Length();

            float distanceTraveled = Vector3.Distance(ball.Position, ball.LastTrailPosition);

            if (distanceTraveled > 0.02f)
            {
                Vector3 vel = body.Velocity.Linear;
                float speed = vel.Length();

                // Protect against divide-by-zero if the ball is barely moving
                Vector3 dir = speed > 0.001f ? vel / speed : Vector3.UnitZ;

                ball.Trail[ball.TrailIndex] = new TrailPoint
                {
                    // UPDATED: Set the Y axis to -0.02f to sink it into the glass floor
                    Position = new Vector3(ball.Position.X, -0.02f, ball.Position.Z),
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
        const float radius = GameSettings.StandardBallRadius;

        Vector3 v = body.Velocity.Linear;
        Vector3 w = body.Velocity.Angular;

        float linearSpeed = v.Length();
        float spinSpeed = w.Length();

        if (linearSpeed < 0.0005f && spinSpeed < 0.05f)
        {
            body.Velocity.Linear = Vector3.Zero;
            body.Velocity.Angular = Vector3.Zero;
            return;
        }

        Vector3 contact = new(0, -radius, 0);

        // velocity at cloth contact point
        Vector3 surfaceVelocity = v + Vector3.Cross(w, contact);
        float surfaceSpeed = surfaceVelocity.Length();

        // tuned constants
        const float slidingFriction = 1.1f;   // sliding → rolling transition
        const float rollingDecel = 0.8f;      // m/s² rolling resistance
        const float spinDecel = 12f;          // rad/s² spin decay

        if (surfaceSpeed > 0.01f)
        {
            // sliding phase
            Vector3 dir = surfaceVelocity / surfaceSpeed;

            Vector3 friction = -dir * slidingFriction;

            body.Velocity.Linear += friction * dt;

            // convert sliding into spin
            Vector3 torque = Vector3.Cross(contact, friction);

            body.Velocity.Angular += torque * dt;
        }
        else
        {
            // rolling resistance (constant deceleration)
            if (linearSpeed > 0f)
            {
                float newSpeed = MathF.Max(0f, linearSpeed - rollingDecel * dt);
                body.Velocity.Linear *= newSpeed / linearSpeed;
            }
        }

        // spin decay (stronger than before)
        spinSpeed = body.Velocity.Angular.Length();

        if (spinSpeed > 0f)
        {
            float newSpin = MathF.Max(0f, spinSpeed - spinDecel * dt);
            body.Velocity.Angular *= newSpin / spinSpeed;
        }

        //Vector3 desiredAngular = Vector3.Cross(Vector3.UnitY, body.Velocity.Linear) / radius;

        //const float radius = GameSettings.StandardBallRadius;

        Vector3 desiredSpin = Vector3.Cross(Vector3.UnitY, v) / radius;

        body.Velocity.Angular =
            Vector3.Lerp(body.Velocity.Angular, desiredSpin, 0.12f);

        body.Velocity.Angular = Vector3.Lerp(
            body.Velocity.Angular,
            desiredSpin,
            0.15f);

        const float maxSpin = 120f;

        float spin = body.Velocity.Angular.Length();

        if (spin > maxSpin)
        {
            body.Velocity.Angular *= maxSpin / spin;
        }

    }

    public void SetChalkMark(int id, Vector3 worldOffset)
    {
        Span<Ball> ballsSpan = CollectionsMarshal.AsSpan(_balls);
        ballsSpan[id].ChalkMarkLocal = Vector3.Transform(worldOffset, Quaternion.Inverse(ballsSpan[id].Orientation));
    }

    public void ClearTrails()
    {
        Span<Ball> ballsSpan = CollectionsMarshal.AsSpan(_balls);
        for (int i = 0; i < ballsSpan.Length; i++)
        {
            ballsSpan[i].TrailIndex = 0;
            Array.Clear(ballsSpan[i].Trail, 0, ballsSpan[i].Trail.Length);
            ballsSpan[i].ChalkMarkLocal = null;

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