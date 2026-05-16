using System.Collections.Generic;
using System.Linq;
using LongShot.Engine;
using LongShot.Shot;

namespace LongShot.Rules;

/// <summary>
/// Minimal 8-ball rules engine. Implements the most-commonly-checked rules:
/// <list type="bullet">
///   <item>Standard ball-to-group convention: id 0 = cue, 1-7 = solids, 8 = the eight, 9-15 = stripes.</item>
///   <item>Scratch detection (cue ball pocketed = foul, opponent ball-in-hand).</item>
///   <item>"No contact" foul (cue ball didn't touch any object ball).</item>
///   <item>Wrong-group foul (cue contacted an opposing-group ball first, after groups assigned).</item>
///   <item>Open-table group assignment on the first legal pot.</item>
///   <item>8-ball pot mid-game = game loss.</item>
///   <item>8-ball pot after own group cleared, no foul = game win.</item>
///   <item>8-ball pot after own group cleared with a foul = game loss.</item>
/// </list>
///
/// Deliberately NOT modeled here:
/// <list type="bullet">
///   <item>Called pocket on the 8-ball.</item>
///   <item>"No rail after contact" foul (object ball must hit a rail OR be pocketed).</item>
///   <item>Break-specific fouls (cue not driven past head string, no 4-rail kick, etc.).</item>
///   <item>Ball-in-hand placement validation (the rule fires, but the host actually moves the cue).</item>
/// </list>
/// These layer on as additional <see cref="IShotObserver"/>s or extensions to this class.
/// </summary>
public sealed class EightBallRules : IShotObserver
{
    /// <summary>Engine ball id of the cue ball.</summary>
    public const int CueBallId = 0;

    /// <summary>Engine ball id of the 8-ball.</summary>
    public const int EightBallId = 8;

    public int CurrentPlayer { get; private set; } = 1;

    /// <summary>True until the first legal pot of a non-8 ball assigns the groups.</summary>
    public bool OpenTable { get; private set; } = true;

    public BallGroup? Player1Group { get; private set; }
    public BallGroup? Player2Group { get; private set; }

    public bool GameOver { get; private set; }
    public int Winner { get; private set; }    // 0 = none, 1 = player 1, 2 = player 2
    public string WinReason { get; private set; } = string.Empty;

    public EightBallShotResult LastShot { get; private set; }
    public List<EightBallShotResult> History { get; } = new();

    public void OnShotStart(ShotContext context) { }

    /// <summary>
    /// Initialise the rules to a mid-game state with groups already assigned. Useful for
    /// tests that exercise rules logic that only fires after the open-table phase.
    /// </summary>
    public void SeedAssignedGroups(int player, BallGroup group)
    {
        OpenTable = false;
        var opposite = group == BallGroup.Solid ? BallGroup.Stripe : BallGroup.Solid;
        if (player == 1) { Player1Group = group; Player2Group = opposite; }
        else { Player2Group = group; Player1Group = opposite; }
        CurrentPlayer = player;
    }

    public void OnShotEnd(ShotSummary summary)
    {
        if (GameOver) return;

        var result = Evaluate(summary);
        LastShot = result;
        History.Add(result);
        Apply(result);
    }

    /// <summary>Static helper: map an engine ball id to its 8-ball group.</summary>
    public static BallGroup GroupOf(int ballId) => ballId switch
    {
        CueBallId => BallGroup.Cue,
        EightBallId => BallGroup.Eight,
        >= 1 and <= 7 => BallGroup.Solid,
        >= 9 and <= 15 => BallGroup.Stripe,
        _ => BallGroup.Cue, // out-of-range; treat as cue for safety
    };

    /// <summary>The group of the currently-shooting player, or null if open table.</summary>
    public BallGroup? CurrentPlayerGroup =>
        CurrentPlayer == 1 ? Player1Group : Player2Group;

    public BallGroup? OpponentGroup =>
        CurrentPlayer == 1 ? Player2Group : Player1Group;

    private EightBallShotResult Evaluate(ShotSummary summary)
    {
        var pocketed = summary.Pocketings.Select(p => p.BallId).ToList();
        bool cuePocketed = pocketed.Contains(CueBallId);
        bool eightPocketed = pocketed.Contains(EightBallId);

        int firstContactId = summary.FirstContactBallId;
        BallGroup? firstContactGroup = firstContactId >= 0 ? GroupOf(firstContactId) : null;

        BallGroup? playerGroup = CurrentPlayerGroup;
        bool ownGroupRemainingBeforeShot = !OpenTable
            && playerGroup.HasValue
            && CountUnpocketedInGroup(summary.StateAtStrike, playerGroup.Value) > 0;
        bool ownGroupClearedAfterShot = !OpenTable
            && playerGroup.HasValue
            && CountUnpocketedInGroup(summary.StateAtRest, playerGroup.Value) == 0;

        // --- Foul detection ---
        string foulReason = null;
        if (cuePocketed)
            foulReason = "cue ball pocketed (scratch)";
        else if (firstContactId < 0)
            foulReason = "cue ball did not contact any ball";
        else if (!OpenTable && playerGroup.HasValue)
        {
            // Once groups are assigned, the cue must contact own-group first.
            // The 8-ball is only a legal first-contact target once own group is cleared.
            if (firstContactGroup == BallGroup.Eight && ownGroupRemainingBeforeShot)
                foulReason = "cue contacted the 8-ball while own group still had balls";
            else if (firstContactGroup == OpponentGroup)
                foulReason = "cue contacted opponent's group ball first";
        }
        bool foul = foulReason != null;

        // --- 8-ball pot resolution (wins/losses take priority over normal scoring) ---
        bool gameWon = false, gameLost = false;
        string description;
        if (eightPocketed)
        {
            // If the player's group still had balls on the table when the 8 went down, lose.
            // OR if any foul on the same shot, lose. Otherwise: win.
            if (ownGroupRemainingBeforeShot || OpenTable || foul)
            {
                gameLost = true;
                description = OpenTable
                    ? "8-ball pocketed on an open table - game loss"
                    : foul
                        ? "8-ball pocketed with a foul - game loss"
                        : "8-ball pocketed before clearing own group - game loss";
            }
            else
            {
                gameWon = true;
                description = "8-ball pocketed legally - game won";
            }
        }
        else
        {
            description = foul ? foulReason : DescribeLegalShot(pocketed, playerGroup, OpenTable);
        }

        // --- Group assignment on open table ---
        // Standard rule: groups assign on the first shot in which the shooter pots a
        // non-8 ball legally. If both solids AND stripes are pocketed on the same open-
        // table shot, the table stays open.
        bool groupJustAssigned = false;
        BallGroup? assigned = null;
        if (OpenTable && !foul && !eightPocketed)
        {
            bool pottedSolid = pocketed.Any(id => GroupOf(id) == BallGroup.Solid);
            bool pottedStripe = pocketed.Any(id => GroupOf(id) == BallGroup.Stripe);
            if (pottedSolid && !pottedStripe)
            {
                groupJustAssigned = true;
                assigned = BallGroup.Solid;
            }
            else if (pottedStripe && !pottedSolid)
            {
                groupJustAssigned = true;
                assigned = BallGroup.Stripe;
            }
            // both → table stays open. neither → table stays open, no change.
        }

        // --- Turn-change logic ---
        // Foul → turn changes, opponent gets ball-in-hand.
        // Otherwise: shooter continues if they legally pocketed at least one own-group ball
        // (or any non-8 ball while the table was open).
        bool turnChanged;
        if (foul)
        {
            turnChanged = true;
        }
        else if (gameWon || gameLost)
        {
            turnChanged = false; // game over
        }
        else
        {
            bool pocketedSomethingScorable = OpenTable
                ? pocketed.Any(id => GroupOf(id) != BallGroup.Eight && id != CueBallId)
                : pocketed.Any(id => GroupOf(id) == playerGroup);
            turnChanged = !pocketedSomethingScorable;
        }

        return new EightBallShotResult
        {
            Player = CurrentPlayer,
            PlayerGroupAtStart = playerGroup,
            Foul = foul,
            FoulReason = foulReason ?? string.Empty,
            PocketedBalls = pocketed,
            GroupJustAssigned = groupJustAssigned,
            AssignedToShootingPlayer = assigned,
            TurnChanged = turnChanged,
            BallInHand = foul,
            GameWon = gameWon,
            GameLost = gameLost,
            Description = description,
        };
    }

    private void Apply(EightBallShotResult r)
    {
        // Group assignment on open table.
        if (r.GroupJustAssigned && r.AssignedToShootingPlayer.HasValue)
        {
            OpenTable = false;
            if (CurrentPlayer == 1)
            {
                Player1Group = r.AssignedToShootingPlayer;
                Player2Group = r.AssignedToShootingPlayer == BallGroup.Solid ? BallGroup.Stripe : BallGroup.Solid;
            }
            else
            {
                Player2Group = r.AssignedToShootingPlayer;
                Player1Group = r.AssignedToShootingPlayer == BallGroup.Solid ? BallGroup.Stripe : BallGroup.Solid;
            }
        }

        if (r.GameWon)
        {
            GameOver = true;
            Winner = CurrentPlayer;
            WinReason = r.Description;
        }
        else if (r.GameLost)
        {
            GameOver = true;
            Winner = CurrentPlayer == 1 ? 2 : 1;
            WinReason = r.Description;
        }
        else if (r.TurnChanged)
        {
            CurrentPlayer = CurrentPlayer == 1 ? 2 : 1;
        }
    }

    private static string DescribeLegalShot(List<int> pocketed, BallGroup? playerGroup, bool openTable)
    {
        if (pocketed.Count == 0) return "dry shot, turn changes";
        var owned = playerGroup.HasValue
            ? pocketed.Where(id => GroupOf(id) == playerGroup.Value).ToList()
            : pocketed.Where(id => GroupOf(id) != BallGroup.Eight && id != CueBallId).ToList();
        if (owned.Count == 0) return "no own-group balls pocketed - turn changes";
        return openTable
            ? $"open-table pot: {string.Join(",", owned)} - shooter continues"
            : $"own-group pot: {string.Join(",", owned)} - shooter continues";
    }

    private static int CountUnpocketedInGroup(BallState[] state, BallGroup group)
    {
        int count = 0;
        for (int id = 0; id < state.Length; id++)
        {
            if (GroupOf(id) != group) continue;
            if (state[id].State == MotionState.Pocketed) continue;
            count++;
        }
        return count;
    }
}
