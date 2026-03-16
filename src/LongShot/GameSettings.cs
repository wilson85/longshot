namespace LongShot;

public static class GameSettings
{
    public static float MasterSensitivity = 0.00225f;

    // Multipliers for specific modes
    public static float FineModifier = 0.2f;
    public static float PowerModifier = 1f;
    public static float ViewModifier = 1.0f;

    // --- Visuals vs. Physics Decoupling ---
    public static float CueVisualDrive = 0.05f;
    public static float PhysicsForceMultiplier = 1.0f;

    // --- Physics Constants ---
    public const float StandardBallRadius = 0.028575f;
    public const float MaxPullback = -1.00f;
    public const float MaxImpactSpeed = 1000.0f;

    // --- Player View Tuning (Dropping down to the cue) ---

    // Low pitch (0.15 - 0.22) makes you feel like your chin is near the table.
    public static float PlayerViewPitch = 0.18f;

    // Aiming Offset: Lower this to bring the camera closer to the table surface.
    public static float PlayerViewVerticalOffset = 0.02f;

    // --- Camera Parameters (Standing View) ---
    public static float CameraMinDistance = 0.2f;
    public static float CameraMaxDistance = 5.0f;
    public static float CameraDefaultDistance = 1.1f;
    public static float CameraFieldOfView = 0.60f;

    // Standing Offset: Higher value (0.15+) represents standing height.
    // When switching to Aim, the camera will now move DOWN toward PlayerViewVerticalOffset.
    public static float CameraTargetVerticalOffset = 0.25f;

    // Rotation limits
    public static float CameraMinPitch = 0.05f;
    public static float CameraMaxPitch = MathF.PI / 2.0f - 0.05f;
}
