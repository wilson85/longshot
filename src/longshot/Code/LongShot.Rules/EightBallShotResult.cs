using System.Collections.Generic;

namespace LongShot.Rules;

/// <summary>
/// Outcome of one 8-ball shot, computed by <see cref="EightBallRules"/> from the
/// engine's <see cref="LongShot.Shot.ShotSummary"/>. Captures every state transition the
/// rules layer cares about: fouls, group assignment, turn change, game win/loss.
/// </summary>
public sealed class EightBallShotResult
{
    public int Player { get; init; }
    public BallGroup? PlayerGroupAtStart { get; init; }

    public bool Foul { get; init; }
    public string FoulReason { get; init; } = string.Empty;

    public List<int> PocketedBalls { get; init; } = new();

    /// <summary>True if the open table was just assigned by this shot.</summary>
    public bool GroupJustAssigned { get; init; }
    public BallGroup? AssignedToShootingPlayer { get; init; }

    /// <summary>True if the shooter's turn ended (foul, or no own ball potted).</summary>
    public bool TurnChanged { get; init; }

    /// <summary>True if the opponent gets ball-in-hand placement.</summary>
    public bool BallInHand { get; init; }

    public bool GameWon { get; init; }
    public bool GameLost { get; init; }

    public string Description { get; init; } = string.Empty;
}
