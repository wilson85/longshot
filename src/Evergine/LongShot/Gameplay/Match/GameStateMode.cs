namespace Longshot.Gameplay.Match;

public enum GameStateMode 
{
    /// <summary>
    /// Camera is locked to 360 around the cue ball, player can adjust yaw/pitch for english and aiming. Transition to Shoot on 'S' button.
    /// </summary>
    Aim,
    /// <summary>
    /// Camera is locked in position
    /// English can be applied with 'E' but camera does not move with cue at this point.
    /// </summary>
    Shoot,
    /// <summary>
    /// Physics enging is playing the shot. Can be sped up with 'F' button. Once all balls are asleep, transition back to Aim.
    /// </summary>
    Simulate,
    /// <summary>
    /// A ghost ball is draw on the table and the camera is locked onto it, it can be moved around with the camera using WSAD. Ghost ball is only a visual helper and does not interact with the physics engine. Ghost ball fades when transitioning to aim.
    /// </summary>
    GhostView,
    Replay,
    FreeView
}
