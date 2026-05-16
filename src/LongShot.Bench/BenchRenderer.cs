using System;
using System.IO;
using System.Numerics;
using LongShot.Engine;
using SkiaSharp;

namespace LongShot.Bench;

/// <summary>
/// Top-down PNG renderer for a finished <see cref="Scenario"/>. Draws the table outline,
/// every ball trajectory coloured by motion state, start/end markers, and an assertion
/// summary so the result is inspectable without launching the game.
/// </summary>
public static class BenchRenderer
{
    private const float PixelsPerMeter = 320f;
    private const float Padding = 0.18f; // meters

    private static readonly SKColor FeltColor = new(0x10, 0x2a, 0x18);
    private static readonly SKColor TableEdgeColor = new(0x32, 0x6e, 0x46);
    private static readonly SKColor RailColor = new(0x80, 0xb0, 0x90);
    private static readonly SKColor PocketColor = new(0x05, 0x05, 0x05);
    private static readonly SKColor CueBallColor = SKColors.White;
    private static readonly SKColor ObjectBallColor = new(0xff, 0xc8, 0x40);
    private static readonly SKColor PassColor = new(0x4f, 0xff, 0x66);
    private static readonly SKColor FailColor = new(0xff, 0x5d, 0x5d);
    private static readonly SKColor TextColor = new(0xe6, 0xe6, 0xe6);

    private static readonly SKColor SlidingColor = new(0xff, 0x5d, 0x5d);
    private static readonly SKColor RollingColor = new(0x4f, 0xff, 0x66);
    private static readonly SKColor AirborneColor = new(0x4f, 0xc5, 0xff);

    public static void Render(Scenario s, string outputPath)
    {
        float tw = GameSettings.TableWidth + (GameSettings.RailWidth * 2) + (Padding * 2);
        float tl = GameSettings.TableLength + (GameSettings.RailWidth * 2) + (Padding * 2);

        int canvasW = (int)MathF.Ceiling(tw * PixelsPerMeter);
        int canvasH = (int)MathF.Ceiling(tl * PixelsPerMeter) + 220; // header strip for assertions

        const int HeaderHeight = 220;

        using var bitmap = new SKBitmap(canvasW, canvasH);
        using var canvas = new SKCanvas(bitmap);

        // Background
        canvas.Clear(new SKColor(0x0a, 0x0d, 0x14));

        // Header text
        DrawHeader(s, canvas, canvasW);

        canvas.Save();
        canvas.Translate(canvasW / 2f, HeaderHeight + (canvasH - HeaderHeight) / 2f);

        DrawTable(canvas, s.Engine.TableLayout);
        DrawTrajectories(s, canvas);
        DrawBallMarkers(s, canvas);

        canvas.Restore();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);
    }

    private static void DrawHeader(Scenario s, SKCanvas canvas, int canvasW)
    {
        using var titleFont = new SKFont(SKTypeface.Default, 18);
        using var descFont = new SKFont(SKTypeface.Default, 12);
        using var assertFont = new SKFont(SKTypeface.Default, 12);
        using var paint = new SKPaint { Color = TextColor, IsAntialias = true };

        canvas.DrawText(s.Name, 14, 26, SKTextAlign.Left, titleFont, paint);

        if (!string.IsNullOrWhiteSpace(s.Description))
        {
            using var descPaint = new SKPaint { Color = new SKColor(0xa0, 0xa6, 0xb0), IsAntialias = true };
            canvas.DrawText(s.Description, 14, 46, SKTextAlign.Left, descFont, descPaint);
        }

        float y = 68;
        foreach (var (label, pass, detail) in s.Assertions)
        {
            using var statusPaint = new SKPaint { Color = pass ? PassColor : FailColor, IsAntialias = true };
            string marker = pass ? "PASS" : "FAIL";
            canvas.DrawText(marker, 14, y, SKTextAlign.Left, assertFont, statusPaint);
            canvas.DrawText(label, 70, y, SKTextAlign.Left, assertFont, paint);
            if (!string.IsNullOrEmpty(detail))
            {
                using var dimPaint = new SKPaint { Color = new SKColor(0x80, 0x88, 0x90), IsAntialias = true };
                canvas.DrawText(detail, 70, y + 13, SKTextAlign.Left, assertFont, dimPaint);
                y += 14;
            }
            y += 16;
        }

        // Legend along the top-right
        using var legendFont = new SKFont(SKTypeface.Default, 11);
        DrawLegendChip(canvas, canvasW - 230, 18, SlidingColor, "Sliding", legendFont);
        DrawLegendChip(canvas, canvasW - 150, 18, RollingColor, "Rolling", legendFont);
        DrawLegendChip(canvas, canvasW - 75, 18, AirborneColor, "Airborne", legendFont);

        using var timePaint = new SKPaint { Color = new SKColor(0xa0, 0xa6, 0xb0), IsAntialias = true };
        canvas.DrawText($"sim time: {s.ElapsedSimTime:0.000}s   pocketed: {s.Pocketings.Count}", canvasW - 14, 42, SKTextAlign.Right, descFont, timePaint);
    }

    private static void DrawLegendChip(SKCanvas canvas, float x, float y, SKColor color, string label, SKFont font)
    {
        using var swatchPaint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x, y - 8, 12, 4, swatchPaint);
        using var textPaint = new SKPaint { Color = TextColor, IsAntialias = true };
        canvas.DrawText(label, x + 16, y, SKTextAlign.Left, font, textPaint);
    }

    private static void DrawTable(SKCanvas canvas, TableLayout layout)
    {
        // Felt
        float fw = GameSettings.TableWidth * PixelsPerMeter;
        float fl = GameSettings.TableLength * PixelsPerMeter;
        using var feltPaint = new SKPaint { Color = FeltColor, IsAntialias = true };
        canvas.DrawRect(-fw / 2f, -fl / 2f, fw, fl, feltPaint);

        using var feltEdge = new SKPaint { Color = TableEdgeColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
        canvas.DrawRect(-fw / 2f, -fl / 2f, fw, fl, feltEdge);

        // Rail cushions
        using var railPaint = new SKPaint { Color = RailColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, StrokeCap = SKStrokeCap.Round };
        foreach (var rail in layout.Rails)
        {
            var a = ToCanvas(rail.Start);
            var b = ToCanvas(rail.End);
            canvas.DrawLine(a, b, railPaint);
        }

        // Pockets - drawn at the rail's playfield-facing MOUTH (not the trigger plane). The
        // engine's PocketBeam capsule sits one commitment-depth INSIDE the pocket, so we walk
        // back out along PullDirection to land on the mouth where a real pocket cutout is.
        // (TableBuilder.BuildOptions.Default puts commitment depth at 2 * BallRadius.)
        using var pocketPaint = new SKPaint { Color = PocketColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var pocketRim = new SKPaint { Color = new SKColor(0x40, 0x40, 0x40), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
        float pocketRadiusPx = GameSettings.BallRadius * 1.6f * PixelsPerMeter;
        const float MouthOffset = GameSettings.BallRadius * 2f;
        foreach (var pocket in layout.Pockets)
        {
            var capsuleCenter = (pocket.P1 + pocket.P2) / 2f;
            var mouthCenter = capsuleCenter - (pocket.PullDirection * MouthOffset);
            var c = ToCanvas(mouthCenter);
            canvas.DrawCircle(c, pocketRadiusPx, pocketPaint);
            canvas.DrawCircle(c, pocketRadiusPx, pocketRim);
        }
    }

    private static void DrawTrajectories(Scenario s, SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.0f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        foreach (var traj in s.Trajectories)
        {
            for (int i = 1; i < traj.Samples.Count; i++)
            {
                var prev = traj.Samples[i - 1];
                var curr = traj.Samples[i];
                paint.Color = ColorForState(prev.State);
                canvas.DrawLine(ToCanvas(prev.Position), ToCanvas(curr.Position), paint);
            }
        }
    }

    /// <summary>Period (in sample frames) between stroboscopic ghost-ball markers along the trail.</summary>
    private const int GhostMarkerInterval = 25;

    private static void DrawBallMarkers(Scenario s, SKCanvas canvas)
    {
        float ballPx = GameSettings.BallRadius * PixelsPerMeter;
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var ringPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        using var ghostPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.0f };
        using var darkRim = new SKPaint { Color = new SKColor(0x20, 0x20, 0x20, 0xc0), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };

        foreach (var traj in s.Trajectories)
        {
            SKColor color = traj.Type == BallType.Cue ? CueBallColor : ObjectBallColor;
            fillPaint.Color = color;
            ringPaint.Color = color.WithAlpha(200);
            ghostPaint.Color = color.WithAlpha(95);

            // Stroboscopic ghost markers along the trail - one per GhostMarkerInterval samples
            // (about every 200ms at the fixed 125 Hz tick rate). Faster motion = wider spacing.
            for (int i = GhostMarkerInterval; i < traj.Samples.Count - 1; i += GhostMarkerInterval)
            {
                var p = ToCanvas(traj.Samples[i].Position);
                canvas.DrawCircle(p, ballPx, ghostPaint);
            }

            // Start: solid filled disc
            var start = ToCanvas(traj.InitialPosition);
            canvas.DrawCircle(start, ballPx, fillPaint);
            canvas.DrawCircle(start, ballPx, darkRim);

            // End: hollow ring (only if the ball actually moved)
            if (Vector3.DistanceSquared(traj.InitialPosition, traj.FinalPosition) > 1e-4f)
            {
                var end = ToCanvas(traj.FinalPosition);
                canvas.DrawCircle(end, ballPx, ringPaint);
            }
        }
    }

    private static SKColor ColorForState(MotionState state) => state switch
    {
        MotionState.Sliding => SlidingColor,
        MotionState.Rolling => RollingColor,
        MotionState.Airborne => AirborneColor,
        _ => new SKColor(0x60, 0x60, 0x60),
    };

    private static SKPoint ToCanvas(Vector3 worldPos) =>
        new(worldPos.X * PixelsPerMeter, -worldPos.Z * PixelsPerMeter);
    private static SKPoint ToCanvas(Vector2 worldXZ) =>
        new(worldXZ.X * PixelsPerMeter, -worldXZ.Y * PixelsPerMeter);
}
