using System;
using System.Collections.Generic;
using System.Diagnostics;
using LongShot.Bench.Scenarios;

namespace LongShot.Bench;

internal static class Program
{
    private static readonly Func<ScenarioResult>[] AllScenarios =
    {
        // --- Shot mechanics ---
        StopShot.Run,
        FollowShot.Run,
        DrawShot.Run,
        StunCut.Run,
        CurveShot.Run,
        BallBallThrow.Run,
        JumpShot.Run,
        Masse.Run,
        MultiBallChain.Run,
        FrozenBall.Run,
        // --- Cushion physics calibration (measurements vs real-world references) ---
        CushionPerpendicularBounce.Run,
        CushionAngleReflection.Run,
        LagStrokeTravel.Run,
        BreakStrokeTravel.Run,
        // --- Game-flow / pocketing ---
        EnglishOffRail.Run,
        CornerPocketClean.Run,
        CornerPocketJaw.Run,
        CombinationPot.Run,
        HardBreak.Run,
        // --- Architecture: shot recording / observer pattern ---
        ObserverDemo.Run,
        // --- 8-ball rules ---
        Rules8BallLegalPot.Run,
        Rules8BallScratch.Run,
        Rules8BallWrongGroup.Run,
        Rules8BallEightEarlyLoss.Run,
    };

    private static int Main(string[] args)
    {
        Console.WriteLine($"LongShot physics bench  ({AllScenarios.Length} scenarios)");
        Console.WriteLine($"Output dir: {Scenario.OutputDir}");
        Console.WriteLine(new string('=', 72));

        int failed = 0;
        var sw = Stopwatch.StartNew();

        foreach (var run in AllScenarios)
        {
            var scenarioSw = Stopwatch.StartNew();
            ScenarioResult result;
            try
            {
                result = run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n!! {run.Method.DeclaringType?.Name} threw {ex.GetType().Name}: {ex.Message}");
                failed++;
                continue;
            }
            scenarioSw.Stop();
            PrintResult(result, scenarioSw.ElapsedMilliseconds);
            if (!result.AllPassed) failed++;
        }

        sw.Stop();
        Console.WriteLine(new string('=', 72));
        Console.WriteLine($"{AllScenarios.Length - failed}/{AllScenarios.Length} scenarios passed in {sw.ElapsedMilliseconds} ms");
        return failed == 0 ? 0 : 1;
    }

    private static void PrintResult(ScenarioResult result, long elapsedMs)
    {
        var s = result.Scenario;
        string status = result.AllPassed ? "PASS" : "FAIL";
        Console.WriteLine();
        Console.WriteLine($"[{status}] {s.Name,-26} sim={s.ElapsedSimTime,6:0.000}s  wall={elapsedMs,4}ms  -> {result.ImagePath}");
        if (!string.IsNullOrWhiteSpace(s.Description))
        {
            Console.WriteLine($"       {s.Description}");
        }
        foreach (var (label, pass, detail) in s.Assertions)
        {
            string marker = pass ? "  ok " : "  X  ";
            Console.WriteLine($"  {marker}{label}");
            if (!string.IsNullOrEmpty(detail))
            {
                Console.WriteLine($"        {detail}");
            }
        }
    }
}
