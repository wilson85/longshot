using System.Collections.Generic;
using LongShot.Shot;

namespace LongShot.Rules;

/// <summary>
/// Minimum-viable scorer: +1 per object ball pocketed, -1 if the cue ball was pocketed
/// (a scratch). Demonstrates the <see cref="IShotObserver"/> shape without committing to
/// any particular game variant. Real 8-ball / 9-ball implementations layer on top of the
/// same hook.
/// </summary>
public sealed class SimpleScoring : IShotObserver
{
    public int Score { get; private set; }
    public int ShotCount { get; private set; }
    public List<string> Log { get; } = new();

    public void OnShotStart(ShotContext context)
    {
        ShotCount++;
    }

    public void OnShotEnd(ShotSummary summary)
    {
        int delta = 0;
        var notes = new List<string>();

        foreach (var p in summary.Pocketings)
        {
            if (p.BallId == summary.Strike?.CueBallId)
            {
                delta -= 1;
                notes.Add("scratch (-1)");
            }
            else
            {
                delta += 1;
                notes.Add($"potted ball {p.BallId} (+1)");
            }
        }

        if (delta == 0) notes.Add("dry shot (0)");

        Score += delta;
        Log.Add($"shot {ShotCount}: {string.Join(", ", notes)}  → total {Score}");
    }
}
