using System;
using System.Numerics;
using LongShot.Engine;
using TorchSharp;
using static TorchSharp.torch;

namespace LongShot.Train;

/// <summary>
/// Drives the bench's billiards engine to measure how often a predicted aim actually
/// pockets the object ball. The geometric oracle gives an upper bound; a fully-trained
/// network should match it almost exactly on the supervised task.
/// </summary>
public static class Evaluator
{
    public const float StrikeForce = 0.7f;
    public const int MaxTicksPerShot = 2000;

    public sealed class Result
    {
        public int Pocketed;
        public int Total;
        public float Rate => Total == 0 ? 0f : 100f * Pocketed / Total;
    }

    /// <summary>Run the engine with the predicted aim for every sample and count pocketings.</summary>
    public static Result EvaluateModel(PoolAimNet model, int count = 500, int seed = 12345)
    {
        var (features, _) = DataGen.Generate(count, seed);
        var x = torch.tensor(features, new long[] { count, DataGen.FeatureDim });

        model.eval();
        float[] predictions;
        using (no_grad())
        {
            var pred = model.forward(x);
            predictions = pred.data<float>().ToArray();
        }

        return RunAllShots(features, predictions, count);
    }

    /// <summary>Same protocol, but uses the ground-truth ghost-ball labels directly.</summary>
    public static Result EvaluateGeometricOracle(int count = 500, int seed = 12345)
    {
        var (features, labels) = DataGen.Generate(count, seed);
        return RunAllShots(features, labels, count);
    }

    private static Result RunAllShots(float[] features, float[] aims, int count)
    {
        var engine = BuildEngine();
        bool objectPocketed = false;
        engine.OnBallPocketed += (id, _) => { if (id == 1) objectPocketed = true; };

        int pocketed = 0;
        for (int i = 0; i < count; i++)
        {
            var (cuePos, objPos) = DataGen.UnpackSample(features, i);

            // (sin_yaw, cos_yaw) - normalise back to a unit direction (the network's output
            // doesn't strictly lie on the unit circle).
            float sinY = aims[(i * DataGen.LabelDim) + 0];
            float cosY = aims[(i * DataGen.LabelDim) + 1];
            float mag = MathF.Sqrt((sinY * sinY) + (cosY * cosY));
            if (mag < 1e-6f) mag = 1f;
            sinY /= mag;
            cosY /= mag;

            engine.RespawnBall(0, cuePos);
            engine.RespawnBall(1, objPos);
            objectPocketed = false;

            engine.StrikeCueBall(0, new Vector3(sinY, 0, cosY), StrikeForce, Vector3.Zero);

            int ticks = 0;
            while (!engine.AreAllBallsAsleep() && ticks++ < MaxTicksPerShot)
            {
                engine.Tick(GameSettings.FixedStep);
            }

            if (objectPocketed) pocketed++;
        }

        return new Result { Pocketed = pocketed, Total = count };
    }

    private static BilliardsEngine BuildEngine()
    {
        var engine = new BilliardsEngine();
        var (rails, pockets) = TableBuilder.Build(TableDefinition.BuildWpaStandard());
        engine.InitializeMatch(rails, pockets);
        engine.AddBall(Vector3.Zero, BallType.Cue);
        engine.AddBall(Vector3.Zero, BallType.Normal);
        return engine;
    }
}
