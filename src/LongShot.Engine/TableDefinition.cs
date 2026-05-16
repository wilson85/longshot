using System.Collections.Generic;
using System.Numerics;

namespace LongShot.Engine;

/// <summary>
/// Pure-data description of a table layout. Independent of any visual representation.
/// Pass to <see cref="TableBuilder"/> to produce the physics primitives (cushion segments,
/// pocket beams) that the engine consumes.
/// </summary>
public sealed class TableDefinition
{
    public float Width { get; init; }
    public float Length { get; init; }
    public List<RailData> Rails { get; init; } = new();
    public List<PocketData> Pockets { get; init; } = new();

    public static TableDefinition BuildWpaStandard()
    {
        var table = new TableDefinition
        {
            Width = GameSettings.TableWidth,
            Length = GameSettings.TableLength,
        };

        float w2 = GameSettings.TableWidth / 2f;
        float l2 = GameSettings.TableLength / 2f;

        float cc = GameSettings.RailCornerMouthWidth / 1.414f;
        float sc = GameSettings.RailSideMouthWidth / 2f;

        float cornerCut = cc * 0.5f;
        float sideCut = sc * 0.5f;
        float hw = GameSettings.RailWidth / 2f;

        // Pockets (corners and sides)
        table.Pockets.Add(new PocketData("Gate_BL", new Vector3(-w2, 0, -l2 + cc), new Vector3(-w2 + cc, 0, -l2), new Vector3(-1, 0, -1)));
        table.Pockets.Add(new PocketData("Gate_BR", new Vector3(w2 - cc, 0, -l2), new Vector3(w2, 0, -l2 + cc), new Vector3(1, 0, -1)));
        table.Pockets.Add(new PocketData("Gate_TL", new Vector3(-w2 + cc, 0, l2), new Vector3(-w2, 0, l2 - cc), new Vector3(-1, 0, 1)));
        table.Pockets.Add(new PocketData("Gate_TR", new Vector3(w2, 0, l2 - cc), new Vector3(w2 - cc, 0, l2), new Vector3(1, 0, 1)));
        table.Pockets.Add(new PocketData("Gate_MidL", new Vector3(-w2, 0, sc), new Vector3(-w2, 0, -sc), new Vector3(-1, 0, 0)));
        table.Pockets.Add(new PocketData("Gate_MidR", new Vector3(w2, 0, -sc), new Vector3(w2, 0, sc), new Vector3(1, 0, 0)));

        // Rails
        table.Rails.Add(new RailData("Bottom",
            new Vector2(-w2 + cc, -l2 - hw), new Vector2(w2 - cc, -l2 - hw),
            new JawSpec { Cutout = cornerCut, AngleDeg = GameSettings.RailCornerSweep },
            new JawSpec { Cutout = cornerCut, AngleDeg = GameSettings.RailCornerSweep }));

        table.Rails.Add(new RailData("Top",
            new Vector2(w2 - cc, l2 + hw), new Vector2(-w2 + cc, l2 + hw),
            new JawSpec { Cutout = cornerCut, AngleDeg = GameSettings.RailCornerSweep },
            new JawSpec { Cutout = cornerCut, AngleDeg = GameSettings.RailCornerSweep }));

        table.Rails.Add(new RailData("Right_Bottom",
            new Vector2(w2 + hw, -l2 + cc), new Vector2(w2 + hw, -sc),
            new JawSpec { Cutout = cornerCut, AngleDeg = GameSettings.RailCornerSweep },
            new JawSpec { Cutout = sideCut, AngleDeg = GameSettings.RailSideSweep }));

        table.Rails.Add(new RailData("Right_Top",
            new Vector2(w2 + hw, sc), new Vector2(w2 + hw, l2 - cc),
            new JawSpec { Cutout = sideCut, AngleDeg = GameSettings.RailSideSweep },
            new JawSpec { Cutout = cornerCut, AngleDeg = GameSettings.RailCornerSweep }));

        table.Rails.Add(new RailData("Left_Bottom",
            new Vector2(-w2 - hw, -sc), new Vector2(-w2 - hw, -l2 + cc),
            new JawSpec { Cutout = sideCut, AngleDeg = GameSettings.RailSideSweep },
            new JawSpec { Cutout = cornerCut, AngleDeg = GameSettings.RailCornerSweep }));

        table.Rails.Add(new RailData("Left_Top",
            new Vector2(-w2 - hw, l2 - cc), new Vector2(-w2 - hw, sc),
            new JawSpec { Cutout = cornerCut, AngleDeg = GameSettings.RailCornerSweep },
            new JawSpec { Cutout = sideCut, AngleDeg = GameSettings.RailSideSweep }));

        return table;
    }
}

public sealed class RailData
{
    public string Name { get; init; }
    public Vector2 Start { get; init; }
    public Vector2 End { get; init; }
    public JawSpec StartJaw { get; init; }
    public JawSpec EndJaw { get; init; }

    public RailData(string name, Vector2 start, Vector2 end, JawSpec startJaw, JawSpec endJaw)
    {
        Name = name;
        Start = start;
        End = end;
        StartJaw = startJaw;
        EndJaw = endJaw;
    }
}

public sealed class PocketData
{
    public string Name { get; init; }
    public Vector3 P1 { get; init; }
    public Vector3 P2 { get; init; }
    public Vector3 PullDir { get; init; }

    public PocketData(string name, Vector3 p1, Vector3 p2, Vector3 pullDir)
    {
        Name = name;
        P1 = p1;
        P2 = p2;
        PullDir = pullDir;
    }
}

public struct JawSpec
{
    public float Cutout;   // inset along the rail direction
    public float AngleDeg; // angle relative to rail normal
}

public struct LineSegment
{
    public Vector2 Start;
    public Vector2 End;
    public Vector2 Normal;
}
