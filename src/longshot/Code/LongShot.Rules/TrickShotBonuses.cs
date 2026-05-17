using System.Collections.Generic;
using LongShot.Shot;

namespace LongShot.Rules;

/// <summary>
/// Awards bonus "wow" points on top of base scoring for impressive shots. Demonstrates
/// the power-up / fantasy-effect extension pattern: every multiplier or special-event
/// reward is just another <see cref="IShotObserver"/> reading the same
/// <see cref="ShotSummary"/>. Stack as many as you like alongside <see cref="SimpleScoring"/>.
/// </summary>
/// <remarks>
/// This is meant to be illustrative, not the final word on bonus design. The point is:
/// adding a new "fantasy" bonus is a new file, a new <c>IShotObserver</c>, and a
/// registration. No engine changes, no rule-system changes, no coordination with other
/// observers needed.
/// </remarks>
public sealed class TrickShotBonuses : IShotObserver
{
    /// <summary>Awarded for any pot where the object ball touched a rail on its way to the pocket.</summary>
    public int RailKillBonusPoints { get; init; } = 2;

    /// <summary>Awarded if a successful pot was performed via a jump shot.</summary>
    public int JumpPotBonusPoints { get; init; } = 5;

    /// <summary>Awarded for a successful massé pot.</summary>
    public int MassePotBonusPoints { get; init; } = 10;

    /// <summary>Awarded for combination pots (cue → object A → object B → pocket).</summary>
    public int CombinationBonusPoints { get; init; } = 3;

    public int BonusScore { get; private set; }
    public List<string> Log { get; } = new();

    public void OnShotEnd(ShotSummary summary)
    {
        // Only award bonuses on legal pots (object ball pocketed, no scratch).
        bool legalPotHappened = !summary.CueBallPocketed && summary.Pocketings.Count > 0;
        if (!legalPotHappened) return;

        int delta = 0;
        var notes = new List<string>();

        // Rail kill: any object ball was pocketed AND the cue or that ball hit a rail this shot.
        // (Cheap heuristic - doesn't track WHICH ball pocketed bounced off WHICH rail. Good enough
        // for the demo; a richer implementation could trace per-ball trajectories.)
        if (summary.RailContacts.Count > 0)
        {
            delta += RailKillBonusPoints;
            notes.Add($"rail-kill (+{RailKillBonusPoints})");
        }

        // Jump-pot: cue went airborne and something got pocketed.
        if (summary.WasJumpShot)
        {
            delta += JumpPotBonusPoints;
            notes.Add($"jump-pot (+{JumpPotBonusPoints})");
        }

        // Massé-pot: massé heuristic fired and something pocketed.
        if (summary.WasMasse)
        {
            delta += MassePotBonusPoints;
            notes.Add($"massé-pot (+{MassePotBonusPoints})");
        }

        // Combination: cue first contacted ball A, that ball contacted ball B, that ball pocketed.
        // The summary's ball-contact timeline tells us the chain.
        if (IsCombinationPot(summary))
        {
            delta += CombinationBonusPoints;
            notes.Add($"combo (+{CombinationBonusPoints})");
        }

        if (delta != 0)
        {
            BonusScore += delta;
            Log.Add($"bonus: {string.Join(", ", notes)}  → total bonus {BonusScore}");
        }
    }

    /// <summary>
    /// Detects "cue → A → B → pocket" pattern: cue's first contact ball isn't pocketed, but
    /// a different ball that A subsequently contacted was pocketed.
    /// </summary>
    private static bool IsCombinationPot(ShotSummary summary)
    {
        int cueId = summary.Strike?.CueBallId ?? 0;
        if (summary.BallContacts.Count < 2) return false;

        int firstHit = summary.FirstContactBallId;
        if (firstHit < 0) return false;

        // Was first-hit ball NOT pocketed?
        foreach (var p in summary.Pocketings)
            if (p.BallId == firstHit) return false;

        // Did first-hit subsequently contact ANOTHER ball, and that other ball got pocketed?
        foreach (var c in summary.BallContacts)
        {
            if (c.BallA == cueId || c.BallB == cueId) continue; // skip cue contact
            int other = c.BallA == firstHit ? c.BallB : (c.BallB == firstHit ? c.BallA : -1);
            if (other < 0) continue;
            foreach (var p in summary.Pocketings)
                if (p.BallId == other) return true;
        }
        return false;
    }
}
