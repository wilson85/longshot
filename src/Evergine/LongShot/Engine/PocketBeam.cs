using System.Numerics;

namespace Longshot.Engine;

public readonly struct PocketBeam
{
    public readonly Vector3 P1;
    public readonly Vector3 P2;
    public readonly Vector3 PullDirection;
    public readonly Vector3 Normal;

    // --- NEW CAPSULE PROPERTIES ---
    public readonly float Radius;
    public readonly float Height;

    public PocketBeam(Vector3 p1, Vector3 p2, Vector3 pullDirection, float radius = 0.02f, float height = 0.1f)
    {
        P1 = p1;
        P2 = p2;
        PullDirection = Vector3.Normalize(pullDirection);

        // Normal faces OUT of the pocket, toward the table center
        Normal = -PullDirection;

        Radius = radius;
        Height = height;
    }
}