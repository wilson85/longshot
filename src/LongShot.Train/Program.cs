using System;
using System.Diagnostics;

namespace LongShot.Train;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("LongShot.Train - supervised pocket-the-ball pipeline");
        Console.WriteLine("===================================================");
        Console.WriteLine();

        // 1. Geometric-oracle baseline. This tells us the ceiling the supervised network
        //    is trying to reach: how often does the textbook ghost-ball aim actually pocket
        //    the ball at our fixed force? Anything less than 100% is the engine's squirt
        //    and cushion physics rejecting the naive aim.
        Console.WriteLine("[1/3] Geometric oracle baseline...");
        var oracleSw = Stopwatch.StartNew();
        var oracle = Evaluator.EvaluateGeometricOracle(count: 500);
        oracleSw.Stop();
        Console.WriteLine($"      pocketed {oracle.Pocketed}/{oracle.Total} = {oracle.Rate:F1}%  ({oracleSw.ElapsedMilliseconds} ms)");
        Console.WriteLine();

        // 2. Train the network.
        Console.WriteLine("[2/3] Training PoolAimNet...");
        var model = Trainer.Train();
        Console.WriteLine();

        // 3. Same eval protocol with the network's predicted aim.
        Console.WriteLine("[3/3] Trained-model evaluation...");
        var modelSw = Stopwatch.StartNew();
        var modelResult = Evaluator.EvaluateModel(model, count: 500);
        modelSw.Stop();
        Console.WriteLine($"      pocketed {modelResult.Pocketed}/{modelResult.Total} = {modelResult.Rate:F1}%  ({modelSw.ElapsedMilliseconds} ms)");
        Console.WriteLine();

        Console.WriteLine($"Oracle:  {oracle.Rate:F1}%");
        Console.WriteLine($"Network: {modelResult.Rate:F1}%");
        Console.WriteLine($"Gap:     {oracle.Rate - modelResult.Rate:+0.0;-0.0;0.0} pp");
        return 0;
    }
}
