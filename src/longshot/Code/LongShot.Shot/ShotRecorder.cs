using System;
using System.Collections.Generic;
using SnVector3 = System.Numerics.Vector3;
using SnVector2 = System.Numerics.Vector2;
using LongShot.Engine;

namespace LongShot.Shot;

/// <summary>
/// Subscribes to a <see cref="BilliardsEngine"/>'s event stream for the duration of one
/// shot and produces a <see cref="ShotSummary"/> when <see cref="Finalize"/> is called.
///
/// Lifecycle:
/// <code>
///   var rec = new ShotRecorder(engine);  // attaches event handlers
///   engine.StrikeCueBall(...);            // captured by recorder
///   while (!engine.AreAllBallsAsleep()) engine.Tick(dt);
///   var summary = rec.Finalize();        // detaches and returns the data
/// </code>
/// </summary>
public sealed class ShotRecorder : IDisposable
{
    private readonly BilliardsEngine _engine;
    private readonly int _cueBallId;

    private readonly List<BallContactEvent> _ballContacts = new();
    private readonly List<RailContactEvent> _railContacts = new();
    private readonly List<JawContactEvent> _jawContacts = new();
    private readonly List<PocketingEvent> _pocketings = new();

    private CueStrikeData _strike;
    private BallState[] _stateAtStrike;
    private float _strikeStartSimTime;

    private bool _cueAirborneEverObserved;
    private float _cueMaxHeight;
    private float _cueMaxSpeed;
    private float _cueTotalDistance;
    private SnVector3 _cuePrevPosition;
    private SnVector3 _cueLaunchHeading;     // horizontal heading right after the strike
    private bool _disposed;

    public ShotRecorder(BilliardsEngine engine, int cueBallId = 0)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _cueBallId = cueBallId;

        _engine.OnCueStrike += HandleCueStrike;
        _engine.OnBallContact += HandleBallContact;
        _engine.OnRailContact += HandleRailContact;
        _engine.OnJawContact += HandleJawContact;
        _engine.OnBallPocketed += HandlePocketed;
    }

    /// <summary>
    /// Call once per outer simulation step (after <see cref="BilliardsEngine.Tick"/>) so
    /// the recorder can sample ball-state-derived metrics (peak speed, max height, etc.).
    /// </summary>
    public void Sample()
    {
        if (_engine.ActiveBallCount <= _cueBallId) return;
        var cue = _engine.PhysicsStates[_cueBallId];
        if (cue.State == MotionState.Pocketed) return;

        float spd = cue.LinearVelocity.Length();
        if (spd > _cueMaxSpeed) _cueMaxSpeed = spd;

        float height = cue.Position.Y - GameSettings.BallRadius;
        if (height > _cueMaxHeight) _cueMaxHeight = height;

        if (cue.State == MotionState.Airborne) _cueAirborneEverObserved = true;

        _cueTotalDistance += SnVector3.Distance(cue.Position, _cuePrevPosition);
        _cuePrevPosition = cue.Position;
    }

    public ShotSummary Finalize()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ShotRecorder));

        // First non-cue ball touched by the cue ball, if any.
        int firstContactId = -1;
        float firstContactTime = -1f;
        foreach (var ev in _ballContacts)
        {
            int other = ev.BallA == _cueBallId ? ev.BallB : (ev.BallB == _cueBallId ? ev.BallA : -1);
            if (other >= 0)
            {
                firstContactId = other;
                firstContactTime = ev.Time;
                break;
            }
        }

        bool cuePocketed = false;
        foreach (var p in _pocketings)
        {
            if (p.BallId == _cueBallId) { cuePocketed = true; break; }
        }

        int cueRailBounces = 0;
        foreach (var r in _railContacts) if (r.BallId == _cueBallId) cueRailBounces++;

        // Massé heuristic: high elevation + the cue ball's horizontal heading swung sharply.
        bool wasMasse = false;
        if (_strike != null && _strike.ElevationDegrees > 45f && _cueLaunchHeading.LengthSquared() > 1e-4f)
        {
            var stateNow = _engine.PhysicsStates[_cueBallId];
            var finalHeading = new SnVector2(
                stateNow.Position.X - _stateAtStrike[_cueBallId].Position.X,
                stateNow.Position.Z - _stateAtStrike[_cueBallId].Position.Z);
            if (finalHeading.LengthSquared() > 1e-4f)
            {
                var launchHorizon = new SnVector2(_cueLaunchHeading.X, _cueLaunchHeading.Z);
                float cos = SnVector2.Dot(SnVector2.Normalize(launchHorizon), SnVector2.Normalize(finalHeading));
                cos = MathF.Max(-1f, MathF.Min(1f, cos));
                float angleDeg = MathF.Acos(cos) * (180f / MathF.PI);
                wasMasse = angleDeg > 25f;
            }
        }

        bool wasJumpShot = _cueAirborneEverObserved
            && _strike != null
            && _strike.ElevationDegrees > 15f;

        return new ShotSummary
        {
            Strike = _strike,
            StateAtStrike = _stateAtStrike,
            StateAtRest = _engine.SnapshotState(),
            TotalSimulatedTime = _engine.TotalSimulatedTime - _strikeStartSimTime,
            AllBallsAtRest = _engine.AreAllBallsAsleep(),

            BallContacts = _ballContacts.AsReadOnly(),
            RailContacts = _railContacts.AsReadOnly(),
            JawContacts = _jawContacts.AsReadOnly(),
            Pocketings = _pocketings.AsReadOnly(),

            FirstContactBallId = firstContactId,
            FirstContactTime = firstContactTime,
            CueBallPocketed = cuePocketed,
            CueRailBounceCount = cueRailBounces,
            CueMaxAirborneHeight = _cueMaxHeight,
            CueWentAirborne = _cueAirborneEverObserved,
            CueTotalDistance = _cueTotalDistance,
            CueMaxSpeed = _cueMaxSpeed,
            WasJumpShot = wasJumpShot,
            WasMasse = wasMasse,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _engine.OnCueStrike -= HandleCueStrike;
        _engine.OnBallContact -= HandleBallContact;
        _engine.OnRailContact -= HandleRailContact;
        _engine.OnJawContact -= HandleJawContact;
        _engine.OnBallPocketed -= HandlePocketed;
        _disposed = true;
    }

    // ---- Event handlers ----

    private void HandleCueStrike(int id, SnVector3 aim, float force, SnVector3 offset)
    {
        if (id != _cueBallId) return;
        var aimNorm = aim.LengthSquared() > 1e-8f ? SnVector3.Normalize(aim) : SnVector3.Zero;
        _strike = new CueStrikeData
        {
            CueBallId = id,
            AimRaw = aim,
            AimNormalized = aimNorm,
            Force = force,
            HitOffset = offset,
        };
        _stateAtStrike = _engine.SnapshotState();
        _strikeStartSimTime = _engine.TotalSimulatedTime;
        _cuePrevPosition = _engine.PhysicsStates[_cueBallId].Position;
        _cueLaunchHeading = _engine.PhysicsStates[_cueBallId].LinearVelocity;
    }

    private void HandleBallContact(int a, int b) =>
        _ballContacts.Add(new BallContactEvent(a, b, _engine.TotalSimulatedTime));

    private void HandleRailContact(int id, int railIndex, float speed) =>
        _railContacts.Add(new RailContactEvent(id, railIndex, speed, _engine.TotalSimulatedTime));

    private void HandleJawContact(int id, int jawIndex, float speed) =>
        _jawContacts.Add(new JawContactEvent(id, jawIndex, speed, _engine.TotalSimulatedTime));

    private void HandlePocketed(int id, SnVector3 dropPos) =>
        _pocketings.Add(new PocketingEvent(id, dropPos, _engine.TotalSimulatedTime));
}
