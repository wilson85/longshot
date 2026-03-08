using System.Numerics;

namespace LongShot.Engine;

public sealed class BilliardsSceneBuilder(
    BilliardsEngine engine,
    Camera camera,
    MatchManager match)
{
    public void Build(RenderQueue queue)
    {
        queue.Clear();

        AddTable(queue);
        AddBalls(queue);
        AddTrails(queue);

        if (match.Mode == GameStateMode.Aim ||
            match.Mode == GameStateMode.Power)
        {
            AddCue(queue);
        }
    }

    private void AddBalls(RenderQueue queue)
    {
        foreach (ref readonly var b in engine.ActiveBalls)
        {
            Vector4 color = b.Type switch
            {
                BallType.Cue => new Vector4(1f, 1f, 1f, 1f),
                BallType.Normal => new Vector4(1f, 0.1f, 0.5f, 1f),
                _ => new Vector4(0.5f, 0.5f, 0.5f, 1)
            };

            queue.Add(new RenderItem
            {
                Color = color,
                Mesh = MeshType.Sphere,
                Material = MaterialType.Ball,
                World = b.Position == Vector3.Zero
                    ? Matrix4x4.Identity
                    : Matrix4x4.CreateFromQuaternion(b.Orientation) *
                      Matrix4x4.CreateTranslation(b.Position)
            });

            if (b.ChalkMarkLocal.HasValue)
            {
                // Scale it down, move it to the edge of the ball, then rotate/translate it with the ball
                var markWorld = Matrix4x4.CreateScale(0.15f) * Matrix4x4.CreateTranslation(b.ChalkMarkLocal.Value) *
                                Matrix4x4.CreateFromQuaternion(b.Orientation) *
                                Matrix4x4.CreateTranslation(b.Position);

                queue.Add(new RenderItem
                {
                    Color = new Vector4(0.2f, 0.6f, 1.0f, 1.0f), // Pool cue chalk blue
                    Mesh = MeshType.Sphere,
                    Material = MaterialType.Ball,
                    World = markWorld
                });
            }


            if (b.HitMarksWorld == null) continue;
            // Render the Impact Shadows!

            for (int i = 0; i < b.HitMarksWorld.Length; i++)
            {
                var hit = b.HitMarksWorld[i];
                if (hit == Vector3.Zero) continue;

                // Exactly the diameter of the ball
                float shadowSize = GameSettings.StandardBallRadius * 2.0f;

                queue.Add(new RenderItem
                {
                    Mesh = MeshType.Circle, // Switch to our new perfect circle!
                    Material = MaterialType.Trail,
                    Color = new Vector4(1.0f, 0.9f, 0.2f, 0.8f),
                    World = Matrix4x4.CreateScale(shadowSize, 1.0f, shadowSize) * Matrix4x4.CreateTranslation(hit)
                });
            }
        }
    }

    private void AddTable(RenderQueue queue)
    {
        // Note: You will want to adjust these dimensions to match 
        // the hardcoded bounds in your physics BilliardsEngine!
        float bedWidth = 1.27f;  // ~4.1 feet (Standard 9ft table width)
        float bedLength = 2.54f; // ~8.3 feet (Standard 9ft table length)
        float bedThickness = 0.1f;

        float railWidth = 0.15f;
        float railHeight = (bedThickness + GameSettings.StandardBallRadius) * 1; // Needs to be taller than the bed to block balls

        // We put the top of the bed exactly at Y = 0. 
        // This assumes your engine rests the balls at Y = ballRadius.
        float bedY = -bedThickness / 2f;

        // Center the rails vertically so they stick up above the bed
        float railY = bedY + (railHeight / 2f) - (bedThickness / 4f);

        Vector4 bedColor = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
        Vector4 railColor = new Vector4(0.0f, 0.8f, 1.0f, 1.0f); 

        // 1. Add the Playing Surface (Bed)
        queue.Add(new RenderItem
        {
            Mesh = MeshType.Cube,
            Material = MaterialType.Table,
            Color = bedColor,
            World = Matrix4x4.CreateScale(bedWidth, bedThickness, bedLength) * Matrix4x4.CreateTranslation(0, bedY, 0),
        });

        // 2. Add the Rails (Frame)
        // Left Rail
        queue.Add(CreateRail(
            new Vector3(-bedWidth / 2f - railWidth / 2f, railY, 0),
            new Vector3(railWidth, railHeight, bedLength + (railWidth * 2)),
            railColor));

        // Right Rail
        queue.Add(CreateRail(
            new Vector3(bedWidth / 2f + railWidth / 2f, railY, 0),
            new Vector3(railWidth, railHeight, bedLength + (railWidth * 2)),
            railColor));

        // Top Rail
        queue.Add(CreateRail(
            new Vector3(0, railY, -bedLength / 2f - railWidth / 2f),
            new Vector3(bedWidth, railHeight, railWidth),
            railColor));

        // Bottom Rail
        queue.Add(CreateRail(
            new Vector3(0, railY, bedLength / 2f + railWidth / 2f),
            new Vector3(bedWidth, railHeight, railWidth),
            railColor));
    }

    // Helper method to keep the AddTable method readable
    private RenderItem CreateRail(Vector3 position, Vector3 scale, Vector4 color)
    {
        return new RenderItem
        {
            Mesh = MeshType.Cube,
            Material = MaterialType.Table,
            Color = color,
            World = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position)
        };
    }

    private void AddCue(RenderQueue queue)
    {
        var cueBall = engine.ActiveBalls[0];

        // Assuming your Camera class exposes Yaw. 
        // If not, you can calculate it from camera.Target - cueBall.Position
        Matrix4x4 cueWorldMatrix = CuePoseSolver.Solve(
            cueBall.Position,
            camera.Yaw,
            match.CueStickOffset);

        queue.Add(new RenderItem
        {
            Mesh = MeshType.Cube, // or MeshType.Cylinder if you have one!
            Material = MaterialType.Cue, // Update this if you have a specific material
            Color = new Vector4(0.8f, 0.6f, 0.3f, 1), // A nice wood color
            World = cueWorldMatrix
        });
    }

    private void AddTrails(RenderQueue queue)
    {
        foreach (ref readonly var b in engine.ActiveBalls)
        {
            for (int i = 0; i < b.Trail.Length; i++)
            {
                var point = b.Trail[i];
                if (point.Position == Vector3.Zero) continue;

                float spinIntensity = Math.Clamp(point.Spin / 150f, 0f, 1f);

                Vector4 color = Vector4.Lerp(
                    new Vector4(0.0f, 0.8f, 1.0f, 0.3f),
                    new Vector4(1.0f, 0.0f, 0.5f, 0.8f),
                    spinIntensity
                );

                // Calculate the Yaw angle from the direction vector
                float yaw = MathF.Atan2(point.Direction.X, point.Direction.Z);

                // Scale: Make it thin on X (width), and long on Z (length)
                var scale = Matrix4x4.CreateScale(0.004f, 1.0f, 0.015f);
                var rot = Matrix4x4.CreateRotationY(yaw);
                var trans = Matrix4x4.CreateTranslation(point.Position);

                queue.Add(new RenderItem
                {
                    Mesh = MeshType.Quad,
                    Material = MaterialType.Trail,
                    Color = color,
                    World = scale * rot * trans // Apply scale, then rotate, then translate
                });
            }
        }
    }
}