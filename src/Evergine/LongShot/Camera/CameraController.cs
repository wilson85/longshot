using System;
using System.Runtime.Serialization;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Mathematics;

namespace Longshot.Camera;


public class CameraController : Behavior
{
    [BindComponent]
    private Transform3D transform;

    [DataMember]
    public float TransitionSpeed { get; set; } = 5.0f;

    [DataMember]
    public float Distance { get; set; } = 5.0f;

    private float pitch = 0.5f;
    private float yaw = 0f;
    private Vector3 targetPosition = Vector3.Zero;

    protected override void Update(TimeSpan gameTime)
    {
        float dt = (float)gameTime.TotalSeconds;

        // 1. You would grab input and target positions here 
        // (similar to your PoC logic)

        // 2. Calculate your new position based on Pitch/Yaw/Distance
        Vector3 newPosition = targetPosition + new Vector3(
            Distance * MathF.Cos(pitch) * MathF.Sin(yaw),
            Distance * MathF.Sin(pitch),
            Distance * MathF.Cos(pitch) * MathF.Cos(yaw)
        );

        // 3. Smoothly Lerp the Evergine Transform
        transform.Position = Vector3.Lerp(transform.Position, newPosition, dt * TransitionSpeed);

        // 4. Tell Evergine to look at the target (replaces your ViewMatrix code)
        transform.LookAt(targetPosition);
    }
}
