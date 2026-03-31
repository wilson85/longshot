using System.Numerics;
using LongShot.Rendering;

namespace LongShot.Engine;

public class TronParticle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Life;
}

public sealed class BilliardsSceneBuilder
{
    private readonly BilliardsEngine engine;
    private readonly Camera camera;
    private readonly MatchManager match;
    private readonly List<TronParticle> _particles = [];

    public BilliardsSceneBuilder(BilliardsEngine engine, Camera camera, MatchManager match)
    {
        this.engine = engine;
        this.camera = camera;
        this.match = match;
        this.engine.OnBallPocketed += HandleBallPocketed;
    }

    private void HandleBallPocketed(int ballId, Vector3 dropPos)
    {
        var rand = Random.Shared;
        for (int i = 0; i < 30; i++)
        {
            var vel = new Vector3(
                (float)((rand.NextDouble() * 2) - 1),
                (float)((rand.NextDouble() * 2) - 0.5),
                (float)((rand.NextDouble() * 2) - 1)) * 3.0f;

            _particles.Add(new TronParticle { Position = dropPos + new Vector3(0, 0.05f, 0), Velocity = vel, Life = 1.0f });
        }
    }

    public void Build(RenderQueue queue)
    {
        queue.Clear();
        UpdateAndRenderParticles(queue);
        AddTable(queue);
        AddBalls(queue);
        AddTrails(queue);

        if (match.Mode is GameStateMode.Aim or GameStateMode.Power)
        {
            AddCue(queue);
        }
    }

    private void UpdateAndRenderParticles(RenderQueue queue)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Position += p.Velocity * 0.016f;
            p.Velocity.Y -= 9.81f * 0.016f;
            p.Life -= 0.025f;

            if (p.Life <= 0) { _particles.RemoveAt(i); continue; }

            queue.Add(new RenderItem
            {
                Mesh = MeshType.Cube,
                Material = MaterialType.Trail,
                Color = new Vector4(1.0f, 0.0f, 0.5f, p.Life),
                World = Matrix4x4.CreateScale(p.Life * 0.05f) * Matrix4x4.CreateTranslation(p.Position)
            });
        }
    }

    private void AddBalls(RenderQueue queue)
    {
        float bedThickness = 0.02f;
        float bedY = -bedThickness / 2f;
        float floorLevel = bedY + (bedThickness / 2f); // Removed manual 0.001f, handled by ApplyZDepth now

        foreach (var b in engine.RenderData)
        {
            var phys = engine.PhysicsStates[b.Id];
            if (phys.Position.Y < -100f)
            {
                continue;
            }

            // Render a soft drop shadow directly underneath the ball onto the slate
            queue.Add(new RenderItem
            {
                Color = new Vector4(0.0f, 0.0f, 0.02f, 0.45f), // Soft dark shadow, 45% opacity
                Mesh = MeshType.Circle,
                Material = MaterialType.Trail,
                World = Matrix4x4.CreateScale(BilliardsConstants.BallRadius * 2.0f) * Matrix4x4.CreateTranslation(ApplyZDepth(new Vector3(phys.Position.X, floorLevel, phys.Position.Z), 0.001f))
            });

            // Balls are solid (W = 1.0f)
            Vector4 color = b.Type == BallType.Cue ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(1f, 0.1f, 0.5f, 1f);

            queue.Add(new RenderItem
            {
                Color = color,
                Mesh = MeshType.Sphere,
                Material = MaterialType.Ball,
                World = Matrix4x4.CreateFromQuaternion(b.Orientation) * Matrix4x4.CreateTranslation(phys.Position)
            });

            if (b.ChalkMarkLocal.HasValue)
            {
                queue.Add(new RenderItem
                {
                    Color = new Vector4(0.2f, 0.6f, 1.0f, 1.0f),
                    Mesh = MeshType.Sphere,
                    Material = MaterialType.Ball,
                    World = Matrix4x4.CreateScale(0.15f) * Matrix4x4.CreateTranslation(b.ChalkMarkLocal.Value) * Matrix4x4.CreateFromQuaternion(b.Orientation) * Matrix4x4.CreateTranslation(phys.Position)
                });
            }

            if (b.HitMarksWorld == null)
            {
                continue;
            }

            for (int i = 0; i < b.HitMarksWorld.Length; i++)
            {
                if (b.HitMarksWorld[i] == Vector3.Zero)
                {
                    continue;
                }

                // Hit markers are transparent (W = 0.8f)
                queue.Add(new RenderItem
                {
                    Mesh = MeshType.Circle,
                    Material = MaterialType.HitMark,
                    Color = new Vector4(1.0f, 0.9f, 0.2f, 0.8f),
                    World = Matrix4x4.CreateScale(BilliardsConstants.BallRadius * 2.0f, 1.0f, BilliardsConstants.BallRadius * 2.0f) * Matrix4x4.CreateTranslation(ApplyZDepth(b.HitMarksWorld[i]))
                });
            }
        }
    }

    private void AddTable(RenderQueue queue)
    {
        float bedW = BilliardsConstants.TableWidth;
        float bedL = BilliardsConstants.TableLength;

        float bedThickness = 0.02f;
        float bedY = -bedThickness / 2f;
        float railHeight = 0.07f;

        Vector4 bedCol = new Vector4(0.4f, 0.7f, 0.8f, 1.0f);
        Vector4 railCol = new Vector4(0.05f, 0.85f, 1.0f, 1.0f);

        // Find the length of the laser to perfectly match the 45 degree slate cut
        float laserLength = 0.12f; // safe default
        if (engine.TableLayout.Pockets.Length > 0)
        {
            laserLength = (engine.TableLayout.Pockets[0].P1 - engine.TableLayout.Pockets[0].P2).Length();
        }

        // We extend the main bounding box outward under the rails to ensure zero visual gaps
        float over = 0.05f;
        float w = bedW + (over * 2);
        float l = bedL + (over * 2);

        // The mathematical cut depth from the corner of the bounding box
        float c = laserLength / 1.41421356f;
        float cut = c + over;

        // --- 1. Core vertical strip ---
        queue.Add(CreateBedPiece(w - (cut * 2), l, bedThickness, 0, 0, bedY, bedCol));

        // --- 2. Core horizontal strip ---
        queue.Add(CreateBedPiece(w, l - (cut * 2), bedThickness, 0, 0, bedY, bedCol));

        // --- 3. Four 45-degree corner fillers ---
        // By rotating a square 45 degrees, its straight edge perfectly forms the 45-degree pocket cutout!
        float cornerSize = cut * 1.41421356f;
        float cx = (w / 2f) - cut;
        float cz = (l / 2f) - cut;

        Matrix4x4 rot45 = Matrix4x4.CreateRotationY(MathF.PI / 4f);

        queue.Add(CreateRotatedBedPiece(cornerSize, bedThickness, cx, cz, bedY, bedCol, rot45)); // Top Right
        queue.Add(CreateRotatedBedPiece(cornerSize, bedThickness, -cx, cz, bedY, bedCol, rot45)); // Top Left
        queue.Add(CreateRotatedBedPiece(cornerSize, bedThickness, cx, -cz, bedY, bedCol, rot45)); // Bottom Right
        queue.Add(CreateRotatedBedPiece(cornerSize, bedThickness, -cx, -cz, bedY, bedCol, rot45)); // Bottom Left

        // Draw the solid rail mesh
        queue.Add(new RenderItem
        {
            Mesh = MeshType.TableRails,
            Material = MaterialType.Cushion,
            Color = railCol,
            World = Matrix4x4.Identity // Vertices are already in perfect world space
        });

        // Draw the pocket lasers
        // Bed is at Y=0.0 to Y=-0.02. Therefore, passing railHeight / 2 forces the laser base to be perfectly flush at Y = 0.0
        foreach (var pocket in engine.TableLayout.Pockets)
        {
            queue.Add(CreatePocketLaser(pocket, railHeight, railHeight / 2f));
        }
    }

    private RenderItem CreateBedPiece(float w, float l, float thickness, float x, float z, float y, Vector4 color) =>
        new RenderItem
        {
            Mesh = MeshType.Cube,
            Material = MaterialType.Table,
            Color = color,
            World = Matrix4x4.CreateScale(w, thickness, l) * Matrix4x4.CreateTranslation(x, y, z)
        };

    private RenderItem CreateRotatedBedPiece(float size, float thickness, float x, float z, float y, Vector4 color, Matrix4x4 rot) =>
        new RenderItem
        {
            Mesh = MeshType.Cube,
            Material = MaterialType.Table,
            Color = color,
            World = Matrix4x4.CreateScale(size, thickness, size) * rot * Matrix4x4.CreateTranslation(x, y, z)
        };

    private RenderItem CreatePocketLaser(LongShot.Table.PocketBeam pocket, float beamHeight, float beamY)
    {
        Vector3 dir = pocket.P2 - pocket.P1;
        Vector3 center = pocket.P1 + (dir / 2f);

        center.Y = beamY;

        // Pockets stay transparent (W = 0.9f) since they are just thin single-plane lines
        return new RenderItem
        {
            Mesh = MeshType.Cube,
            Material = MaterialType.Trail,
            Color = new Vector4(1.0f, 0.0f, 0.3f, 0.9f),
            World = Matrix4x4.CreateScale(0.015f, beamHeight, dir.Length()) * Matrix4x4.CreateRotationY(MathF.Atan2(dir.X, dir.Z)) * Matrix4x4.CreateTranslation(center)
        };
    }

    private void AddCue(RenderQueue queue)
    {
        var cueBall = engine.PhysicsStates[0];
        if (cueBall.Position.Y < -100f)
        {
            return;
        }

        Vector3 flatForward = new Vector3(-MathF.Sin(match.CueYaw), 0, -MathF.Cos(match.CueYaw));
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, flatForward));

        Matrix4x4 pitchRot = Matrix4x4.CreateFromAxisAngle(right, match.CuePitch);
        Vector3 actualForward = Vector3.Transform(flatForward, pitchRot);
        Vector3 actualUp = Vector3.Cross(actualForward, right);

        Vector3 tipWorldOffset = (right * match.TipOffset.X * BilliardsConstants.BallRadius) +
                                 (actualUp * match.TipOffset.Y * BilliardsConstants.BallRadius);

        queue.Add(new RenderItem
        {
            Mesh = MeshType.Cylinder,
            Material = MaterialType.Cue,
            Color = new Vector4(0.0f, 0.8f, 1.0f, 1.0f),
            World = CuePoseSolver.Solve(cueBall.Position + tipWorldOffset, -match.CuePitch, match.CueYaw, match.CueStickOffset)
        });
    }

    private void AddTrails(RenderQueue queue)
    {
        foreach (BallRenderData render in engine.RenderData)
        {
            if (engine.PhysicsStates[render.Id].Position.Y < -100f)
            {
                continue;
            }

            for (int i = 0; i < render.Trail.Length; i++)
            {
                var pt = render.Trail[i];
                if (pt.Position == Vector3.Zero)
                {
                    continue;
                }

                queue.Add(new RenderItem
                {
                    Mesh = MeshType.Quad,
                    Material = MaterialType.Trail,
                    Color = Vector4.Lerp(new Vector4(0.0f, 0.8f, 1.0f, 0.3f), new Vector4(1.0f, 0.0f, 0.5f, 0.8f), Math.Clamp(pt.Spin / 150f, 0f, 1f)),
                    World = Matrix4x4.CreateScale(0.004f, 1.0f, 0.015f)
                    * Matrix4x4.CreateRotationY(MathF.Atan2(pt.Direction.X, pt.Direction.Z))
                    * Matrix4x4.CreateTranslation(ApplyZDepth(pt.Position))
                });
            }
        }
    }

    private Vector3 ApplyZDepth(Vector3 position, float yOffset = 0.0015f)
    {
        // Lifts a position slightly on the Y axis to prevent Z-fighting with the table bed
        return new Vector3(position.X, position.Y + yOffset, position.Z);
    }
}