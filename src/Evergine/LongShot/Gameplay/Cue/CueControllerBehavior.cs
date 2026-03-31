using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Services;
using Evergine.Mathematics;

namespace Longshot.Gameplay.Cue;

public class CueControllerBehavior : Behavior
{
    [BindComponent]
    private readonly Transform3D transform;

    [BindService]
    private readonly GraphicsPresenter graphicsPresenter;

    // Exposed settings
    [DataMember] public float MasterSensitivity { get; set; } = 1.0f;
    [DataMember] public float CueVisualDrive { get; set; } = 0.005f; // Meters per mouse pixel
    [DataMember] public float MaxPullback { get; set; } = 0.8f; // Positive distance away from ball

    public float CueOffset { get; private set; }
    public float PreviousCueOffset { get; private set; }
    public bool HasPulledBack => CueOffset > 0.05f;
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public Vector2 TipOffset { get; set; }

    // The smoothed, raw velocity
    public float CurrentVelocity { get; private set; }

    // Ring buffer to smooth raw mouse input over 4 frames
    private readonly Queue<float> _velocityBuffer = new Queue<float>(4);

    protected override void Update(TimeSpan gameTime)
    {

    }

    public void UpdateStroke(float dt)
    {
        float safeDt = Math.Max(dt, 0.0001f);
        var mouse = graphicsPresenter.FocusedDisplay.MouseDispatcher;

        // Raw 1:1 mapping: Mouse Y pixels to meters
        float rawMouseMovement = -mouse.PositionDelta.Y * (MasterSensitivity * CueVisualDrive);

        PreviousCueOffset = CueOffset;
        CueOffset += rawMouseMovement;
        CueOffset = Math.Max(CueOffset, -0.05f); // Allow tiny follow-through before clamping
        CueOffset = Math.Min(CueOffset, MaxPullback);

        // Calculate instantaneous velocity (meters per second)
        float instantaneousVelocity = rawMouseMovement / safeDt;

        // Push to ring buffer, pop oldest
        _velocityBuffer.Enqueue(instantaneousVelocity);
        if (_velocityBuffer.Count > 4)
        {
            _velocityBuffer.Dequeue();
        }

        // The true stroke velocity is the average of the last few frames
        // We only care about positive velocity (moving TOWARD the ball, which we'll say is negative offset change)
        CurrentVelocity = _velocityBuffer.Average();
    }

    public void ResetStroke()
    {
        CueOffset = 0f;
        PreviousCueOffset = 0f;
        CurrentVelocity = 0f;
        _velocityBuffer.Clear();
    }

    public void UpdateVisualTransform(Vector3 numCueBallPos)
    {
        Vector3 cueBallPos = numCueBallPos;

        // Calculate the English Tip Offset (Up/Right relative to the camera)
        Vector3 flatForward = new Vector3(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, flatForward));

        Matrix4x4 pitchRot = Matrix4x4.CreateFromAxisAngle(right, Pitch);
        Vector3 actualForward = Vector3.Transform(flatForward, pitchRot);
        Vector3 actualUp = Vector3.Cross(actualForward, right);

        Vector3 tipWorldOffset = (right * TipOffset.X * 0.0285f) +
                                 (actualUp * TipOffset.Y * 0.0285f);

        // Position the Pivot at the ball + english offset + pullback offset
        transform.Position = cueBallPos + tipWorldOffset + (actualForward * CueOffset);

        // Rotate the Pivot to look directly at the impact point
        transform.LookAt(cueBallPos + tipWorldOffset);
    }

}
