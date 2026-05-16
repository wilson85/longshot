using System;
using System.Numerics;
using LongShot.Engine;

namespace LongShot.Train;

/// <summary>
/// Generates supervised training data for the "pot the object ball into the top-right corner
/// pocket" task. Inputs are (cueX, cueZ, objX, objZ) world coordinates; labels are the
/// ghost-ball aim yaw encoded as (sin, cos) to avoid the wrap-around discontinuity at ±π.
/// </summary>
/// <remarks>
/// The geometric ghost-ball formula is an *oracle* - it gives the correct aim for a stun
/// shot with no squirt or cushion interaction. The network learns to reproduce this function,
/// which is a sanity check that the TorchSharp training loop works end-to-end. To get past
/// the geometric baseline, the labels would need to come from engine search (sweep aims,
/// pick the one that actually pockets) - that's the natural next step after this pipeline
/// is validated.
/// </remarks>
public static class DataGen
{
    public const float R = GameSettings.BallRadius;
    public const float HalfW = 0.55f;   // safe cue/object range in X (table half-width is 0.665)
    public const float HalfL = 1.00f;   // safe cue/object range in Z (table half-length is 1.165)
    public const float MinSeparation = 0.15f; // minimum cue-object distance (3R or so)
    public const float MinPocketStandoff = 0.20f; // object ball must be at least this far from the target pocket

    /// <summary>Approximate centre of the top-right corner pocket throat in world space.</summary>
    public static readonly Vector3 TargetPocket = new(0.624f, 0f, 1.124f);

    /// <summary>Number of feature dimensions per sample (cueX, cueZ, objX, objZ).</summary>
    public const int FeatureDim = 4;

    /// <summary>Number of label dimensions per sample (sin_yaw, cos_yaw).</summary>
    public const int LabelDim = 2;

    /// <summary>
    /// Generates <paramref name="count"/> samples. Rejection-sampled to avoid degenerate
    /// configurations (balls overlapping, object stuck in the target pocket, etc.).
    /// </summary>
    public static (float[] Features, float[] Labels) Generate(int count, int seed)
    {
        var rng = new Random(seed);
        var features = new float[count * FeatureDim];
        var labels = new float[count * LabelDim];

        for (int i = 0; i < count; i++)
        {
            Vector3 cue, obj;
            int attempts = 0;
            do
            {
                cue = RandomPosition(rng);
                obj = RandomPosition(rng);
                attempts++;
                if (attempts > 100) throw new InvalidOperationException("Rejection sampling failed.");
            } while (Vector3.Distance(cue, obj) < MinSeparation
                  || Vector3.Distance(obj, TargetPocket) < MinPocketStandoff);

            // Ghost-ball position = behind the object on the line to the pocket.
            Vector3 objToPocket = Vector3.Normalize(TargetPocket - obj);
            Vector3 ghost = obj - (objToPocket * 2 * R);
            Vector3 aimDir = Vector3.Normalize(ghost - cue);
            float yaw = MathF.Atan2(aimDir.X, aimDir.Z);

            features[(i * FeatureDim) + 0] = cue.X;
            features[(i * FeatureDim) + 1] = cue.Z;
            features[(i * FeatureDim) + 2] = obj.X;
            features[(i * FeatureDim) + 3] = obj.Z;

            labels[(i * LabelDim) + 0] = MathF.Sin(yaw);
            labels[(i * LabelDim) + 1] = MathF.Cos(yaw);
        }

        return (features, labels);
    }

    private static Vector3 RandomPosition(Random rng)
    {
        float x = (float)((rng.NextDouble() * 2.0) - 1.0) * HalfW;
        float z = (float)((rng.NextDouble() * 2.0) - 1.0) * HalfL;
        return new Vector3(x, R, z);
    }

    /// <summary>
    /// Reconstructs the world-space position from a feature row (inverse of Generate's
    /// flattening). Useful for evaluators that need to drive the engine from a sample.
    /// </summary>
    public static (Vector3 Cue, Vector3 Object) UnpackSample(ReadOnlySpan<float> features, int sampleIndex)
    {
        int off = sampleIndex * FeatureDim;
        return (
            new Vector3(features[off + 0], R, features[off + 1]),
            new Vector3(features[off + 2], R, features[off + 3]));
    }
}
