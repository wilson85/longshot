using System;
using System.Numerics;
using LongShot.Rendering;

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
        // Cache the physics state span for fast lookups
        var physicsStates = engine.PhysicsStates;

        foreach (var b in engine.RenderData)
        {
            // IMPORTANT: Fetch the pure physics state using the ID
            var phys = physicsStates[b.Id];

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
                // Combine visual orientation with physical position
                World = Matrix4x4.CreateFromQuaternion(b.Orientation) *
                      Matrix4x4.CreateTranslation(phys.Position)
            });

            if (b.ChalkMarkLocal.HasValue)
            {
                // Scale it down, move it to the edge of the ball, then rotate/translate it with the ball
                var markWorld = Matrix4x4.CreateScale(0.15f) * Matrix4x4.CreateTranslation(b.ChalkMarkLocal.Value) *
                                Matrix4x4.CreateFromQuaternion(b.Orientation) *
                                Matrix4x4.CreateTranslation(phys.Position);

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
                float shadowSize = BilliardsEngine.BallRadius * 2.0f;

                queue.Add(new RenderItem
                {
                    Mesh = MeshType.Circle, // Switch to our new perfect circle!
                    Material = MaterialType.HitMark,
                    Color = new Vector4(1.0f, 0.9f, 0.2f, 0.8f),
                    World = Matrix4x4.CreateScale(shadowSize, 1.0f, shadowSize) * Matrix4x4.CreateTranslation(hit)
                });
            }
        }
    }

    private void AddTable(RenderQueue queue)
    {
        // Pull dimensions straight from the physics engine to guarantee alignment
        float bedWidth = BilliardsEngine.TableWidth;
        float bedLength = BilliardsEngine.TableLength;
        float bedThickness = 0.1f;

        float railWidth = 0.15f;
        float railHeight = (bedThickness + BilliardsEngine.BallRadius) * 1; // Needs to be taller than the bed to block balls

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
            Material = MaterialType.Cushion,
            Color = color,
            World = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position)
        };
    }

    private void AddCue(RenderQueue queue)
    {
        // Grab the physical cue ball
        var cueBall = engine.PhysicsStates[0];

        // 1. Calculate the Left/Right/Up/Down shift for ENGLISH
        Vector3 forward = new Vector3(-MathF.Sin(camera.Yaw), 0, -MathF.Cos(camera.Yaw));
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 up = Vector3.Cross(forward, right);

        // Takes the 0.6 Max English clamp and shrinks it to the physical size of the ball.
        float radius = BilliardsEngine.BallRadius;
        Vector3 tipWorldOffset = (right * match.TipOffset.X * radius) + (up * match.TipOffset.Y * radius);

        // Add to physical position
        Vector3 shiftedTargetPos = cueBall.Position + tipWorldOffset;

        // 2. Solve the final matrix using the shifted position and the PULLBACK offset
        Matrix4x4 cueWorldMatrix = CuePoseSolver.Solve(
            shiftedTargetPos,
            camera.Pitch,
            camera.Yaw,
            match.CueStickOffset); // Uses CueStickOffset (float) for the pullback

        queue.Add(new RenderItem
        {
            Mesh = MeshType.Cylinder,
            Material = MaterialType.Cue,
            Color = new Vector4(0.0f, 0.8f, 1.0f, 1.0f),
            World = cueWorldMatrix
        });
    }

    private void AddTrails(RenderQueue queue)
    {
        // Loop through the RenderData which safely holds the Trails
        foreach (var render in engine.RenderData)
        {
            for (int i = 0; i < render.Trail.Length; i++)
            {
                var point = render.Trail[i];
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