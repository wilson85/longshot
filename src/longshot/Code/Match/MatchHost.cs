using System;
using LongShot.Engine;
using LongShot.Rules;
using LongShot.Shot;
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
    /// Native size of <c>models/dev/sphere.vmdl</c> in s&amp;box units. The core dev sphere is 50u across,
    /// so its <c>LocalScale</c> must be divided by 50 to render at a requested diameter (in units).
    /// Hard-coded as a constant — the dev sphere is the canonical fallback and isn't user-swappable.
    /// </summary>
    private const float DevSphereNativeSize = 50f;

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

    // -------------------- Aim camera + cue stick --------------------

    /// <summary>
    /// The scene <see cref="CameraComponent"/> that the aim controller should drive. Optional —
    /// if left null, <see cref="OnStart"/> auto-discovers the first <c>IsMainCamera</c> component
    /// in the scene (falling back to the first <see cref="CameraComponent"/> found). Assign by hand
    /// only if you have multiple cameras and want a non-default one driven.
    /// </summary>
    [Property] public CameraComponent ViewCamera { get; set; }

    /// <summary>Camera distance from the cue ball (units). Real billiards eye-line ≈ 50–80 units (1.25–2 m).</summary>
    [Property, Range(20f, 200f)] public float CameraDistance { get; set; } = 60f;

    /// <summary>Minimum camera pitch (degrees above the table plane). Prevents the camera from going below the table.</summary>
    [Property, Range(2f, 30f)] public float MinPitchDeg { get; set; } = 8f;

    /// <summary>Maximum camera pitch (degrees above the table plane). 89° = nearly straight down.</summary>
    [Property, Range(30f, 89f)] public float MaxPitchDeg { get; set; } = 70f;

    /// <summary>Initial camera elevation (degrees). 30° is a comfortable over-the-shoulder default.</summary>
    [Property, Range(2f, 89f)] public float StartingPitchDeg { get; set; } = 25f;

    /// <summary>Mouse sensitivity multiplier applied to <see cref="Input.AnalogLook"/> deltas.</summary>
    [Property, Range(0.05f, 2f)] public float MouseSensitivity { get; set; } = 0.3f;

    /// <summary>If true, the camera is positioned around the cue ball every frame (recommended).</summary>
    [Property] public bool ControlCamera { get; set; } = true;

    /// <summary>Cue stick length (units). Real cues ≈ 56″ ≈ 56u (1u = 1 inch).</summary>
    [Property, Range(20f, 80f)] public float CueLengthUnits { get; set; } = 56f;

    /// <summary>Cue stick radius at the visual mesh (units). 0.4u ≈ 20 mm diameter.</summary>
    [Property, Range(0.1f, 2f)] public float CueRadiusUnits { get; set; } = 0.4f;

    /// <summary>Distance the cue's tip is pulled back from the cue ball when at rest (units).</summary>
    [Property, Range(0f, 20f)] public float CueDrawbackUnits { get; set; } = 4f;

    /// <summary>Tint applied to the cue stick. Default: light maple wood.</summary>
    [Property] public Color CueColor { get; set; } = new Color(0.82f, 0.65f, 0.42f);

    private BilliardsEngine _engine;
    private readonly List<GameObject> _ballObjects = new();
    private readonly List<GameObject> _tableObjects = new();

    // --- Aim state ---
    /// <summary>Current aim yaw in degrees. 0° = aim along world +X (s&amp;box "forward").</summary>
    private float _aimYawDeg;
    /// <summary>Current camera elevation pitch in degrees (clamped to [MinPitchDeg, MaxPitchDeg]).</summary>
    private float _aimPitchDeg;
    /// <summary>Spawned in OnStart, transformed every frame. Local +X is the cue's length axis (tip at +X).</summary>
    private GameObject _cueStickGo;

    // --- Rules + per-shot lifecycle ---
    private EightBallRules _rules;
    private ShotRecorder _recorder;
    private bool _shotInFlight;
    private int _shotNumber;

    /// <summary>Read-only view of the 8-ball rules state for the inspector / external observers.</summary>
    public int CurrentPlayer => _rules?.CurrentPlayer ?? 1;
    public bool OpenTable => _rules?.OpenTable ?? true;
    public string Player1Group => _rules?.Player1Group?.ToString() ?? "(unassigned)";
    public string Player2Group => _rules?.Player2Group?.ToString() ?? "(unassigned)";
    public bool GameOver => _rules?.GameOver ?? false;
    public int Winner => _rules?.Winner ?? 0;
    public string LastShotDescription => _rules?.LastShot?.Description ?? "(no shots yet)";

    protected override void OnStart()
    {
        _engine = new BilliardsEngine(seed: 1);
        _rules = new EightBallRules();

        var def = TableDefinition.BuildWpaStandard();
        var (rails, pockets) = TableBuilder.Build(def);
        _engine.InitializeMatch(rails, pockets);

        if (SpawnTableVisuals)
        {
            BuildTableVisuals(def, rails, pockets);
        }

        // First-light rack: cue (id 0) + one solid (id 1). Enough to test the open-table →
        // group-assignment path. Full 16-ball rack is a follow-up — for now we only need
        // enough balls to verify the per-shot lifecycle wiring works.
        _engine.AddBall(new SnVec3(0, GameSettings.BallRadius, -0.8f), BallType.Cue);
        _engine.AddBall(new SnVec3(0, GameSettings.BallRadius,  0.8f), BallType.Normal);

        float diameterUnits = Conversions.MetresToUnits(GameSettings.BallRadius * 2f);
        // Core dev sphere is a local asset and works at runtime (unlike mounted cloud .vmdl assets).
        // For artist-authored balls, assign a BallPrefab in the inspector instead.
        var sphereFallback = Model.Load("models/dev/sphere.vmdl");

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
                // Procedural fallback: spawn a ModelRenderer'd sphere. The dev sphere is 50u native.
                go = Scene.CreateObject(true);
                go.Name = $"Ball_{i}";
                float ballScale = diameterUnits / DevSphereNativeSize;
                go.LocalScale = new Vector3(ballScale, ballScale, ballScale);
                var mr = go.AddComponent<ModelRenderer>();
                mr.Model = sphereFallback;
                mr.Tint  = Color.White;
            }
            else
            {
                Log.Warning($"{nameof(MatchHost)}: BallPrefab is null AND 'models/dev/sphere.vmdl' could not be loaded. Ball #{i} will not be visible.");
                continue;
            }
            _ballObjects.Add(go);
        }

        // -------- Cue stick + aim camera --------
        _aimYawDeg = 0f;                                     // initial aim: along world +X (engine +Z, table length)
        _aimPitchDeg = MathX.Clamp(StartingPitchDeg, MinPitchDeg, MaxPitchDeg);
        DiscoverViewCameraIfNeeded();                        // find the scene's main camera if not assigned in inspector
        DisableFreeCamIfAny();
        SpawnCueStick();
        UpdateAimRig();                                      // first-frame placement so the camera/cue aren't at origin

        if (AutoStrikeOnStart)
        {
            Strike();
        }
    }

    /// <summary>
    /// Build a procedural cue stick mesh (long thin box, tip at local +X end) and spawn it as a
    /// renderable GameObject. The mesh is centred on its own origin; <see cref="UpdateAimRig"/> places
    /// it each frame so the tip sits behind the cue ball at the configured drawback distance.
    /// </summary>
    private void SpawnCueStick()
    {
        var material = Material.Load("materials/default.vmat");
        // Box with X = half-length, Y = radius, Z = radius. Tip at +X end (local) → world aim direction
        // after Rotation.LookAt(aimDir) below.
        var cueModel = ProceduralMeshes.BuildBox(
            new Vector3(CueLengthUnits * 0.5f, CueRadiusUnits, CueRadiusUnits),
            material);

        _cueStickGo = Scene.CreateObject(true);
        _cueStickGo.Name = "CueStick";
        var mr = _cueStickGo.AddComponent<ModelRenderer>();
        mr.Model = cueModel;
        mr.Tint = CueColor;
    }

    /// <summary>
    /// If a <see cref="FreeCam"/> (or similar input-driven rotator) is attached to <see cref="ViewCamera"/>'s
    /// GameObject, disable it. Without this, FreeCam reads <c>Input.AnalogLook</c> and overwrites our
    /// camera rotation every frame (see <c>sbox-runtime-rendering</c> skill).
    /// </summary>
    private void DisableFreeCamIfAny()
    {
        if (ViewCamera is null) return;
        var freeCam = ViewCamera.GameObject.Components.Get<FreeCam>();
        if (freeCam is not null) freeCam.Enabled = false;
    }

    /// <summary>
    /// Locate the scene's main camera if no <see cref="ViewCamera"/> was assigned in the inspector.
    /// Order: first <see cref="CameraComponent"/> with <c>IsMainCamera == true</c>, then any other
    /// <see cref="CameraComponent"/>. Logs a warning if none is found — the aim system will still run
    /// the cue stick but won't reposition any camera.
    /// </summary>
    private void DiscoverViewCameraIfNeeded()
    {
        if (ViewCamera.IsValid()) return;

        CameraComponent best = null;
        foreach (var cam in Scene.GetAllComponents<CameraComponent>())
        {
            if (cam.IsMainCamera) { best = cam; break; }
            best ??= cam;          // fallback to first one found
        }
        ViewCamera = best;

        if (ViewCamera is null)
        {
            Log.Warning($"{nameof(MatchHost)}: no CameraComponent found in the scene. Camera control disabled.");
        }
    }

    /// <summary>
    /// Drive the deterministic physics on the fixed tick, and detect end-of-shot to close out
    /// the rules observer + ShotRecorder lifecycle.
    /// </summary>
    protected override void OnFixedUpdate()
    {
        if (_engine is null) return;
        _engine.Tick(Time.Delta);
        _recorder?.Sample();

        // If a shot is in flight and all balls have settled, evaluate the shot through the rules.
        if (_shotInFlight && _engine.AreAllBallsAsleep())
        {
            FinishShot();
        }
    }

    /// <summary>Mirror engine state to GameObject transforms every frame for smooth visuals.</summary>
    protected override void OnUpdate()
    {
        if (_engine is null) return;

        // -------- Aim input (only while the shot has settled — locks aim during ball motion) --------
        if (!_shotInFlight)
        {
            var look = Input.AnalogLook;                       // (pitch, yaw, roll) in degrees per frame
            _aimYawDeg += look.yaw * MouseSensitivity;
            _aimPitchDeg = MathX.Clamp(_aimPitchDeg + look.pitch * MouseSensitivity, MinPitchDeg, MaxPitchDeg);
            // Normalise yaw to (-180, 180] to keep numbers tidy in the inspector.
            _aimYawDeg = ((_aimYawDeg + 180f) % 360f + 360f) % 360f - 180f;
        }

        UpdateAimRig();

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
    /// Place the camera and cue stick each frame based on <see cref="_aimYawDeg"/>,
    /// <see cref="_aimPitchDeg"/>, and the cue ball's current world position. Idempotent — safe to call
    /// from <see cref="OnStart"/> for first-frame placement and from <see cref="OnUpdate"/> thereafter.
    /// </summary>
    private void UpdateAimRig()
    {
        if (_engine is null) return;
        if (_engine.PhysicsStates.Length == 0) return;        // no balls spawned yet

        var cue = _engine.PhysicsStates[0];
        if (cue.State == MotionState.Pocketed) return;        // cue ball is gone — leave rig parked

        Vector3 cueBallWorld = Conversions.EngineToWorld(cue.Position);

        float yawRad   = _aimYawDeg   * (MathF.PI / 180f);
        float pitchRad = _aimPitchDeg * (MathF.PI / 180f);

        // Aim direction is the horizontal vector the player is pointing at, in s&box world XY plane.
        Vector3 aimDirWorld = new Vector3(MathF.Cos(yawRad), MathF.Sin(yawRad), 0f);

        // -------- Camera: orbit behind cue ball, look at cue ball --------
        if (ControlCamera && ViewCamera.IsValid())
        {
            float horiz = CameraDistance * MathF.Cos(pitchRad);
            float vert  = CameraDistance * MathF.Sin(pitchRad);
            Vector3 camPos = cueBallWorld - aimDirWorld * horiz + new Vector3(0, 0, vert);

            ViewCamera.GameObject.WorldPosition = camPos;
            ViewCamera.GameObject.WorldRotation = Rotation.LookAt((cueBallWorld - camPos).Normal);
        }

        // -------- Cue stick: tip pulled back from the cue ball by CueDrawbackUnits along -aim --------
        if (_cueStickGo.IsValid())
        {
            _cueStickGo.Enabled = !_shotInFlight;             // hide while balls are moving

            if (!_shotInFlight)
            {
                // Local +X points along aimDirWorld. Tip at +halfLen in local; place GameObject so the tip
                // sits one CueDrawbackUnits behind the cue ball (cue draws back from the ball at rest).
                Vector3 tipTarget = cueBallWorld - aimDirWorld * CueDrawbackUnits;
                Vector3 cueCenter = tipTarget - aimDirWorld * (CueLengthUnits * 0.5f);

                _cueStickGo.WorldPosition = cueCenter;
                _cueStickGo.WorldRotation = Rotation.LookAt(aimDirWorld);
            }
        }
    }

    /// <summary>
    /// Procedurally spawns slate / rails / pockets from the engine's collision primitives. Each piece is
    /// built as a runtime <see cref="Model"/> via <see cref="ProceduralMeshes.BuildBox"/> — no .vmdl assets
    /// involved. This avoids the cloud-asset rendering bug entirely (mounted cloud .vmdl assets silently
    /// fail to bind at runtime; see <c>.claude/skills/sbox-runtime-rendering/SKILL.md</c>) and also means
    /// every box is sized in exact playfield units, no native-size scaling math.
    ///
    /// All spawned GameObjects sit at the scene root. Parenting under <see cref="Component.GameObject"/>
    /// is fine if you want a tidy hierarchy — kept flat for now so the inspector lists them at the top.
    /// </summary>
    private void BuildTableVisuals(TableDefinition def, CushionSegment[] rails, PocketBeam[] pockets)
    {
        // Single shared material for every table piece. Material.Load returns a real PBR vmat that
        // ModelRenderer.Tint multiplies cleanly; Material.Create("name", "shader") would produce a
        // magenta-checker missing-texture placeholder instead. Each call to BuildBox makes a *new*
        // Sandbox.Mesh bound to this material, which is fine — the material itself is reused.
        var material = Material.Load("materials/default.vmat");

        // Visual rails come from the source RailData (6 long bodies), NOT from the post-built
        // CushionSegment[] which subdivides each corner into a fillet arc (300+ tiny segments).
        var visualRails = def.Rails;

        // --- Slate: a thin flat box covering the entire playfield, with its top face at world Z = 0. ---
        {
            float widthUnits  = Conversions.MetresToUnits(def.Width);   // engine X  → sbox Y span
            float lengthUnits = Conversions.MetresToUnits(def.Length);  // engine Z  → sbox X span

            var slateModel = ProceduralMeshes.BuildBox(
                new Vector3(lengthUnits * 0.5f, widthUnits * 0.5f, SlateThicknessUnits * 0.5f),
                material);

            var slate = Scene.CreateObject(true);
            slate.Name = "TableSlate";
            slate.WorldPosition = new Vector3(0f, 0f, -SlateThicknessUnits * 0.5f);
            // LocalScale stays at (1,1,1) — the mesh is already built at the right size.
            var mr = slate.AddComponent<ModelRenderer>();
            mr.Model = slateModel;
            mr.Tint  = SlateColor;
            _tableObjects.Add(slate);
        }

        // --- Rails: one hexagonal-with-jaws prism per source RailData (6 total). ---
        // The local 2D outline comes from TableBuilder.BuildRailVisualOutline, which shares geometry
        // with the physics layer (BuildRailPhysics) — the rendered rail edge therefore matches the
        // cushion collision boundary exactly. The outline has a straight back edge (table boundary)
        // and a shorter playfield-facing edge with chamfered, filleted jaw cutouts at each end.
        for (int i = 0; i < visualRails.Count; i++)
        {
            var rd = visualRails[i];

            // Outline points are in rail-local 2D, metres. Local axes:
            //   x = along-rail length (-halfLen .. +halfLen)
            //   y = perpendicular  (-halfWid = back/table-boundary, +halfWid = playfield-facing)
            var (outlineMetres, worldMidEngine, _) = TableBuilder.BuildRailVisualOutline(rd);

            // Convert outline to s&box units. Local x/y stay axis-aligned with the mesh's local X/Y;
            // the yaw rotation below rotates the whole mesh around s&box +Z to align with the world rail.
            var outlineUnits = new List<Vector2>(outlineMetres.Count);
            for (int k = 0; k < outlineMetres.Count; k++)
            {
                outlineUnits.Add(new Vector2(
                    Conversions.MetresToUnits(outlineMetres[k].X),
                    Conversions.MetresToUnits(outlineMetres[k].Y)));
            }

            // Yaw: derived from the rail direction in world space (consistent with the previous box-rail
            // placement). We deliberately don't use the engine-frame yaw returned by BuildRailVisualOutline
            // — the Y-up → Z-up axis swap means the world-frame angle is what s&box needs.
            var startEngine = new SnVec3(rd.Start.X, 0f, rd.Start.Y);
            var endEngine   = new SnVec3(rd.End.X,   0f, rd.End.Y);
            Vector3 midWorld = Conversions.EngineToWorld(worldMidEngine);
            Vector3 dirWorld = Conversions.EngineToWorld(endEngine - startEngine);
            float yawDeg = MathF.Atan2(dirWorld.y, dirWorld.x) * (180f / MathF.PI);

            // Extrude vertically. The prism spans Z ∈ [-halfHeight, +halfHeight] in its own local
            // frame; placing the GameObject at z = halfHeight puts the bottom face flush with the slate.
            float halfHeight = RailHeightUnits * 0.5f;
            var railModel = ProceduralMeshes.BuildPrism(outlineUnits, halfHeight, material);

            var rail = Scene.CreateObject(true);
            rail.Name = $"Rail_{i}_{rd.Name}";
            rail.WorldPosition = new Vector3(midWorld.x, midWorld.y, halfHeight);
            rail.WorldRotation = Rotation.FromYaw(yawDeg);

            var mr = rail.AddComponent<ModelRenderer>();
            mr.Model = railModel;
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

            float halfGate = gateLengthUnits * 0.5f;
            float halfDepth = SlateThicknessUnits * 0.55f;
            var pocketModel = ProceduralMeshes.BuildBox(
                new Vector3(halfGate, halfGate, halfDepth),
                material);

            var pocket = Scene.CreateObject(true);
            pocket.Name = $"Pocket_{i}";
            // Sink below the slate top so the dark cube reads as a hole.
            pocket.WorldPosition = new Vector3(midWorld.x, midWorld.y, -SlateThicknessUnits * 0.25f);

            var mr = pocket.AddComponent<ModelRenderer>();
            mr.Model = pocketModel;
            mr.Tint  = PocketColor;
            _tableObjects.Add(pocket);
        }

        Log.Info($"{nameof(MatchHost)}.BuildTableVisuals: spawned {_tableObjects.Count} procedurally-built table GameObjects.");
    }

    /// <summary>
    /// Strikes the cue ball along the current aim direction with the configured force. Wraps the
    /// strike with a fresh <see cref="ShotRecorder"/> + <see cref="EightBallRules.OnShotStart"/>
    /// so the rules observer sees the per-shot lifecycle. Ignored if a shot is already in flight
    /// or the game's over.
    /// <para>
    /// The aim direction comes from <see cref="_aimYawDeg"/> (set by mouse look in
    /// <see cref="OnUpdate"/>). We project to s&amp;box's XY plane, convert to engine coords via
    /// <see cref="Conversions.WorldToEngine"/>, and zero out any vertical component so the cue
    /// always strikes parallel to the table (no jump-shot energy unless we add elevation later).
    /// </para>
    /// </summary>
    public void Strike()
    {
        // EightBallRules integration: per-shot lifecycle via ShotRecorder.
        if (_engine is null || _rules is null) return;
        if (_shotInFlight) return;             // already simulating a shot
        if (_rules.GameOver) return;           // game's done

        _shotNumber++;
        _recorder = new ShotRecorder(_engine);
        _rules.OnShotStart(new ShotContext
        {
            ShotNumber = _shotNumber,
            StateAtStart = _engine.SnapshotState(),
        });

        // World aim direction in s&box's XY plane, derived from current aim yaw.
        float yawRad = _aimYawDeg * (MathF.PI / 180f);
        Vector3 aimDirWorld = new Vector3(MathF.Cos(yawRad), MathF.Sin(yawRad), 0f);
        SnVec3 aimDirEngine = Conversions.WorldToEngine(aimDirWorld);
        aimDirEngine.Y = 0f;                                   // strip vertical — table-plane shot only
        if (aimDirEngine.LengthSquared() > 1e-6f)
        {
            aimDirEngine = SnVec3.Normalize(aimDirEngine);
        }
        else
        {
            // Degenerate aim — fall back to engine +Z so we don't pass a zero vector to StrikeCueBall.
            aimDirEngine = new SnVec3(0, 0, 1);
        }

        _engine.StrikeCueBall(
            id: 0,
            aimDirection: aimDirEngine,
            force: StrikeForce,
            hitOffset: SnVec3.Zero);

        _shotInFlight = true;
    }

    /// <summary>
    /// Called from <see cref="OnFixedUpdate"/> when <see cref="BilliardsEngine.AreAllBallsAsleep"/>
    /// reports the shot has settled. Finalises the recorder, runs the rules observer, logs the
    /// outcome, and resets the per-shot state so the next <see cref="Strike"/> can fire.
    /// </summary>
    private void FinishShot()
    {
        var summary = _recorder.Finalize();
        _rules.OnShotEnd(summary);
        _recorder.Dispose();
        _recorder = null;
        _shotInFlight = false;

        var shot = _rules.LastShot;
        if (shot is null)
        {
            Log.Info($"Shot {_shotNumber}: rules produced no result.");
            return;
        }

        string p1 = _rules.Player1Group?.ToString() ?? "-";
        string p2 = _rules.Player2Group?.ToString() ?? "-";
        Log.Info(
            $"Shot {_shotNumber} P{shot.Player}: {shot.Description}. "
            + $"Turn → P{_rules.CurrentPlayer}, OpenTable={_rules.OpenTable}, "
            + $"P1={p1}, P2={p2}, BallInHand={shot.BallInHand}");

        if (_rules.GameOver)
        {
            Log.Info($"GAME OVER. Winner: P{_rules.Winner}. Reason: {_rules.WinReason}");
        }
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
