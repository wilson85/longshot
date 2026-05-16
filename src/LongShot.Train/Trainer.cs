using System;
using System.Diagnostics;
using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn.functional;

namespace LongShot.Train;

public static class Trainer
{
    /// <summary>
    /// Trains a fresh <see cref="PoolAimNet"/> on geometrically-labelled data with mini-batch
    /// SGD. Reports MSE plus mean angular error (the more interpretable metric for an
    /// aiming task - 1° of error loses the pocket window on a long shot).
    /// </summary>
    public static PoolAimNet Train(
        int trainSamples = 100_000,
        int testSamples = 5_000,
        int epochs = 500,
        int batchSize = 512,
        float lr = 1e-3f,
        int logEvery = 50,
        int trainSeed = 0,
        int testSeed = 999)
    {
        var (trainX, trainY) = DataGen.Generate(trainSamples, trainSeed);
        var (testX, testY) = DataGen.Generate(testSamples, testSeed);

        // Pin to CPU for now. Switch to CUDA by changing this device + the TorchSharp runtime package.
        var device = torch.CPU;

        var x = torch.tensor(trainX, new long[] { trainSamples, DataGen.FeatureDim }, device: device);
        var y = torch.tensor(trainY, new long[] { trainSamples, DataGen.LabelDim }, device: device);
        var xTest = torch.tensor(testX, new long[] { testSamples, DataGen.FeatureDim }, device: device);
        var yTest = torch.tensor(testY, new long[] { testSamples, DataGen.LabelDim }, device: device);

        var model = new PoolAimNet();
        model.to(device);

        var optimizer = torch.optim.Adam(model.parameters(), lr: lr);

        Console.WriteLine($"Training PoolAimNet: {trainSamples} train / {testSamples} test, {epochs} epochs, batch={batchSize}, lr={lr}");
        Console.WriteLine($"{"epoch",6} {"train_mse",12} {"test_mse",12} {"mean_ang_err_deg",18}");

        var shuffleRng = new Random(trainSeed);
        int numBatches = trainSamples / batchSize;

        var sw = Stopwatch.StartNew();
        for (int epoch = 0; epoch <= epochs; epoch++)
        {
            model.train();

            var indices = MakeShuffledIndices(trainSamples, shuffleRng);
            var idxTensor = torch.tensor(indices, new long[] { trainSamples }, device: device);

            float epochLoss = 0f;
            for (int b = 0; b < numBatches; b++)
            {
                var batchIdx = idxTensor.slice(0, b * batchSize, (b + 1) * batchSize, 1);
                var batchX = x.index_select(0, batchIdx);
                var batchY = y.index_select(0, batchIdx);

                optimizer.zero_grad();
                var pred = model.forward(batchX);
                var loss = mse_loss(pred, batchY);
                loss.backward();
                optimizer.step();

                epochLoss += loss.item<float>();
            }
            epochLoss /= numBatches;

            if (epoch % logEvery == 0 || epoch == epochs)
            {
                model.eval();
                using (no_grad())
                {
                    var testPred = model.forward(xTest);
                    var testMse = mse_loss(testPred, yTest).item<float>();
                    var meanAngErrDeg = MeanAngularErrorDegrees(testPred, yTest);
                    Console.WriteLine($"{epoch,6} {epochLoss,12:F6} {testMse,12:F6} {meanAngErrDeg,18:F3}");
                }
            }
        }
        sw.Stop();
        Console.WriteLine($"Trained in {sw.Elapsed.TotalSeconds:F1}s");

        return model;
    }

    private static long[] MakeShuffledIndices(int n, Random rng)
    {
        var arr = new long[n];
        for (int i = 0; i < n; i++) arr[i] = i;
        for (int i = n - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        return arr;
    }

    /// <summary>Mean absolute angular difference between predicted and target (sin, cos) tensors, in degrees.</summary>
    private static float MeanAngularErrorDegrees(Tensor pred, Tensor target)
    {
        // For each row, compute atan2(sin, cos) and take wrap-aware absolute difference.
        var p = pred.data<float>().ToArray();
        var t = target.data<float>().ToArray();
        int n = (int)target.size(0);

        double total = 0;
        for (int i = 0; i < n; i++)
        {
            float pAng = MathF.Atan2(p[(i * 2) + 0], p[(i * 2) + 1]);
            float tAng = MathF.Atan2(t[(i * 2) + 0], t[(i * 2) + 1]);
            float diff = MathF.Abs(pAng - tAng);
            if (diff > MathF.PI) diff = (2 * MathF.PI) - diff;
            total += diff;
        }
        return (float)(total / n * (180.0 / Math.PI));
    }
}
