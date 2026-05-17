using Sandbox;

namespace Longshot;

/// <summary>
/// Standard fly-cam: WASD to move, mouse to look, Shift to sprint, Ctrl to crawl.
/// Drop on the Camera GameObject so you can navigate the scene at runtime without coding a real
/// player controller. Disable / remove once a proper player controller exists.
///
/// Action bindings used (defined in Project Settings → Input Actions, defaults shipped by s&amp;box):
/// <list type="bullet">
///   <item><c>Forward / Backward / Left / Right</c> via <see cref="Input.AnalogMove"/></item>
///   <item><c>Run</c> (Shift by default) — 3× speed</item>
///   <item><c>Walk</c> (Ctrl by default) — 0.25× speed</item>
/// </list>
/// </summary>
public sealed class FreeCam : Component
{
    /// <summary>Base movement speed in s&amp;box units per second.</summary>
    [Property] public float Speed { get; set; } = 250f;

    /// <summary>Mouse-look sensitivity multiplier applied to <see cref="Input.AnalogLook"/>.</summary>
    [Property] public float MouseSensitivity { get; set; } = 1f;

    /// <summary>If true, mouse-look only applies while right mouse button is held. Otherwise it's always-on.</summary>
    [Property] public bool RequireRightMouseToLook { get; set; } = false;

    /// <summary>Pitch clamp in degrees so the camera doesn't flip past straight-up/down.</summary>
    [Property] public float PitchClamp { get; set; } = 89f;

    private Angles _eyeAngles;

    protected override void OnStart()
    {
        // Seed eye angles from the GameObject's current rotation so we don't snap on the first frame.
        _eyeAngles = WorldRotation.Angles();
    }

    protected override void OnUpdate()
    {
        // ---- Look ----
        bool canLook = !RequireRightMouseToLook || Input.Down("Attack2");
        if (canLook)
        {
            _eyeAngles += Input.AnalogLook * MouseSensitivity;
            _eyeAngles.pitch = _eyeAngles.pitch.Clamp(-PitchClamp, PitchClamp);
            _eyeAngles.roll = 0f;
            WorldRotation = _eyeAngles.ToRotation();
        }

        // ---- Move ----
        var move = Input.AnalogMove;
        if (move.LengthSquared > 0f)
        {
            float speed = Speed;
            if (Input.Down("Run")) speed *= 3f;
            if (Input.Down("Walk")) speed *= 0.25f;
            WorldPosition += WorldRotation * move * speed * Time.Delta;
        }
    }
}
