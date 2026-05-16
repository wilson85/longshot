namespace LongShot.Rules;

/// <summary>
/// 8-ball group assignment for a physical ball id.
/// Convention used here: engine id 0 = cue, 1-7 = solids, 8 = the eight, 9-15 = stripes.
/// </summary>
public enum BallGroup
{
    Cue,
    Solid,
    Eight,
    Stripe,
}
