using System;
using LongShot.Engine;
using SnVec3 = System.Numerics.Vector3;

namespace Longshot;

/// <summary>
/// First-light s&amp;box host for <see cref="BilliardsEngine"/>. Single-player, single shot,
/// no rules — the absolute minimum that proves the integration model:
/// <list type="number">
///   <item>Project reference to LongShot.Engine resolves.</item>
///   <item>Coordinate/type seam in <see cref="Conversions"/> works.</item>
///   <item>Component lifecycle drives the engine's tick.</item>
///   <item>Mirroring engine state to GameObject transforms produces a visible result.</item>
/// </list>
///
/// Once these four work, layer in rules (see <c>LongShot.Rules.EightBallRules</c>), a proper
/// cue stick, mouse-driven aim, and rack setup. Until then this is intentionally barebones.
/// </summary>
public sealed class MatchHost : Component
{


    /// <summary>Prefab spawned once per ball. A simple sphere model works for first light.</summary>
    [Property] public GameObject BallPrefab { get; set; }

    /// <summary>Cue impulse magnitude (N·s). Force / Ball.Mass = initial cue speed; 0.5 ≈ medium shot.</summary>
    [Property] public float StrikeForce { get; set; } = 0.5f;

    /// <summary>Optional: if true, the cue ball is automatically struck once on start.</summary>
    [Property] public bool AutoStrikeOnStart { get; set; } = false;

    // -------------------- Table visualisation --------------------

    /// <summary>
    /// If true, the slate / rails / pockets are spawned procedurally from <see cref="TableBuilder"/>'s output
    /// on <see cref="OnStart"/>. Disable if your scene already has a hand-authored table.
    /// </summary>
    [Property] public bool SpawnTableVisuals { get; set; } = true;

    /// <summary>
    /// Box-shaped model used for slate / rails / pockets when no specific model is assigned. Defaults to
    /// <c>models/vidya/cube_white_64.vmdl</c> (mounted via <c>vidya.model-cube64</c>) so visuals work
    /// out of the box. Swap for a nicer mesh once the geometry is verified.
    /// </summary>
    [Property] public Model BoxModel { get; set; }

    /// <summary>
    /// Sphere model used for procedurally-spawned balls when <see cref="BallPrefab"/> is null.
    /// Defaults to <c>models/vidya/sphere_white_64.vmdl</c> (mounted via <c>vidya.model-sphere64</c>).
    /// </summary>
    [Property] public Model SphereModel { get; set; }

    /// <summary>
    /// Native size of the box mesh assigned to <see cref="BoxModel"/>, in s&amp;box units. The default
    /// fallback <c>models/dev/box.vmdl</c> is 50 units cubed. Every slate/rail/pocket <c>LocalScale</c>
    /// is divided by this so the rendered size matches the requested dimensions in units. Set to 1 if
    /// you assign a 1×1×1 cube.
    /// </summary>
    [Property] public float BoxModelNativeSize { get; set; } = 50f;

    /// <summary>
    /// Native size of the sphere mesh assigned to <see cref="SphereModel"/>, in s&amp;box units. The
    /// default fallback <c>models/dev/sphere.vmdl</c> is 50 units across. Used to scale procedurally-
    /// spawned balls when no <see cref="BallPrefab"/> is assigned.
    /// </summary>
    [Property] public float SphereModelNativeSize { get; set; } = 50f;

    /// <summary>Tint applied to the slate ModelRenderer. Default: classic billiard green.</summary>
    [Property] public Color SlateColor { get; set; } = new Color(0.04f, 0.40f, 0.27f);

    /// <summary>Tint applied to rail ModelRenderers. Default: dark walnut.</summary>
    [Property] public Color RailColor { get; set; } = new Color(0.32f, 0.20f, 0.12f);

    /// <summary>Tint applied to pocket ModelRenderers. Default: solid black so they read as holes.</summary>
    [Property] public Color PocketColor { get; set; } = Color.Black;

    /// <summary>Visual rail width (perpendicular to playfield), in s&amp;box units. Doesn't affect physics.</summary>
    [Property] public float RailThicknessUnits { get; set; } = 1.5f;

    /// <summary>Visual rail height (vertical), in s&amp;box units. Doesn't affect physics.</summary>
    [Property] public float RailHeightUnits { get; set; } = 2.5f;

    /// <summary>Visual slate thickness, in s&amp;box units. The top face sits at world Z = 0.</summary>
    [Property] public float SlateThicknessUnits { get; set; } = 1f;

    private BilliardsEngine _engine;
    private readonly List<GameObject> _ballObjects = new();
    private readonly List<GameObject> _tableObjects = new();

    protected override void OnStart()
    {
        _engine = new BilliardsEngine(seed: 1);

        var def = TableDefinition.BuildWpaStandard();
        var (rails, pockets) = TableBuilder.Build(def);
        _engine.InitializeMatch(rails, pockets);

        if (SpawnTableVisuals)
        {
            BuildTableVisuals(def, rails, pockets);
        }

        // First-light minimum: cue ball + one object ball directly ahead.
        // Engine coordinates: Y-up, metres. Ball centre rests at Y = BallRadius (slate at Y=0).
        _engine.AddBall(new SnVec3(0, GameSettings.BallRadius, -0.8f), BallType.Cue);
        _engine.AddBall(new SnVec3(0, GameSettings.BallRadius, 0.8f), BallType.Normal);

        float diameterUnits = Conversions.MetresToUnits(GameSettings.BallRadius * 2f);
        var sphereFallback = SphereModel ?? Model.Load("models/dev/sphere.vmdl");

        for (int i = 0; i < _engine.ActiveBallCount; i++)
        {
            GameObject go;
            if (BallPrefab is not null)
            {
                // Authored prefab path: clone, assume a unit-sized sphere mesh.
                go = BallPrefab.Clone();
                go.Name = $"Ball_{i}";
                go.LocalScale = new Vector3(diameterUnits, diameterUnits, diameterUnits);
            }
            else if (sphereFallback is not null)
            {
                // Procedural fallback: spawn a ModelRenderer'd sphere. The mounted sphere is 64u
                // native, so we scale to match the ball diameter.
                go = Scene.CreateObject(true);
                go.Name = $"Ball_{i}";
                float ballScale = diameterUnits / MathF.Max(0.0001f, SphereModelNativeSize);
                go.LocalScale = new Vector3(ballScale, ballScale, ballScale);
                var mr = go.AddComponent<ModelRenderer>();
                mr.Model = sphereFallback;
                mr.Tint  = Color.White;
            }
            else
            {
                Log.Warning($"{nameof(MatchHost)}: BallPrefab is null AND fallback sphere model ('models/vidya/sphere_white_64.vmdl') could not be loaded. Ball #{i} will not be visible.");
                continue;
            }
            _ballObjects.Add(go);
        }

        if (AutoStrikeOnStart)
        {
            Strike();
        }
    }

    /// <summary>Drive the deterministic physics on the fixed tick.</summary>
    protected override void OnFixedUpdate()
    {
        if (_engine is null) return;
        _engine.Tick(Time.Delta);
    }

    /// <summary>Mirror engine state to GameObject transforms every frame for smooth visuals.</summary>
    protected override void OnUpdate()
    {
        if (_engine is null) return;

        // Strike on left-mouse press for first-light testing. Replace with a proper
        // cue stick + aim + power UI once the integration is verified.
        if (Input.Pressed("Attack1"))
        {
            Strike();
        }

        var balls = _engine.PhysicsStates;
        for (int i = 0; i < balls.Length && i < _ballObjects.Count; i++)
        {
            var go = _ballObjects[i];
            if (!go.IsValid()) continue;

            if (balls[i].State == MotionState.Pocketed)
            {
                go.Enabled = false;
                continue;
            }

            go.WorldPosition = Conversions.EngineToWorld(balls[i].Position);
        }
    }

    /// <summary>
    /// Procedurally spawns slate / rails / pockets from the engine's collision primitives. Box meshes only
    /// (so first-light visuals match the physics 1:1); replace with authored models once the layout is verified.
    ///
    /// All spawned GameObjects are parented under <c>this.GameObject</c> so they move with the host and so
    /// scene-inspection tools can see them as a tidy group.
    /// </summary>
    private void BuildTableVisuals(TableDefinition def, CushionSegment[] rails, PocketBeam[] pockets)
    {
        // Use the built-in core dev box. Path 'models/dev/box.vmdl' lives in <sbox-install>/core/models/dev/
        // and renders correctly at runtime. Mounted CLOUD assets (e.g. vidya.model-cube64) do NOT render
        // when assigned at runtime via Model.Load or Cloud.Model — they only work when assigned in edit
        // mode (as a serialized scene reference). The core primitives ship with s&box and always work.
        var boxModel = BoxModel ?? Model.Load("models/dev/box.vmdl");
        if (boxModel is null)
        {
            Log.Warning($"{nameof(MatchHost)}: BoxModel is null and 'models/dev/box.vmdl' could not be loaded — table visuals will be invisible. Assign a box-shaped Model in the inspector.");
            return;
        }

        // Scaling: every cube/box model has a "native size" (the dimensions of the unscaled mesh).
        // The default cube_white_64 is 64 units cubed, so a LocalScale of 1 renders a 64-unit cube.
        // To render a 100-unit-wide slate from a 64-native cube we need scale = 100/64 ≈ 1.5625.
        float boxN = MathF.Max(0.0001f, BoxModelNativeSize);

        // Visual rails come from the source RailData (6 long bodies), NOT from the post-built
        // CushionSegment[] which subdivides each corner into a fillet arc (300+ tiny segments).
        var visualRails = def.Rails;

        // --- Slate: a thin flat box covering the entire playfield, with its top face at world Z = 0. ---
        {
            var slate = Scene.CreateObject(true);
            slate.Name = "TableSlate";
            // Top-level scene objects (no parenting). Parenting under Match was suspected to break
            // rendering but the real bug was mounted cloud assets at runtime. With models/dev/box.vmdl
            // parenting works fine if you want a tidy hierarchy — revisit if needed.
            float widthUnits  = Conversions.MetresToUnits(def.Width);   // engine X  → sbox Y span
            float lengthUnits = Conversions.MetresToUnits(def.Length);  // engine Z  → sbox X span
            slate.WorldPosition = new Vector3(0f, 0f, -SlateThicknessUnits * 0.5f);
            slate.LocalScale    = new Vector3(lengthUnits / boxN, widthUnits / boxN, SlateThicknessUnits / boxN);
            var mr = slate.AddComponent<ModelRenderer>();
            mr.Model = boxModel;
            mr.Tint  = SlateColor;
            _tableObjects.Add(slate);
        }

        // --- Rails: one box per source RailData (6 total), oriented along the rail direction, sitting on the slate. ---
        for (int i = 0; i < visualRails.Count; i++)
        {
            var rd = visualRails[i];

            // RailData stores 2D positions: (engine.X, engine.Z) with engine.Y = 0 implied.
            // Build 3D engine vectors at slate height.
            var startEngine = new SnVec3(rd.Start.X, 0f, rd.Start.Y);
            var endEngine   = new SnVec3(rd.End.X,   0f, rd.End.Y);

            SnVec3 midEngine  = (startEngine + endEngine) * 0.5f;
            SnVec3 diffEngine = endEngine - startEngine;
            float lengthUnits = Conversions.MetresToUnits(diffEngine.Length());

            Vector3 midWorld = Conversions.EngineToWorld(midEngine);
            Vector3 dirWorld = Conversions.EngineToWorld(diffEngine);
            float yawDeg = MathF.Atan2(dirWorld.y, dirWorld.x) * (180f / MathF.PI);

            var rail = Scene.CreateObject(true);
            rail.Name = $"Rail_{i}_{rd.Name}";
            rail.WorldPosition = new Vector3(midWorld.x, midWorld.y, RailHeightUnits * 0.5f);
            rail.WorldRotation = Rotation.FromYaw(yawDeg);
            // After yaw rotation: X = length axis, Y = thickness (toward playfield), Z = height (up).
            rail.LocalScale    = new Vector3(lengthUnits / boxN, RailThicknessUnits / boxN, RailHeightUnits / boxN);

            var mr = rail.AddComponent<ModelRenderer>();
            mr.Model = boxModel;
            mr.Tint  = RailColor;
            _tableObjects.Add(rail);
        }

        // --- Pockets: dark cubes sunk into the slate at each pocket throat. ---
        for (int i = 0; i < pockets.Length; i++)
        {
            var p = pockets[i];
            SnVec3 midEngine  = (p.P1 + p.P2) * 0.5f;
            SnVec3 diffEngine = p.P2 - p.P1;
            float gateLengthUnits = Conversions.MetresToUnits(diffEngine.Length());

            Vector3 midWorld = Conversions.EngineToWorld(midEngine);

            var pocket = Scene.CreateObject(true);
            pocket.Name = $"Pocket_{i}";
            // Sink below the slate top so the dark cube reads as a hole.
            pocket.WorldPosition = new Vector3(midWorld.x, midWorld.y, -SlateThicknessUnits * 0.25f);
            pocket.LocalScale    = new Vector3(gateLengthUnits / boxN, gateLengthUnits / boxN, (SlateThicknessUnits * 1.1f) / boxN);

            var mr = pocket.AddComponent<ModelRenderer>();
            mr.Model = boxModel;
            mr.Tint  = PocketColor;
            _tableObjects.Add(pocket);
        }

        Log.Info($"{nameof(MatchHost)}.BuildTableVisuals: spawned {_tableObjects.Count} table GameObjects under {GameObject.Name}.");
    }

    /// <summary>Strikes the cue ball forward (+Z in engine space) with the configured force.</summary>
    public void Strike()
    {
        if (_engine is null) return;
        _engine.StrikeCueBall(
            id: 0,
            aimDirection: new SnVec3(0, 0, 1),
            force: StrikeForce,
            hitOffset: SnVec3.Zero);
    }

    /// <summary>
    /// Draws a wireframe outline of slate / rails / pockets directly in the editor viewport
    /// so the camera, light, and scene composition can be tuned without entering play mode.
    /// Uses the exact same geometry the physics layer consumes (via <see cref="TableBuilder"/>),
    /// so what shows in the editor matches what's spawned at play.
    ///
    /// Fires every frame in the editor; cheap enough to leave on (the bench builds the same
    /// layout in ~1 ms).
    /// </summary>
    protected override void DrawGizmos()
    {
        var def = TableDefinition.BuildWpaStandard();
        var (rails, pockets) = TableBuilder.Build(def);

        float widthUnits  = Conversions.MetresToUnits(def.Width);
        float lengthUnits = Conversions.MetresToUnits(def.Length);

        // Slate outline.
        using (Gizmo.Scope("slate"))
        {
            Gizmo.Transform = new Transform(new Vector3(0, 0, -SlateThicknessUnits * 0.5f));
            Gizmo.Draw.Color = SlateColor.WithAlpha(0.8f);
            Gizmo.Draw.LineBBox(new BBox(
                new Vector3(-lengthUnits * 0.5f, -widthUnits * 0.5f, -SlateThicknessUnits * 0.5f),
                new Vector3( lengthUnits * 0.5f,  widthUnits * 0.5f,  SlateThicknessUnits * 0.5f)));
        }

        // Rails: one wireframe box per cushion segment.
        for (int i = 0; i < rails.Length; i++)
        {
            var seg = rails[i];
            SnVec3 midEngine  = (seg.Start + seg.End) * 0.5f;
            SnVec3 diffEngine = seg.End - seg.Start;
            float railLen = Conversions.MetresToUnits(diffEngine.Length());

            Vector3 midWorld = Conversions.EngineToWorld(midEngine);
            Vector3 dirWorld = Conversions.EngineToWorld(diffEngine);
            float yawDeg = MathF.Atan2(dirWorld.y, dirWorld.x) * (180f / MathF.PI);

            using (Gizmo.Scope($"rail-{i}"))
            {
                Gizmo.Transform = new Transform(
                    new Vector3(midWorld.x, midWorld.y, RailHeightUnits * 0.5f),
                    Rotation.FromYaw(yawDeg));
                Gizmo.Draw.Color = RailColor.WithAlpha(0.9f);
                Gizmo.Draw.LineBBox(new BBox(
                    new Vector3(-railLen * 0.5f, -RailThicknessUnits * 0.5f, -RailHeightUnits * 0.5f),
                    new Vector3( railLen * 0.5f,  RailThicknessUnits * 0.5f,  RailHeightUnits * 0.5f)));
            }
        }

        // Pockets: wireframe boxes at each gate midpoint.
        for (int i = 0; i < pockets.Length; i++)
        {
            var p = pockets[i];
            SnVec3 midEngine  = (p.P1 + p.P2) * 0.5f;
            SnVec3 diffEngine = p.P2 - p.P1;
            float gateLen = Conversions.MetresToUnits(diffEngine.Length());

            Vector3 midWorld = Conversions.EngineToWorld(midEngine);

            using (Gizmo.Scope($"pocket-{i}"))
            {
                Gizmo.Transform = new Transform(
                    new Vector3(midWorld.x, midWorld.y, -SlateThicknessUnits * 0.25f));
                Gizmo.Draw.Color = PocketColor.WithAlpha(0.9f);
                Gizmo.Draw.LineBBox(new BBox(
                    new Vector3(-gateLen * 0.5f, -gateLen * 0.5f, -SlateThicknessUnits * 0.55f),
                    new Vector3( gateLen * 0.5f,  gateLen * 0.5f,  SlateThicknessUnits * 0.55f)));
            }
        }
    }
}
