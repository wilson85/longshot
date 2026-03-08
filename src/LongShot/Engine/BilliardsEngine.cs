
using System.Numerics;
using System.Runtime.InteropServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
namespace LongShot.Engine;

public struct Ball
{
    public int Id;
    public BallType Type;

    public Vector3 Position;
    public Quaternion Orientation;

    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;

    public float LastSpeed;

    public Vector3[] Trail;
    public int TrailIndex;
}

public enum BallType { Cue, Normal }

public sealed class BilliardsEngine : IDisposable
{
    public const float StandardBallRadius = 0.028575f;
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
            new SolveDescription(32, 4));

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
        var sphere = new Sphere(StandardBallRadius);
        var shape = _simulation.Shapes.Add(sphere);
        var inertia = sphere.ComputeInertia(0.17f);

        CreateBall(new Vector3(0, StandardBallRadius, -0.8f), shape, inertia, BallType.Cue);

        float spacing = StandardBallRadius * 2.01f;
        int rows = 5;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c <= r; c++)
            {
                Vector3 pos = new(
                    (c - (r * 0.5f)) * spacing,
                    StandardBallRadius,
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
            Trail = new Vector3[32],
            TrailIndex = 0,
            LastSpeed = 0f
        });
    }

    public Vector3 GetBallPosition(int id)
    {
        var handle = _ballHandles[id];
        return _simulation.Bodies.GetBodyReference(handle).Pose.Position;
    }

    public void StrikeCueBall(Vector3 direction, float targetSpeed, Vector2 tipOffset)
    {
        var body = _simulation.Bodies.GetBodyReference(_ballHandles[0]);

        direction = Vector3.Normalize(direction);

        body.Velocity.Linear += direction * targetSpeed;

        Vector3 spin = new Vector3(
            -tipOffset.Y,
            tipOffset.X,
            0
        ) * targetSpeed * 15f;

        body.Velocity.Angular += spin;
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

            var spin = body.Velocity.Angular;

            if (spin.LengthSquared() > 0.01f)
            {
                Vector3 spinAcceleration = Vector3.Cross(spin, Vector3.UnitY) * 0.05f;
                body.Velocity.Linear += spinAcceleration * dt;
            }

            ball.Position = body.Pose.Position;
            ball.Orientation = body.Pose.Orientation;
            ball.LinearVelocity = body.Velocity.Linear;
            ball.AngularVelocity = body.Velocity.Angular;

            float currentSpeed = ball.LinearVelocity.Length();
            ball.LastSpeed = currentSpeed;

            if (currentSpeed > 0.01f)
            {
                ball.Trail[ball.TrailIndex] = ball.Position;
                ball.TrailIndex = (ball.TrailIndex + 1) % ball.Trail.Length;
            }
        }
    }

    public void Dispose()
    {
        _simulation.Dispose();
        _pool.Clear();
    }
}