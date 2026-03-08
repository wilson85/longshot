using System.Numerics;

namespace LongShot;

public sealed class CueController
{
    readonly ShotPowerManager _power = new();

    // Changed to a public property so we can read it for the UI later!
    public Vector2 TipOffset { get; private set; }

    public float CueOffset => _power.CueStickOffset;

    public void UpdateAim(InputState input)
    {
        // ONLY move the tip offset if the player is holding E
        if (input.Keys[(int)ConsoleKey.E])
        {
            float speed = 0.003f;
            var offset = TipOffset;

            offset.X += input.MouseDeltaX * speed;
            offset.Y -= input.MouseDeltaY * speed;

            TipOffset = Vector2.Clamp(offset, new Vector2(-0.9f), new Vector2(0.9f));
        }
    }

    public ShotResult UpdateStroke(InputState input, float dt)
        => _power.UpdateStroke(input, dt);

    public Shot BuildShot(Camera camera)
    {
        // Pass the TipOffset down to build the shot
        var shot = _power.BuildShot(camera, TipOffset);

        // CRITICAL FIX: Reset the spin back to dead-center after the strike!
        TipOffset = Vector2.Zero;

        return shot;
    }

    public void BeginStroke()
    {
        _power.BeginStroke();
    }
}
