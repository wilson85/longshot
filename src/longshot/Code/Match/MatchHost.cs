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
    /// Native size of <c>models/dev/sphere.vmdl</c> in s&amp;box units. Verified empirically 2026-05-17
    /// via Bounds inspection: the core dev sphere is <b>64u across</b> (not 50u as initially assumed —
    /// the box and the sphere have different native sizes). LocalScale must be divided by 64 to render
    /// at a requested diameter in units. Hard-coded as a constant; the dev sphere is the canonical
    /// fallback and isn't user-swappable.
    /// </summary>
    private const float DevSphereNativeSize = 64f;

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

    /// <summary>Cue radius at the butt end (units). 0.55u ≈ 28 mm diameter — standard pool cue butt.</summary>
    [Property, Range(0.1f, 2f)] public float CueButtRadiusUnits { get; set; } = 0.55f;

    /// <summary>Cue radius at the tip end (units). 0.25u ≈ 13 mm diameter — standard pool cue tip.</summary>
    [Property, Range(0.05f, 1f)] public float CueTipRadiusUnits { get; set; } = 0.25f;

    /// <summary>Number of segments around the cue's circumference. 16+ reads as smooth at cue scale.</summary>
    [Property, Range(6f, 32f)] public int CueSegments { get; set; } = 16;

    /// <summary>Fraction of the cue length covered by the grip wrap at the butt end (0 = no wrap, 0.3 = back 30%).</summary>
    [Property, Range(0f, 0.5f)] public float CueGripFraction { get; set; } = 0.30f;

    /// <summary>Tint applied to the cue's grip wrap (butt section). Default: dark navy linen.</summary>
    [Property] public Color CueGripColor { get; set; } = new Color(0.10f, 0.12f, 0.20f);

    /// <summary>Fraction of cue length at the tip end painted as the ferrule (white plastic before the leather tip).</summary>
    [Property, Range(0f, 0.05f)] public float CueFerruleFraction { get; set; } = 0.015f;

    /// <summary>Tint applied to the cue's ferrule (just behind the tip). Off-white plastic.</summary>
    [Property] public Color CueFerruleColor { get; set; } = new Color(0.95f, 0.95f, 0.90f);

    /// <summary>Tint applied to the cue's leather tip (the contact pad).</summary>
    [Property] public Color CueTipColor { get; set; } = new Color(0.45f, 0.25f, 0.15f);

    /// <summary>Distance the cue's tip is pulled back from the cue ball when at rest (units).</summary>
    [Property, Range(0f, 20f)] public float CueDrawbackUnits { get; set; } = 4f;

    /// <summary>Maximum drawback during a stroke (units). Caps the cue's visual pull-back and the resulting force.</summary>
    [Property, Range(10f, 120f)] public float MaxDrawbackUnits { get; set; } = 60f;

    /// <summary>
    /// Mouse sensitivity for the stroke gesture, in <b>units of drawback per pixel of mouse-Y motion</b>.
    /// Adjustable at runtime with <b>+</b> / <b>-</b> keys; the value persists via <c>Game.Cookies</c>
    /// across sessions so each player's preferred feel is remembered.
    /// </summary>
    [Property, Range(0.005f, 1f)] public float StrokeSensitivity { get; set; } = 0.08f;

    /// <summary>Multiplicative step applied when the player presses + or - to adjust <see cref="StrokeSensitivity"/>.</summary>
    [Property, Range(1.01f, 1.5f)] public float StrokeSensitivityStep { get; set; } = 1.10f;

    /// <summary>Smaller multiplicative step used when Shift is held during + / - (fine adjust).</summary>
    [Property, Range(1.005f, 1.05f)] public float StrokeSensitivityFineStep { get; set; } = 1.02f;

    /// <summary>Cookie key used to persist <see cref="StrokeSensitivity"/> between sessions.</summary>
    private const string CookieStrokeSensitivity = "longshot.stroke_sensitivity";

    /// <summary>
    /// Cue speed (m/s) that maps to 100% power. The cue's forward velocity at impact is converted
    /// directly to ball impulse via <c>force = velocity × BallMass</c>; capping at this speed
    /// prevents accidentally hard pushes from launching the ball off the table. Real-world break
    /// strokes are ~7–9 m/s at the cue tip; 9 m/s is a comfortable ceiling.
    /// </summary>
    [Property, Range(2f, 12f)] public float MaxStrikeSpeedMPS { get; set; } = 9f;

    /// <summary>
    /// Power ceiling as a percentage of <see cref="MaxStrikeSpeedMPS"/>. The cue's velocity at impact
    /// is clamped to <c>MaxStrikeSpeedMPS × PowerLimitPercent / 100</c>. Drop this for finesse-only
    /// play (e.g. 50% disables hard breaks); keep at 100% for full range including breaks.
    /// </summary>
    [Property, Range(10f, 100f)] public float PowerLimitPercent { get; set; } = 100f;

    /// <summary>Minimum extra draw beyond rest before a release counts as a real stroke (units). Below this, click is treated as a cancel.</summary>
    [Property, Range(0.5f, 5f)] public float StrokeFireThresholdUnits { get; set; } = 1.5f;

    /// <summary>Tint applied to the cue stick. Default: light maple wood.</summary>
    [Property] public Color CueColor { get; set; } = new Color(0.82f, 0.65f, 0.42f);

    /// <summary>Mouse sensitivity while in English mode (hold E). Fraction-of-ball-radius per pitch/yaw degree.</summary>
    [Property, Range(0.005f, 0.1f)] public float EnglishSensitivity { get; set; } = 0.02f;

    /// <summary>Mouse sensitivity for butt elevation (hold B). Degrees of cue tilt per pitch-degree of mouse.</summary>
    [Property, Range(0.1f, 3f)] public float ElevationSensitivity { get; set; } = 0.6f;

    /// <summary>Maximum cue elevation in degrees. 45° is a steep massé; 60°+ approaches jump-shot territory.</summary>
    [Property, Range(5f, 75f)] public float MaxElevationDeg { get; set; } = 45f;

    // -------------------- Overhead view + table legs --------------------

    /// <summary>Height of the overhead camera above the slate (units) while RMB is held.</summary>
    [Property, Range(60f, 300f)] public float OverheadHeightUnits { get; set; } = 130f;

    /// <summary>Height of each procedural table leg (units). Real pool tables ≈ 30 inches = 30u.</summary>
    [Property, Range(10f, 40f)] public float LegHeightUnits { get; set; } = 30f;

    /// <summary>Cross-section (square) of each leg (units).</summary>
    [Property, Range(2f, 12f)] public float LegWidthUnits { get; set; } = 6f;

    /// <summary>How far the legs sit inset from the corner of the table (units). Small inset → flush with rails; larger → tucked under slate.</summary>
    [Property, Range(0f, 15f)] public float LegInsetUnits { get; set; } = 4f;

    /// <summary>Tint applied to leg ModelRenderers. Default: same dark walnut as the rails.</summary>
    [Property] public Color LegColor { get; set; } = new Color(0.20f, 0.12f, 0.07f);

    // -------------------- Sound --------------------

    /// <summary>Master volume multiplier for all impact / pocket / cue sounds (0 = mute).</summary>
    [Property, Range(0f, 1f)] public float SoundMasterVolume { get; set; } = 0.85f;

    /// <summary>
    /// Reference speed (m/s) at which an impact plays at full volume. Lower → quieter impacts get
    /// audible faster. Reasonable: 2–4 m/s (typical hard cue shot is ~3 m/s at the ball).
    /// </summary>
    [Property, Range(0.5f, 10f)] public float SoundVolumeReferenceSpeed { get; set; } = 3f;

    /// <summary>Half-range of random pitch jitter applied per impact (e.g. 0.08 → pitch ∈ [0.92, 1.08]).</summary>
    [Property, Range(0f, 0.4f)] public float SoundPitchJitter { get; set; } = 0.08f;

    /// <summary>If true, MatchHost loads built-in s&amp;box physics SoundFiles and plays them on engine events.</summary>
    [Property] public bool EnableSound { get; set; } = true;

    // -------------------- Aim line (ghost-ball predictor) --------------------

    /// <summary>If true, an aim-line mesh is drawn on the slate from the cue ball to the predicted first contact each frame.</summary>
    [Property] public bool ShowAimLine { get; set; } = true;

    /// <summary>Visible width of the aim line (units). 0.3u ≈ 7.6 mm — narrow enough to read as a guide line.</summary>
    [Property, Range(0.05f, 1f)] public float AimLineWidthUnits { get; set; } = 0.3f;

    /// <summary>Visible thickness (Z extent) of the aim line so it doesn't z-fight the slate. 0.05u ≈ 1.3 mm.</summary>
    [Property, Range(0.02f, 0.5f)] public float AimLineHeightUnits { get; set; } = 0.05f;

    /// <summary>Maximum aim-line length when no contact is predicted within range (units).</summary>
    [Property, Range(20f, 300f)] public float AimLineMaxLengthUnits { get; set; } = 200f;

    /// <summary>Tint applied to the aim-line ModelRenderer.</summary>
    [Property] public Color AimLineColor { get; set; } = new Color(1f, 1f, 1f);

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
    /// <summary>Aim-line mesh: unit-length flat box along local +X, scaled to predicted first-contact distance each frame.</summary>
    private GameObject _aimLineGo;

    // --- Stroke state (Shooters-Pool-style: hold LMB or S, pull back, fire on contact during push) ---
    /// <summary>True while the stroke button (LMB or S) is held.</summary>
    private bool _stroking;
    /// <summary>Live drawback distance during stroke (units). At rest, equals <see cref="CueDrawbackUnits"/>.</summary>
    private float _currentDrawbackUnits;
    /// <summary>Peak drawback reached during the current stroke. Force is computed from this when the cue tip reaches the ball.</summary>
    private float _peakDrawbackUnits;
    /// <summary>True once the cue tip reached the ball during the push phase and the shot fired. Prevents re-fire while button stays held.</summary>
    private bool _strokeFired;
    /// <summary>Smoothed cue forward velocity (units/sec) during the push phase. Positive when pushing toward the ball; sampled at impact for force.</summary>
    private float _pushVelocityUnitsPerSec;
    /// <summary>The actual % of MaxStrikeSpeedMPS used by the most recent strike, for HUD display.</summary>
    private float _lastStrikePowerPercent;

    // --- English (cue ball contact point) ---
    /// <summary>Side English: fraction of ball radius. -0.5 = full left contact, +0.5 = full right contact.</summary>
    private float _englishH;
    /// <summary>Vertical English: fraction of ball radius. +0.5 = top english (follow), -0.5 = bottom english (draw).</summary>
    private float _englishV;

    // --- Butt elevation (massé / jump strike) ---
    /// <summary>Cue elevation in degrees. 0 = horizontal, positive = butt raised / tip dipping toward ball top.</summary>
    private float _buttElevDeg;

    // --- Replay state ---
    /// <summary>Ball-state snapshot captured immediately before the most recent <see cref="Strike"/>. Used by <see cref="Replay"/>.</summary>
    private BallState[] _replaySnapshot;

    // --- Sound ---
    /// <summary>Plastic-impact variants for ball-ball collisions (random selection per event).</summary>
    private SoundFile[] _ballHitSounds;
    /// <summary>Wood-impact variants for ball-cushion (rail) collisions.</summary>
    private SoundFile[] _railHitSounds;
    /// <summary>Wood-small-impact variants for cue tip striking the cue ball.</summary>
    private SoundFile[] _cueStrikeSounds;
    /// <summary>Wood-small-impact variants for pocketing drop (softer than rail impact).</summary>
    private SoundFile[] _pocketDropSounds;
    /// <summary>RNG for sound variant + pitch jitter. Separate from the engine RNG so sound randomness never affects physics determinism.</summary>
    private System.Random _soundRng = new System.Random();

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
        LoadSoundsAndHookEvents();

        if (SpawnTableVisuals)
        {
            BuildTableVisuals(def, rails, pockets);
        }

        RackEightBall();

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
                // Procedural fallback: spawn a ModelRenderer'd sphere. The dev sphere is 64u diameter.
                go = Scene.CreateObject(true);
                go.Name = $"Ball_{i}";
                float ballScale = diameterUnits / DevSphereNativeSize;
                go.LocalScale = new Vector3(ballScale, ballScale, ballScale);
                var mr = go.AddComponent<ModelRenderer>();
                mr.Model = sphereFallback;
                mr.Tint  = BallTintForId(i);
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
        LoadStrokeSensitivityFromCookies();                  // restore saved sensitivity from previous session
        DiscoverViewCameraIfNeeded();                        // find the scene's main camera if not assigned in inspector
        DisableFreeCamIfAny();
        SpawnCueStick();
        if (ShowAimLine) SpawnAimLine();
        UpdateAimRig();                                      // first-frame placement so the camera/cue aren't at origin

        if (AutoStrikeOnStart)
        {
            Strike();
        }
    }

    /// <summary>
    /// Build the cue as a small hierarchy: an empty parent <see cref="_cueStickGo"/> whose local +X is the
    /// cue length axis (tip at +X), with three child renderers that together read as a realistic pool cue:
    /// <list type="bullet">
    ///   <item>shaft – tapered cylinder, butt → tip, light maple colour;</item>
    ///   <item>grip wrap – uniform cylinder covering the back <see cref="CueGripFraction"/> of the shaft,
    ///         slightly fatter so it sits proud of the wood; dark linen colour;</item>
    ///   <item>tip – tiny uniform cylinder at the +X end, very slightly fatter than the shaft tip so it
    ///         shows as a leather pad rather than z-fighting the shaft cap.</item>
    /// </list>
    /// <see cref="UpdateAimRig"/> positions the parent each frame so the tip sits behind the cue ball at
    /// the configured drawback distance; the children inherit the transform automatically.
    /// </summary>
    private void SpawnCueStick()
    {
        var material = Material.Load("materials/default.vmat");
        float halfLen = CueLengthUnits * 0.5f;

        // ---- Parent: an empty GameObject; we don't put a renderer here so the inspector tree stays clean. ----
        _cueStickGo = Scene.CreateObject(true);
        _cueStickGo.Name = "CueStick";

        // ---- 1. Shaft (tapered cylinder, light wood). Centred on local origin. ----
        var shaftModel = ProceduralMeshes.BuildTaperedCylinder(
            length: CueLengthUnits,
            buttRadius: CueButtRadiusUnits,
            tipRadius:  CueTipRadiusUnits,
            segments:   CueSegments,
            material:   material);
        var shaftGo = Scene.CreateObject(true);
        shaftGo.Name = "CueStick.Shaft";
        shaftGo.SetParent(_cueStickGo);
        shaftGo.LocalPosition = Vector3.Zero;
        shaftGo.LocalRotation = Rotation.Identity;
        var shaftMr = shaftGo.AddComponent<ModelRenderer>();
        shaftMr.Model = shaftModel;
        shaftMr.Tint = CueColor;

        // ---- 2. Grip wrap. Uniform cylinder over the back CueGripFraction of the shaft, slightly proud. ----
        if (CueGripFraction > 0.001f)
        {
            float gripLen     = CueLengthUnits * CueGripFraction;
            float gripRadius  = CueButtRadiusUnits * 1.05f;
            var gripModel = ProceduralMeshes.BuildTaperedCylinder(
                length:     gripLen,
                buttRadius: gripRadius,
                tipRadius:  gripRadius,                      // uniform → not actually tapered
                segments:   CueSegments,
                material:   material);
            var gripGo = Scene.CreateObject(true);
            gripGo.Name = "CueStick.Grip";
            gripGo.SetParent(_cueStickGo);
            // Grip occupies local X ∈ [-halfLen, -halfLen + gripLen]; centre = -halfLen + gripLen/2.
            gripGo.LocalPosition = new Vector3(-halfLen + gripLen * 0.5f, 0, 0);
            gripGo.LocalRotation = Rotation.Identity;
            var gripMr = gripGo.AddComponent<ModelRenderer>();
            gripMr.Model = gripModel;
            gripMr.Tint = CueGripColor;
        }

        // ---- 3. Tip (leather pad). Tiny uniform cylinder at the +X end, just barely fatter than the shaft tip. ----
        if (CueFerruleFraction > 0.0001f)
        {
            float tipLen     = CueLengthUnits * CueFerruleFraction;
            float tipMeshRad = CueTipRadiusUnits * 1.04f;
            var tipModel = ProceduralMeshes.BuildTaperedCylinder(
                length:     tipLen,
                buttRadius: tipMeshRad,
                tipRadius:  tipMeshRad,
                segments:   CueSegments,
                material:   material);
            var tipGo = Scene.CreateObject(true);
            tipGo.Name = "CueStick.Tip";
            tipGo.SetParent(_cueStickGo);
            // Tip occupies local X ∈ [+halfLen - tipLen, +halfLen]; centre = +halfLen - tipLen/2.
            tipGo.LocalPosition = new Vector3(+halfLen - tipLen * 0.5f, 0, 0);
            tipGo.LocalRotation = Rotation.Identity;
            var tipMr = tipGo.AddComponent<ModelRenderer>();
            tipMr.Model = tipModel;
            tipMr.Tint = CueTipColor;
        }
    }

    /// <summary>
    /// Build a 1-unit-long flat-strip mesh for the aim line, spawned as a GameObject the per-frame
    /// <see cref="UpdateAimRig"/> rescales (along local X) and orients to point from the cue ball to
    /// the predicted first contact.
    /// </summary>
    private void SpawnAimLine()
    {
        var material = Material.Load("materials/default.vmat");
        var model = ProceduralMeshes.BuildBox(
            new Vector3(0.5f, AimLineWidthUnits * 0.5f, AimLineHeightUnits * 0.5f),
            material);

        _aimLineGo = Scene.CreateObject(true);
        _aimLineGo.Name = "AimLine";
        var mr = _aimLineGo.AddComponent<ModelRenderer>();
        mr.Model = model;
        mr.Tint  = AimLineColor;
    }

    /// <summary>
    /// Predict how far the cue ball would travel along <paramref name="aimDirEngine"/> before its first
    /// contact (another ball or a cushion segment). Treats the cue ball as a virtual moving ball with
    /// 1 m/s along the aim direction so the returned time-of-impact is numerically equal to the
    /// distance in metres. Returns <see cref="float.PositiveInfinity"/> if no contact is found.
    /// </summary>
    private float PredictFirstContactDistanceMetres(SnVec3 aimDirEngine)
    {
        if (_engine is null || _engine.PhysicsStates.Length == 0) return float.PositiveInfinity;

        var states = _engine.PhysicsStates;
        if (states[0].State == MotionState.Pocketed) return float.PositiveInfinity;

        // Virtual moving cue ball at 1 m/s along the aim direction.
        BallState virtualCue = states[0];
        virtualCue.LinearVelocity = aimDirEngine;

        float minT = float.PositiveInfinity;

        // Ball-ball contacts (skip the cue itself and any pocketed balls).
        for (int i = 1; i < states.Length; i++)
        {
            if (states[i].State == MotionState.Pocketed) continue;
            float t = CollisionDetection.CalculateBallBallImpactTime(in virtualCue, in states[i]);
            if (t < minT) minT = t;
        }

        // Cushion segments.
        var rails = _engine.TableLayout.Rails;
        for (int r = 0; r < rails.Length; r++)
        {
            float t = CollisionDetection.CalculateBallSegmentImpactTime(in virtualCue, in rails[r]);
            if (t > 0f && t < minT) minT = t;
        }

        return minT;
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
    /// Load <see cref="StrokeSensitivity"/> from <c>Game.Cookies</c> if a value was saved in a previous
    /// session. Bounds-clamps the loaded value so a corrupt or out-of-range cookie can't break input.
    /// </summary>
    private void LoadStrokeSensitivityFromCookies()
    {
        if (Game.Cookies is null) return;
        float saved = Game.Cookies.Get(CookieStrokeSensitivity, StrokeSensitivity);
        StrokeSensitivity = MathX.Clamp(saved, 0.005f, 1f);
    }

    /// <summary>Persist the current <see cref="StrokeSensitivity"/> to <c>Game.Cookies</c>.</summary>
    private void SaveStrokeSensitivityToCookies()
    {
        Game.Cookies?.Set(CookieStrokeSensitivity, StrokeSensitivity);
    }

    /// <summary>
    /// Adjust <see cref="StrokeSensitivity"/> multiplicatively (×step on +, ÷step on -), clamped to a
    /// sensible range, and persist to <c>Game.Cookies</c>. With <paramref name="fine"/> = true (Shift
    /// modifier) the smaller <see cref="StrokeSensitivityFineStep"/> is used for precise adjustments.
    /// </summary>
    private void AdjustStrokeSensitivity(bool increase, bool fine)
    {
        float step = MathF.Max(1.001f, fine ? StrokeSensitivityFineStep : StrokeSensitivityStep);
        float factor = increase ? step : 1f / step;
        StrokeSensitivity = MathX.Clamp(StrokeSensitivity * factor, 0.005f, 1f);
        SaveStrokeSensitivityToCookies();
        Log.Info($"{nameof(MatchHost)}: stroke sensitivity → {StrokeSensitivity:0.0000} u/px{(fine ? " (fine)" : "")} (saved).");
    }

    /// <summary>
    /// Build the standard 16-ball 8-ball rack: cue at the head spot, 15 numbered balls in an equilateral
    /// triangle with its apex at the foot spot. Layout (apex toward head rail, base toward foot rail):
    /// <code>
    ///            1
    ///           2 9
    ///          3 8 10
    ///         4 11 12 5
    ///        6 13 7 14 15
    /// </code>
    /// Satisfies the WPA rack rules: 8 in the centre of row 2; back corners (positions 6 and 15) are a
    /// solid/stripe mix; apex is 1.
    /// <para>
    /// Coordinates are in the engine's Y-up frame (X = across table, Y = up, Z = along table length).
    /// Head spot Z = -L/4, foot spot Z = +L/4. Row spacing = √3/2 × ball diameter (equilateral). A tiny
    /// epsilon is added between balls so the CCD doesn't fire instantaneous events on touching contacts.
    /// </para>
    /// </summary>
    private void RackEightBall()
    {
        // Per-ball position in the triangle: (row, columnWithinRow).
        // Row 0 (apex toward head) has 1 column; row 4 (base) has 5.
        var rackPositions = new (int row, int col)[]
        {
            (0, 0),                       // 1 (apex)
            (1, 0), (2, 0), (3, 0),       // 2, 3, 4 (left edge)
            (3, 3), (4, 0), (4, 2),       // 5, 6, 7
            (2, 1),                       // 8 (centre of row 2)
            (1, 1), (2, 2), (3, 1),       // 9, 10, 11
            (3, 2), (4, 1), (4, 3),       // 12, 13, 14
            (4, 4),                       // 15
        };

        float diameter = GameSettings.BallRadius * 2f;
        float epsilon  = 1e-4f;            // ~0.1 mm pad so balls aren't bitwise-touching at rest
        float dx       = diameter + epsilon;
        float dz       = (MathF.Sqrt(3f) * 0.5f) * (diameter + epsilon);
        float footSpotZ = +GameSettings.TableLength * 0.25f;
        float headSpotZ = -GameSettings.TableLength * 0.25f;
        float y         = GameSettings.BallRadius;

        // Cue ball at head spot.
        _engine.AddBall(new SnVec3(0, y, headSpotZ), BallType.Cue);

        // 15 numbered balls. The id returned by AddBall is sequential (1..15), matching the rules
        // convention (1–7 solids, 8 eight, 9–15 stripes).
        for (int idMinus1 = 0; idMinus1 < rackPositions.Length; idMinus1++)
        {
            var (row, col) = rackPositions[idMinus1];
            float x = (col - row * 0.5f) * dx;
            float z = footSpotZ + row * dz;
            _engine.AddBall(new SnVec3(x, y, z), BallType.Normal);
        }
    }

    /// <summary>
    /// Tint for the ball at engine id <paramref name="id"/>. Cue (0) is white; solids (1–7) are saturated
    /// pool-ball colours; the 8-ball is near-black; stripes (9–15) are pastel versions of the matching
    /// solid colours so they read as "the lighter one" without needing a textured surface.
    /// </summary>
    private static Color BallTintForId(int id) => id switch
    {
        0  => Color.White,
        1  => new Color(1.00f, 0.85f, 0.05f),  // yellow
        2  => new Color(0.05f, 0.25f, 0.85f),  // blue
        3  => new Color(0.95f, 0.10f, 0.10f),  // red
        4  => new Color(0.45f, 0.05f, 0.70f),  // purple
        5  => new Color(1.00f, 0.45f, 0.00f),  // orange
        6  => new Color(0.05f, 0.55f, 0.20f),  // green
        7  => new Color(0.55f, 0.05f, 0.10f),  // maroon
        8  => new Color(0.04f, 0.04f, 0.04f),  // black
        9  => new Color(1.00f, 0.90f, 0.55f),  // pale yellow stripe
        10 => new Color(0.45f, 0.65f, 1.00f),  // pale blue stripe
        11 => new Color(1.00f, 0.55f, 0.55f),  // pale red stripe
        12 => new Color(0.75f, 0.50f, 0.95f),  // pale purple stripe
        13 => new Color(1.00f, 0.70f, 0.45f),  // pale orange stripe
        14 => new Color(0.45f, 0.85f, 0.55f),  // pale green stripe
        15 => new Color(0.90f, 0.50f, 0.55f),  // pale maroon stripe
        _  => Color.White,
    };

    /// <summary>
    /// Load built-in s&amp;box physics SoundFiles for impacts / pocket drops / cue strike, then subscribe
    /// to the engine's event stream so each physics event plays an appropriate sound with
    /// speed/force-modulated volume + a small pitch jitter for variety.
    /// <para>
    /// Files used (all under <c>sounds/physics/</c>):
    /// <list type="bullet">
    ///   <item>Ball-ball:  <c>phys-impact-plastic-{1-4}.vsnd</c> — sharp hollow plastic clack, closest to phenolic ball impact</item>
    ///   <item>Cushion:    <c>phys-impact-wood-{1-4}.vsnd</c>    — duller wood thud (rails are wood-framed)</item>
    ///   <item>Cue strike: <c>phys-impact-wood-small-{1-4}.vsnd</c> — short sharp tip-on-ball click</item>
    ///   <item>Pocket:     <c>phys-impact-wood-small-{1-4}.vsnd</c> — softer drop variant (re-uses cue-strike pool)</item>
    /// </list>
    /// </para>
    /// </summary>
    private void LoadSoundsAndHookEvents()
    {
        if (!EnableSound) return;

        _ballHitSounds    = LoadVariants("sounds/physics/phys-impact-plastic-",     4);
        _railHitSounds    = LoadVariants("sounds/physics/phys-impact-wood-",        4);
        _cueStrikeSounds  = LoadVariants("sounds/physics/phys-impact-wood-small-",  4);
        _pocketDropSounds = _cueStrikeSounds;    // smaller wood thuds suit pocket-drop too

        _engine.OnBallContact  += HandleBallContactSound;
        _engine.OnRailContact  += HandleRailContactSound;
        _engine.OnJawContact   += HandleRailContactSound;   // jaws share the wood/cushion sound
        _engine.OnBallPocketed += HandlePocketDropSound;
        _engine.OnCueStrike    += HandleCueStrikeSound;
    }

    /// <summary>Helper: load <paramref name="count"/> consecutively-numbered SoundFiles (1-based).</summary>
    private static SoundFile[] LoadVariants(string pathPrefix, int count)
    {
        var arr = new SoundFile[count];
        for (int i = 0; i < count; i++)
        {
            arr[i] = SoundFile.Load($"{pathPrefix}{i + 1}.vsnd");
        }
        return arr;
    }

    /// <summary>
    /// Play a random variant from <paramref name="pool"/> at <paramref name="worldPos"/> with volume/pitch
    /// derived from <paramref name="speedOrForce"/>. Volume scales linearly with speed up to
    /// <see cref="SoundVolumeReferenceSpeed"/>, clamped to [0.08, 1.0] so even soft contacts are audible.
    /// Pitch is base ± SoundPitchJitter plus a small speed-coupled term (faster = slightly higher).
    /// No-op if the pool is empty/null (failed loads).
    /// </summary>
    private void PlayImpactSound(SoundFile[] pool, Vector3 worldPos, float speedOrForce)
    {
        if (!EnableSound || pool is null || pool.Length == 0) return;

        var snd = pool[_soundRng.Next(pool.Length)];
        if (snd is null) return;

        float normSpeed = MathX.Clamp(speedOrForce / SoundVolumeReferenceSpeed, 0f, 1f);
        float volume    = MathX.Clamp(0.08f + 0.92f * normSpeed, 0.08f, 1f) * SoundMasterVolume;
        float jitter    = ((float)_soundRng.NextDouble() - 0.5f) * 2f * SoundPitchJitter;
        float pitch     = 1f + jitter + (normSpeed - 0.5f) * 0.20f;

        var handle = Sound.PlayFile(snd, volume, pitch, delay: 0f);
        if (handle is not null) handle.Position = worldPos;
    }

    /// <summary>Ball-ball collision: position at midpoint, "speed" = max of either ball's post-collision linear speed.</summary>
    private void HandleBallContactSound(int ballA, int ballB)
    {
        var states = _engine.PhysicsStates;
        if (ballA < 0 || ballA >= states.Length || ballB < 0 || ballB >= states.Length) return;
        SnVec3 midEngine = (states[ballA].Position + states[ballB].Position) * 0.5f;
        float speed = MathF.Max(states[ballA].LinearVelocity.Length(), states[ballB].LinearVelocity.Length());
        PlayImpactSound(_ballHitSounds, Conversions.EngineToWorld(midEngine), speed);
    }

    /// <summary>Cushion / jaw collision: position at the ball, impact speed supplied by the engine event.</summary>
    private void HandleRailContactSound(int ballId, int _, float impactSpeed)
    {
        var states = _engine.PhysicsStates;
        if (ballId < 0 || ballId >= states.Length) return;
        PlayImpactSound(_railHitSounds, Conversions.EngineToWorld(states[ballId].Position), impactSpeed);
    }

    /// <summary>Pocket drop: play at the pocket position the engine reports. Quieter / lower-pitch by reusing the small-wood pool.</summary>
    private void HandlePocketDropSound(int ballId, SnVec3 dropPosEngine)
    {
        // Use ball linear-speed-at-drop as a proxy for "drop intensity". States may already be Pocketed
        // by the time the event fires, but we read the velocity-just-before for a sensible volume.
        var states = _engine.PhysicsStates;
        float speed = (ballId >= 0 && ballId < states.Length) ? states[ballId].LinearVelocity.Length() : 1f;
        PlayImpactSound(_pocketDropSounds, Conversions.EngineToWorld(dropPosEngine), speed * 0.7f);
    }

    /// <summary>Cue strike: position at the cue ball, "speed" = cue impulse force (the only physics-meaningful intensity available here).</summary>
    private void HandleCueStrikeSound(int ballId, SnVec3 _, float force, SnVec3 __)
    {
        var states = _engine.PhysicsStates;
        if (ballId < 0 || ballId >= states.Length) return;
        // Force in N·s; for a 0.18 kg ball, force/mass ≈ initial speed. Map directly to the speed scale.
        float speedProxy = force / GameSettings.BallMass;
        PlayImpactSound(_cueStrikeSounds, Conversions.EngineToWorld(states[ballId].Position), speedProxy);
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

    /// <summary>
    /// Draw the in-play HUD: current player + group, last shot, ball-in-hand flag, and a stroke-power bar
    /// at the bottom of the screen while the player is actively pulling back. Uses
    /// <see cref="Component.DebugOverlay"/> screen-space primitives — no Razor template, no extra
    /// GameObjects, no asset wiring required for a first-light heads-up display.
    /// </summary>
    private void DrawHud()
    {
        if (_rules is null) return;

        var overlay = DebugOverlay;
        if (overlay is null) return;

        // ---- Top-left: player + group info ----
        string p1Group = _rules.Player1Group?.ToString() ?? "(unassigned)";
        string p2Group = _rules.Player2Group?.ToString() ?? "(unassigned)";
        string p1Label = _rules.CurrentPlayer == 1 ? "> P1" : "  P1";
        string p2Label = _rules.CurrentPlayer == 2 ? "> P2" : "  P2";

        var hudColour = Color.White;
        overlay.ScreenText(new Vector2(20, 20),  $"{p1Label}  -  {p1Group}",                   size: 18, color: hudColour);
        overlay.ScreenText(new Vector2(20, 44),  $"{p2Label}  -  {p2Group}",                   size: 18, color: hudColour);
        overlay.ScreenText(new Vector2(20, 76),  $"Open table: {_rules.OpenTable}",            size: 14, color: hudColour);
        overlay.ScreenText(new Vector2(20, 96),  $"Shot {_shotNumber}: {LastShotDescription}", size: 14, color: hudColour);

        if (_rules.GameOver)
        {
            overlay.ScreenText(new Vector2(20, 124), $"GAME OVER  -  P{_rules.Winner} wins ({_rules.WinReason})", size: 20, color: new Color(1f, 0.9f, 0.3f));
        }

        // ---- Top-right: input mode hints ----
        float w = Screen.Width;
        overlay.ScreenText(new Vector2(w - 280, 20),  "LMB / S    pull back, push forward", size: 14, color: hudColour);
        overlay.ScreenText(new Vector2(w - 280, 40),  "E + mouse   english (cue contact)",  size: 14, color: hudColour);
        overlay.ScreenText(new Vector2(w - 280, 60),  "B + mouse   butt elevation",          size: 14, color: hudColour);
        overlay.ScreenText(new Vector2(w - 280, 80),  "RMB         overhead view",           size: 14, color: hudColour);
        overlay.ScreenText(new Vector2(w - 280, 100), "R           replay last shot",        size: 14, color: hudColour);
        overlay.ScreenText(new Vector2(w - 280, 120), $"+ / -       sensitivity {StrokeSensitivity:0.0000}  (hold Shift for fine)", size: 14, color: new Color(0.85f, 0.85f, 0.55f));

        // ---- Bottom-centre: power readouts ----
        // While stroking: two readouts side-by-side — "draw" (how far we've pulled back) on the left,
        // "push" (live forward speed as % of MaxStrikeSpeedMPS, capped by PowerLimitPercent) on the right.
        // After a shot: the actual % used at impact (until the next stroke begins).
        if (_stroking && _peakDrawbackUnits > CueDrawbackUnits + 0.1f)
        {
            float h = Screen.Height;

            float drawFraction = MathX.Clamp(
                (_peakDrawbackUnits - CueDrawbackUnits) / MathF.Max(1e-3f, MaxDrawbackUnits - CueDrawbackUnits),
                0f, 1f);
            int drawBlocks = (int)MathF.Round(drawFraction * 16f);
            string drawBar = new string('|', drawBlocks).PadRight(16, '.');

            float pushVelMPS    = MathF.Max(0f, _pushVelocityUnitsPerSec) / Conversions.UnitsPerMetre;
            float maxAllowedMPS = MaxStrikeSpeedMPS * (PowerLimitPercent * 0.01f);
            float pushFraction  = MathX.Clamp(pushVelMPS / MathF.Max(0.01f, MaxStrikeSpeedMPS), 0f, 1f);
            float capFraction   = PowerLimitPercent * 0.01f;
            int pushBlocks      = (int)MathF.Round(pushFraction * 16f);
            int capBlocks       = (int)MathF.Round(capFraction * 16f);
            // Show the cap as '|' filled blocks where the live push velocity falls, '·' for empty bar slots
            // up to the cap, and '-' for the locked-out range above the cap.
            var pushBar = new System.Text.StringBuilder(16);
            for (int i = 0; i < 16; i++)
            {
                if (i < pushBlocks) pushBar.Append('|');
                else if (i < capBlocks) pushBar.Append('.');
                else pushBar.Append('-');
            }
            int pushPct = (int)MathF.Round(MathF.Min(pushVelMPS, maxAllowedMPS) / MathF.Max(0.01f, MaxStrikeSpeedMPS) * 100f);

            overlay.ScreenText(new Vector2(w * 0.5f - 220f, h - 50f), $"draw [{drawBar}]",                size: 18, color: new Color(0.85f, 0.85f, 0.85f));
            overlay.ScreenText(new Vector2(w * 0.5f + 10f,  h - 50f), $"push [{pushBar}] {pushPct,3}%",   size: 18, color: new Color(1f, 0.55f, 0.15f));
        }
        else if (_lastStrikePowerPercent > 0.1f && !_shotInFlight)
        {
            float h = Screen.Height;
            overlay.ScreenText(new Vector2(w * 0.5f - 90f, h - 50f),
                $"last strike: {_lastStrikePowerPercent:0.}%  ({_lastStrikePowerPercent * MaxStrikeSpeedMPS * 0.01f:0.0} m/s)",
                size: 14, color: new Color(0.7f, 0.7f, 0.7f));
        }
    }

    /// <summary>Mirror engine state to GameObject transforms every frame for smooth visuals.</summary>
    protected override void OnUpdate()
    {
        if (_engine is null) return;

        // ---- Read input modes (mutually exclusive on mouse) ----
        bool overheadView  = Input.Down("Attack2");                                   // RMB held → overhead camera
        bool strokeButton  = Input.Down("Attack1") || Input.Keyboard.Down("s");       // LMB or S held → stroke
        bool strokeStart   = Input.Pressed("Attack1") || Input.Keyboard.Pressed("s");
        bool strokeEnd     = Input.Released("Attack1") || Input.Keyboard.Released("s");
        bool englishMode   = Input.Keyboard.Down("e");                                // E held → adjust cue-ball contact point
        bool elevationMode = Input.Keyboard.Down("b");                                // B held → tilt cue butt up
        bool replayPressed = Input.Keyboard.Pressed("r");                             // R key → replay last shot

        var look = Input.AnalogLook;                                                  // (pitch, yaw, roll) in degrees this frame

        if (replayPressed)
        {
            Replay();
        }

        // -- Sensitivity adjust (+ / -). Persists to Game.Cookies on every change. --
        // The '+' key requires shift on most keyboards, so we accept '=' as the bare-key alternative.
        // Holding Shift uses the finer step for precise tuning at low sensitivities.
        bool shiftHeld = Input.Keyboard.Down("shift") || Input.Keyboard.Down("lshift") || Input.Keyboard.Down("rshift");
        if (Input.Keyboard.Pressed("=") || Input.Keyboard.Pressed("+"))   AdjustStrokeSensitivity(increase: true,  fine: shiftHeld);
        if (Input.Keyboard.Pressed("-"))                                   AdjustStrokeSensitivity(increase: false, fine: shiftHeld);

        // ---- Mode-driven input handling. Priority: stroke > english > elevation > free aim. ----
        if (_shotInFlight)
        {
            // Locked: balls are moving. No aim/stroke updates. Cue is hidden inside UpdateAimRig.
        }
        else if (strokeButton)
        {
            // Raw mouse delta in pixels — gives a 1:1 feel for the stroke gesture. AnalogLook would
            // go through s&box's camera-sensitivity filter, which adds lag and shrinks the response.
            HandleStrokeWhileHeld(Input.MouseDelta);
        }
        else if (englishMode)
        {
            // Mouse → cue contact point on the ball, in fractions of ball radius.
            //  - Mouse right (yaw+)        → tip moves right on ball → _englishH +=
            //  - Mouse up    (pitch < 0)   → tip moves up on ball    → _englishV +=
            _englishH = MathX.Clamp(_englishH + look.yaw   * EnglishSensitivity, -0.5f, 0.5f);
            _englishV = MathX.Clamp(_englishV - look.pitch * EnglishSensitivity, -0.5f, 0.5f);
        }
        else if (elevationMode)
        {
            // Mouse pitch → cue butt elevation. Mouse down (pitch+) raises the butt (steeper massé).
            _buttElevDeg = MathX.Clamp(_buttElevDeg + look.pitch * ElevationSensitivity, 0f, MaxElevationDeg);
        }
        else
        {
            // Free aim. Mouse drives yaw + camera pitch.
            _aimYawDeg += look.yaw * MouseSensitivity;
            _aimPitchDeg = MathX.Clamp(_aimPitchDeg + look.pitch * MouseSensitivity, MinPitchDeg, MaxPitchDeg);
            _aimYawDeg = ((_aimYawDeg + 180f) % 360f + 360f) % 360f - 180f;           // normalise to (-180, 180]
        }

        if (strokeStart && !_shotInFlight)
        {
            BeginStroke();
        }
        if (strokeEnd && _stroking)
        {
            EndStroke();
        }

        UpdateAimRig(overheadView);
        DrawHud();

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
    /// <see cref="_aimPitchDeg"/>, the cue ball's current world position, and the active drawback.
    /// Idempotent — safe to call from <see cref="OnStart"/> for first-frame placement and from
    /// <see cref="OnUpdate"/> thereafter.
    /// </summary>
    /// <param name="overheadView">If true, the camera switches to a top-down framing instead of the over-the-shoulder orbit.</param>
    private void UpdateAimRig(bool overheadView = false)
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

        // -------- Camera --------
        if (ControlCamera && ViewCamera.IsValid())
        {
            Vector3 camPos;
            Vector3 lookAt;
            if (overheadView)
            {
                // Top-down view above the table center. Yaw still tracks aim so the player's reference
                // "forward" stays consistent (cue ball is always visually above the centre of the screen).
                camPos = new Vector3(0, 0, OverheadHeightUnits);
                lookAt = Vector3.Zero;
                // Look straight down; yaw the camera so the aim direction points up in the view.
                ViewCamera.GameObject.WorldPosition = camPos;
                ViewCamera.GameObject.WorldRotation = Rotation.From(new Angles(90f, _aimYawDeg, 0f));
            }
            else
            {
                float horiz = CameraDistance * MathF.Cos(pitchRad);
                float vert  = CameraDistance * MathF.Sin(pitchRad);
                camPos = cueBallWorld - aimDirWorld * horiz + new Vector3(0, 0, vert);
                ViewCamera.GameObject.WorldPosition = camPos;
                ViewCamera.GameObject.WorldRotation = Rotation.LookAt((cueBallWorld - camPos).Normal);
            }
        }

        // -------- Aim line: cue ball → predicted first contact --------
        if (_aimLineGo.IsValid())
        {
            bool aimLineVisible = ShowAimLine && !_shotInFlight;
            _aimLineGo.Enabled = aimLineVisible;

            if (aimLineVisible)
            {
                // Build the engine-space aim direction the same way Strike() does (table-plane horizontal,
                // ignoring butt elevation — the aim line shows where the cue ball will TRAVEL on the cloth).
                SnVec3 horizAimEngine = Conversions.WorldToEngine(aimDirWorld);
                horizAimEngine.Y = 0f;
                if (horizAimEngine.LengthSquared() > 1e-6f) horizAimEngine = SnVec3.Normalize(horizAimEngine);
                else horizAimEngine = new SnVec3(0, 0, 1);

                float distMetres = PredictFirstContactDistanceMetres(horizAimEngine);
                float distUnits;
                if (!float.IsFinite(distMetres) || distMetres < 0.01f)
                {
                    distUnits = AimLineMaxLengthUnits;
                }
                else
                {
                    distUnits = MathF.Min(Conversions.MetresToUnits(distMetres), AimLineMaxLengthUnits);
                }

                _aimLineGo.LocalScale  = new Vector3(distUnits, 1f, 1f);
                // Centre of the line sits halfway between the cue ball and the predicted contact, lifted
                // just barely off the slate so the strip doesn't z-fight the cloth.
                Vector3 lineCentre = cueBallWorld + aimDirWorld * (distUnits * 0.5f);
                lineCentre.z = AimLineHeightUnits * 0.5f + 0.01f;       // hover just above slate top (Z=0)
                _aimLineGo.WorldPosition = lineCentre;
                _aimLineGo.WorldRotation = Rotation.LookAt(aimDirWorld);
            }
        }

        // -------- Cue stick --------
        if (_cueStickGo.IsValid())
        {
            _cueStickGo.Enabled = !_shotInFlight;             // hide while balls are moving

            if (!_shotInFlight)
            {
                // Drawback: at rest = CueDrawbackUnits; during a stroke = _currentDrawbackUnits.
                float drawback = _stroking ? _currentDrawbackUnits : CueDrawbackUnits;

                // Elevation: tilt the cue so its length axis points (aimDir * cos(elev) - up * sin(elev)),
                // i.e. forward-and-down. Butt then ends up higher than tip; tip is closer to the ball.
                float elevRad   = _buttElevDeg * (MathF.PI / 180f);
                float cosE      = MathF.Cos(elevRad);
                float sinE      = MathF.Sin(elevRad);
                Vector3 cueAxis = (aimDirWorld * cosE - Vector3.Up * sinE).Normal;

                // English: contact-point offset on the cue ball, expressed as fractions of ball radius.
                // Perpendicular axes around the aim direction: right = aim × up (in XY plane), up = world up.
                float ballRadiusUnits = Conversions.MetresToUnits(GameSettings.BallRadius);
                Vector3 worldRight    = Vector3.Cross(aimDirWorld, Vector3.Up).Normal;
                Vector3 tipOffset     = (worldRight * _englishH + Vector3.Up * _englishV) * ballRadiusUnits;

                // Tip target: at the cue ball, displaced by the english offset, then pulled back by 'drawback'
                // along the cue's local axis (so the cue length stays sensible regardless of elevation).
                Vector3 tipTarget = cueBallWorld + tipOffset - cueAxis * drawback;
                Vector3 cueCenter = tipTarget - cueAxis * (CueLengthUnits * 0.5f);

                _cueStickGo.WorldPosition = cueCenter;
                _cueStickGo.WorldRotation = Rotation.LookAt(cueAxis);
            }
        }
    }

    /// <summary>Begin a new stroke gesture. Captures the current drawback baseline and resets the peak tracker.</summary>
    private void BeginStroke()
    {
        _stroking                = true;
        _strokeFired             = false;
        _currentDrawbackUnits    = CueDrawbackUnits;
        _peakDrawbackUnits       = CueDrawbackUnits;
        _pushVelocityUnitsPerSec = 0f;
    }

    /// <summary>
    /// While the stroke button is held: integrate raw mouse-Y motion (pixels) directly into
    /// <see cref="_currentDrawbackUnits"/>. Positive <paramref name="mouseDelta"/>.y = mouse moved down
    /// = cue drawn back; negative = mouse moved up = cue strokes forward. Drawback is clamped to
    /// [0, <see cref="MaxDrawbackUnits"/>].
    /// <para>
    /// Using <see cref="Input.MouseDelta"/> (pixels) rather than <see cref="Input.AnalogLook"/> (camera
    /// degrees) gives a true 1:1 mouse feel — the cue tip moves the same amount each pixel of mouse
    /// motion regardless of s&amp;box's camera sensitivity settings.
    /// </para>
    /// <para>
    /// Fire condition (Shooters-Pool feel): the shot commits during the FORWARD STROKE the instant the cue
    /// tip reaches the cue ball — i.e. when <see cref="_currentDrawbackUnits"/> drops back to (or below) the
    /// rest position <see cref="CueDrawbackUnits"/>, provided the user actually pulled back past
    /// <see cref="StrokeFireThresholdUnits"/> first. Releasing the button without completing the push cancels
    /// the stroke.
    /// </para>
    /// </summary>
    private void HandleStrokeWhileHeld(Vector2 mouseDelta)
    {
        if (_strokeFired) return;                                // already committed this stroke; wait for button release to start a new one

        float dt = Time.Delta;
        if (dt < 1e-5f) return;                                  // skip degenerate frames

        float drawbackDeltaUnits = mouseDelta.y * StrokeSensitivity;
        _currentDrawbackUnits = MathX.Clamp(_currentDrawbackUnits + drawbackDeltaUnits, 0f, MaxDrawbackUnits);
        if (_currentDrawbackUnits > _peakDrawbackUnits) _peakDrawbackUnits = _currentDrawbackUnits;

        // Track cue forward velocity (units/sec). Drawback DECREASING = pushing forward → positive velocity.
        // EMA-smooth a couple of frames so a single noisy dt or mouseDelta doesn't dominate the impact reading.
        float pushVelThisFrame = -drawbackDeltaUnits / dt;
        const float ema = 0.55f;
        _pushVelocityUnitsPerSec = MathX.Lerp(_pushVelocityUnitsPerSec, pushVelThisFrame, ema);

        // Fire the moment the cue tip touches the ball during a forward stroke.
        bool didPullBack    = _peakDrawbackUnits > CueDrawbackUnits + StrokeFireThresholdUnits;
        bool tipAtBall      = _currentDrawbackUnits <= CueDrawbackUnits;
        bool pushingForward = mouseDelta.y < 0f;
        if (didPullBack && tipAtBall && pushingForward)
        {
            // Force from velocity at impact: f = m·v (impulse). Convert units/sec → m/s first, then cap.
            float velMPS         = MathF.Max(0f, _pushVelocityUnitsPerSec) / Conversions.UnitsPerMetre;
            float maxAllowedMPS  = MaxStrikeSpeedMPS * (PowerLimitPercent * 0.01f);
            float clampedMPS     = MathF.Min(velMPS, maxAllowedMPS);
            float force          = clampedMPS * GameSettings.BallMass;

            _lastStrikePowerPercent = (clampedMPS / MathF.Max(0.01f, MaxStrikeSpeedMPS)) * 100f;

            Strike(force);
            _strokeFired          = true;
            _currentDrawbackUnits = CueDrawbackUnits;            // park visual at rest; cue is hidden during shot anyway
        }
    }

    /// <summary>
    /// Stroke button released. Pure reset — the shot itself already fired (if it was going to) the moment
    /// the cue tip reached the ball during the push. Pull-and-release without completing the forward stroke
    /// is treated as a cancel, in line with the Shooters-Pool feel.
    /// </summary>
    private void EndStroke()
    {
        _stroking             = false;
        _strokeFired          = false;
        _currentDrawbackUnits = CueDrawbackUnits;
        _peakDrawbackUnits    = CueDrawbackUnits;
    }

    /// <summary>
    /// Restore ball positions to the state captured immediately before the most recent <see cref="Strike"/>.
    /// Rules state is intentionally NOT rolled back — replay is for re-trying the same shot setup, not for
    /// rewinding game history. Idempotent: no-op if there's no captured snapshot.
    /// </summary>
    public void Replay()
    {
        if (_replaySnapshot is null) return;
        if (_shotInFlight)
        {
            // Mid-shot: cancel the in-flight shot before rolling back, otherwise FinishShot would
            // resolve against the restored (pre-shot) state, which is nonsense.
            _recorder?.Dispose();
            _recorder = null;
            _shotInFlight = false;
        }
        _engine.RestoreState(_replaySnapshot);
        Log.Info($"Replay: restored {_replaySnapshot.Length} balls to pre-shot positions.");
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

        // --- 4 corner legs. Boxes hanging below the slate at each table corner. ---
        // Inset slightly so they tuck under the rails rather than poking out past the table boundary.
        float widthHalf  = Conversions.MetresToUnits(def.Width)  * 0.5f;
        float lengthHalf = Conversions.MetresToUnits(def.Length) * 0.5f;
        float legHalfX   = LegWidthUnits * 0.5f;
        float legHalfY   = LegWidthUnits * 0.5f;
        float legHalfZ   = LegHeightUnits * 0.5f;
        // Top of leg flush with bottom of slate (slate's bottom face is at z = -SlateThicknessUnits).
        float legCentreZ = -SlateThicknessUnits - legHalfZ;

        var legCornerOffsets = new (float sx, float sy)[]
        {
            (-1, -1), (+1, -1), (-1, +1), (+1, +1),
        };
        for (int i = 0; i < legCornerOffsets.Length; i++)
        {
            var (sx, sy) = legCornerOffsets[i];
            var legModel = ProceduralMeshes.BuildBox(new Vector3(legHalfX, legHalfY, legHalfZ), material);
            var leg = Scene.CreateObject(true);
            leg.Name = $"Leg_{i}";
            leg.WorldPosition = new Vector3(
                sx * (lengthHalf - LegInsetUnits - legHalfX),
                sy * (widthHalf  - LegInsetUnits - legHalfY),
                legCentreZ);
            var mr = leg.AddComponent<ModelRenderer>();
            mr.Model = legModel;
            mr.Tint  = LegColor;
            _tableObjects.Add(leg);
        }

        Log.Info($"{nameof(MatchHost)}.BuildTableVisuals: spawned {_tableObjects.Count} procedurally-built table GameObjects.");
    }

    /// <summary>
    /// Strike with the default <see cref="StrikeForce"/>. Convenience wrapper used by the AutoStrikeOnStart
    /// path; the stroke-gesture flow calls <see cref="Strike(float)"/> directly with a peak-drawback-derived force.
    /// </summary>
    public void Strike() => Strike(StrikeForce);

    /// <summary>
    /// Strikes the cue ball along the current aim direction with the supplied force. Wraps the strike with
    /// a fresh <see cref="ShotRecorder"/> + <see cref="EightBallRules.OnShotStart"/> so the rules observer
    /// sees the per-shot lifecycle. Captures a ball-state snapshot for <see cref="Replay"/>. Ignored if a
    /// shot is already in flight or the game's over.
    /// <para>
    /// The aim direction comes from <see cref="_aimYawDeg"/> (set by mouse look in <see cref="OnUpdate"/>).
    /// We project to s&amp;box's XY plane, convert to engine coords via <see cref="Conversions.WorldToEngine"/>,
    /// and zero out any vertical component so the cue always strikes parallel to the table (no jump-shot
    /// energy unless butt elevation gets wired up later).
    /// </para>
    /// </summary>
    /// <param name="force">Cue impulse magnitude (N·s). Force / Ball.Mass = initial cue-ball speed.</param>
    public void Strike(float force)
    {
        // EightBallRules integration: per-shot lifecycle via ShotRecorder.
        if (_engine is null || _rules is null) return;
        if (_shotInFlight) return;             // already simulating a shot
        if (_rules.GameOver) return;           // game's done

        // Capture pre-strike snapshot for Replay before anything mutates the engine.
        _replaySnapshot = _engine.SnapshotState();

        _shotNumber++;
        _recorder = new ShotRecorder(_engine);
        _rules.OnShotStart(new ShotContext
        {
            ShotNumber = _shotNumber,
            StateAtStart = _replaySnapshot,
        });

        // World aim direction in s&box's XY plane, derived from current aim yaw.
        float yawRad = _aimYawDeg * (MathF.PI / 180f);
        Vector3 aimDirWorld = new Vector3(MathF.Cos(yawRad), MathF.Sin(yawRad), 0f);

        // Convert horizontal aim to engine coords (Y-up). Vertical component stripped, normalised.
        SnVec3 horizAimEngine = Conversions.WorldToEngine(aimDirWorld);
        horizAimEngine.Y = 0f;
        if (horizAimEngine.LengthSquared() > 1e-6f) horizAimEngine = SnVec3.Normalize(horizAimEngine);
        else horizAimEngine = new SnVec3(0, 0, 1);            // degenerate-aim fallback

        // Apply elevation: tip down, butt up. Engine +Y = up, so the cue strikes the ball travelling
        // (horizontalDir * cos(elev)) + (-Y * sin(elev)) — forward and downward.
        float elevRad = _buttElevDeg * (MathF.PI / 180f);
        SnVec3 aimDirEngine = horizAimEngine * MathF.Cos(elevRad) + new SnVec3(0, -MathF.Sin(elevRad), 0);
        aimDirEngine = SnVec3.Normalize(aimDirEngine);

        // English → hitOffset in engine local coords (relative to ball centre, in metres). The contact
        // point on the cue ball is (englishH * right + englishV * up) × BallRadius. "right" is perpendicular
        // to the horizontal aim, in the table plane.
        //
        // Cross-product order matters because the engine is Y-up while s&box is Z-up. In s&box, "right"
        // of forward is `forward × up`; in the engine's Y-up frame, "right" is `up × forward` (otherwise
        // we'd pick up the opposite-handed perpendicular and english H would be inverted between the
        // visual cue and the physics hitOffset). Verified: aim_engine=(0,0,1), up=(0,1,0) →
        // up × aim = (+1, 0, 0) = engine +X = right (matches GameSettings convention).
        SnVec3 engineUp     = new SnVec3(0, 1, 0);
        SnVec3 engineRight  = SnVec3.Normalize(SnVec3.Cross(engineUp, horizAimEngine));
        SnVec3 hitOffset    = (engineRight * _englishH + engineUp * _englishV) * GameSettings.BallRadius;

        _engine.StrikeCueBall(
            id: 0,
            aimDirection: aimDirEngine,
            force: force,
            hitOffset: hitOffset);

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

        // Scratch respawn: if the cue ball got pocketed this shot, drop it back on the head spot so the
        // next player can take their ball-in-hand shot. EightBallRules already flagged the foul; this
        // just restores the cue ball physically so play can continue.
        if (_engine.PhysicsStates.Length > 0 && _engine.PhysicsStates[0].State == MotionState.Pocketed)
        {
            float headSpotZ = -GameSettings.TableLength * 0.25f;
            _engine.RespawnBall(0, new SnVec3(0, GameSettings.BallRadius, headSpotZ));
            if (_ballObjects.Count > 0 && _ballObjects[0].IsValid())
            {
                _ballObjects[0].Enabled = true;                  // re-show the cue ball GameObject (was disabled when pocketed)
            }
            Log.Info($"{nameof(MatchHost)}: cue ball scratched - respawned at head spot.");
        }

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
