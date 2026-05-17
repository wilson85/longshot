# LongShot - Project Guide for Claude

LongShot is a realistic billiards simulator built on **s&box** (Source 2 / Sandbox C# API, .NET 10). The physics engine is decoupled into its own portable library; the s&box host pulls it in as inlined source. A headless bench (`LongShot.Bench`) and a TorchSharp training pipeline (`LongShot.Train`) consume the same engine for fast iteration outside the editor.

Earlier prototypes on Evergine and a hand-rolled D3D12/Vortice POC have been removed (2026-05) - the engine kept all the useful physics work; the host layers were starter-template scaffolding.

## Repository layout

```
src/
  longshot/                              # s&box host project (.NET 10, Source 2). Canonical home of ALL game C# source.
    longshot.sbproj                      # game manifest (Singleplayer, TickRate 50)
    Code/longshot.csproj                 # editor-regenerated; never add ProjectReferences here
    Code/Conversions.cs                  # the seam: System.Numerics.Vector3 <-> Sandbox.Vector3 + Y/Z-up swap
    Code/Match/MatchHost.cs              # Component subclass that owns the BilliardsEngine instance per scene
    Code/LongShot.Engine/                # portable billiards simulation core (System.Numerics, no host deps)
      BilliardsEngine.cs                 # event-driven scheduler + ball state + fixed-timestep accumulator
      TablePhysics.cs                    # cushion / jaw / ball-ball resolvers + cloth friction
      CollisionDetection.cs              # predictive (TOI) impact-time queries
      PhysicsMath.cs                     # inertia, surface velocity, dynamic restitution, impulse application
      PhysicsConfig.cs                   # all tunables (env, ball, cushion, cloth, cue)
      GameSettings.cs                    # WPA table constants, fixed-step / accumulator caps
      EngineTypes.cs                     # MotionState, EventType, PhysicsEvent, BallType
      BallState.cs                       # ball physics state (Position, LinearVelocity, AngularVelocity, State)
      TableLayout.cs                     # rails + pockets + jaw corners (consumed by the engine)
      CushionSegment.cs, PocketBeam.cs
      TableDefinition.cs                 # pure data: rails + pockets + jaw specs (WPA factory)
      TableBuilder.cs                    # turns TableDefinition into engine collision primitives
      MathExtensions.cs                  # Y-up <-> Z-up axis swap helpers (the seam to s&box's Z-up world)
    Code/LongShot.Shot/                  # per-shot event recording (consumes engine events)
      ShotEvents.cs                      # CueStrikeData + Ball/Rail/Jaw/PocketingEvent records
      ShotSummary.cs                     # the data every observer reads
      IShotObserver.cs                   # the single extension point
      ShotRecorder.cs                    # subscribes to engine events, builds ShotSummary
    Code/LongShot.Rules/                 # concrete IShotObserver implementations
      SimpleScoring.cs                   # baseline scorer: +1 per pot, -1 for scratch
      TrickShotBonuses.cs                # "wow points": rail-kill, jump-pot, massé, combination
      BallGroup.cs                       # 8-ball group assignment (Cue/Solid/Eight/Stripe)
      EightBallShotResult.cs             # structured outcome of one 8-ball shot
      EightBallRules.cs                  # standard 8-ball: groups, fouls, win/loss, turn change
    Editor/longshot.editor.csproj        # editor-side tooling (also editor-regenerated)
  LongShot.Engine/LongShot.Engine.csproj # thin wrapper: pulls Code/LongShot.Engine/*.cs back for Bench / Train
  LongShot.Shot/LongShot.Shot.csproj     # same pattern for Shot
  LongShot.Rules/LongShot.Rules.csproj   # same pattern for Rules
  LongShot.Bench/                        # headless scenario runner (System.Numerics + SkiaSharp)
    Program.cs, Scenario.cs, Trajectory.cs, BenchRenderer.cs
    Scenarios/                           # one file per shot scenario; see README.md
    out/                                 # generated PNG diagnostics (git-ignored or per taste)
  LongShot.Train/                        # ML pipeline (TorchSharp); references LongShot.Engine
    Program.cs                           # oracle baseline + supervised train + in-engine eval
    DataGen.cs, PoolAimNet.cs, Trainer.cs, Evaluator.cs
    README.md                            # task description + how to extend toward RL
```

## Coordinate system & units

- **Y is up.** Table plane is XZ. Slate is at `y = 0`, ball centres rest at `y = BallRadius` (0.028575 m, the WPA spec 2.25" ball).
- SI units throughout: metres, seconds, kg, Newtons. Mass = 0.180 kg, table 1.33 m x 2.33 m.
- Ball arrays are flat `BallState[GameSettings.MaxBalls]`; ID 0 is conventionally the cue ball.
- `BallState` is a value type (`[StructLayout(Sequential)]`) - ref-access through `Span<BallState>` keeps things allocation-free.
- **Source 2 / s&box is Z-up**. `LongShot.Engine.MathExtensions.ToZUp` / `FromZUp` centralise the swap - the only place that touches axis conventions. Engine internals stay Y-up.

## Physics architecture

Event-driven continuous-collision detection with **fixed-timestep accumulation**:

```
BilliardsEngine.Tick(dt):
  dt = min(dt, MaxAccumulatedDt=0.1)         # spiral-of-death guard
  accumulator += dt
  while accumulator >= FixedStep (=0.008):
    StepFixed(FixedStep)
    accumulator -= FixedStep

StepFixed(step):
  while timeRemaining > eps and iterations < 200:
    event   = FindNextEvent(timeRemaining)
    advance = event.Time or timeRemaining
    AdvancePositions(advance)
    ApplyContinuousPhysics(advance)     # gravity + cloth friction
    if event: ResolveEvent(event)        # impulses + restitution + spin transfer
```

Engine exposes `InterpolationAlpha` for sub-step render smoothing.

Motion states: `Stationary | Sliding | Rolling | Airborne | Pocketed`. Sliding-to-rolling transition is solved analytically inside a single sub-step (`timeToRoll = slipSpeed / (3.5 * slidingFriction)`); rolling friction is applied for the remainder.

Restitution is **velocity-dependent** for both cushions and ball-ball impacts (the same `CalculateDynamicRestitution` helper, with different bounds). Phenolic ball-ball restitution decays from 0.96 at low speed toward 0.86 on hard breaks.

Spin (`AngularVelocity`) is fully integrated: cue offsets generate angular impulse, sliding generates contact-patch drag, cushions absorb spin on every axis proportional to impact force, English induces swerve via cross-product with travel direction.

## Determinism

- `BilliardsEngine(int seed)` takes a seed for all internal randomness (currently just the break-rack jitter).
- Tick consumes time via a fixed-step accumulator - same inputs + same seed = byte-identical simulation across machines and frame rates.
- This is the foundation for replays, networked play, and any s&box server-authoritative tick.

## Game-rule decoupling

The architecture splits cleanly across three layers above the physics engine:

```
LongShot.Engine     ← physics. Emits raw events.
   ↓
LongShot.Shot       ← per-shot recording. ShotRecorder builds a ShotSummary.
   ↓
LongShot.Rules      ← IShotObserver implementations: scoring, fouls, power-ups, achievements.
```

The engine fires five events the recorder subscribes to:
- `OnCueStrike(ballId, aim, force, hitOffset)` — fires inside `StrikeCueBall`
- `OnBallContact(ballA, ballB)` — fires after a ball-ball collision is resolved
- `OnRailContact(ballId, railIndex, impactSpeed)` — fires after a rail bounce
- `OnJawContact(ballId, jawIndex, impactSpeed)` — fires after a jaw-corner clip
- `OnBallPocketed(ballId, dropPos)` — fires when a ball is sunk

`ShotRecorder` accumulates them into a `ShotSummary` with the input strike data, full event timeline, and derived metrics (first contact, jump-shot detection, massé detection, max airborne height, peak speed, rail-bounce count, etc.).

Every "thing above the engine" — base scoring, foul detection, power-ups, fantasy effects, achievement triggers, ML difficulty evaluators — implements `IShotObserver` and reads the same `ShotSummary`. They co-exist without coordinating. Adding a new fantasy effect is a new file in `LongShot.Rules`, never a change to the engine or other observers.

Examples:
- `observer_demo` stacks `SimpleScoring` (base, +1 per pot) and `TrickShotBonuses` (wow points) on a combination pot. Both score the same shot independently.
- `rules_8ball_legal_pot`, `rules_8ball_scratch`, `rules_8ball_wrong_group`, `rules_8ball_eight_early_loss` exercise the `EightBallRules` observer in four distinct rule states (open table → group assignment, scratch foul, wrong-group foul, premature 8-ball loss).

Convention used by `EightBallRules`: engine id 0 = cue, 1–7 = solids, 8 = the eight ball, 9–15 = stripes. The bench's `Scenario.PadBallIdsTo(n)` helper reserves placeholder ids so rule tests can place a ball at any specific id.

Power-ups that need to MODIFY physics for one shot (e.g. "extra-bouncy rails this turn") would use a separate `IShotConfigurator` slot — not yet built but the obvious next addition.

## How to extend

- **Adding a new event type**: add to `EventType`, generate TOI in `CollisionDetection`, add case in `BilliardsEngine.ResolveEvent`, write the impulse handler in `TablePhysics`.
- **Tuning realism**: edit `PhysicsConfig.Default` (or the `Tournament` cloth profile). Every resolver reads `Config` via `in PhysicsConfig` - no scattered constants.
- **New table layouts**: extend `TableDefinition` with another factory (e.g. `BuildSnookerStandard()`); `TableBuilder.Build` consumes it.
- **Testing a shot**: write a scenario in `src/LongShot.Bench/Scenarios/` - see that README.

## The bench

`src/LongShot.Bench` is a headless console runner that scripts shots, runs the engine, asserts on outcomes, and writes a top-down PNG per scenario. **Use it as the iteration loop for any physics change.** PNGs are inspectable as images (Claude Code reads them directly), so changes to `PhysicsConfig` can be verified in seconds without launching the game.

Run:
```pwsh
dotnet run --project src/LongShot.Bench/LongShot.Bench.csproj
```

Nineteen starter scenarios cover: stop, follow, draw, 30° stun cut, curve/swerve, side-English effects, jump shot, massé, multi-ball chain, frozen ball, four cushion-calibration measurements (perpendicular COR, 45° reflection, lag stroke travel, break stroke travel), English off a rail, clean corner pot, jaw deflection, combination pot, hard break. All currently pass.

## The training pipeline

`src/LongShot.Train` uses **TorchSharp** (Microsoft's .NET binding to LibTorch) to train a tiny MLP that learns the ghost-ball aim for potting an object ball into the top-right corner pocket. End-to-end pipeline:

1. Oracle baseline: textbook geometric aim run through the engine (~27% pocketing on random positions).
2. Train: 100k synthetic samples, mini-batch SGD, ~4 min on CPU.
3. Network eval: same protocol with predicted aims. Currently ~20% (within ~7 pp of oracle).

This is the validation rig for TorchSharp in .NET. The next experiments live on top of the engine's `SnapshotState` / `RestoreState` / `Clone` APIs (parallel rollouts, tree search, RL self-play). See `src/LongShot.Train/README.md` for details.

```pwsh
dotnet run --project src/LongShot.Train/LongShot.Train.csproj
```

First run downloads ~250 MB of LibTorch binaries; subsequent runs start fast.

## Cushion + slate calibration

Five `PhysicsConfig` values were tuned 2026-05-16 against real-world references:

| Setting | Old | New | Why |
|---|---|---|---|
| `Cushion.SpeedDecay` | 0.03 | 0.02 | Perpendicular COR was 0.69 (real ≈ 0.75) |
| `Cushion.FrictionCoeff` | 0.20 | 0.15 | 45° rebound was 50.6° (real ≈ 47–48°) |
| `Cloth.NapResistanceMultiplier` | 1.0 | 0.5 | Lag stroke travelled 1.25 lengths (real ≈ 1.5) |
| `Env.SlateRestitution` | 0.12 | 0.5 | Jump shots barely jumped. Real ball-slate COR (with thin tournament cloth) ≈ 0.5. |
| Slate bounce velocity threshold (in `TablePhysics.UpdateBallMotion`) | 0.1 m/s | 0.5 m/s | With the higher COR, gentle rail-induced vertical kicks were causing balls to chatter on the slate. The threshold isolates "real" airborne shots from incidental Y velocity. |

The bench's four cushion-physics scenarios (`cushion_perp_bounce`, `cushion_angle_45`, `lag_stroke_travel`, `break_stroke_travel`) measure these directly against documented real-world values. Re-run after any tuning change.

The trade-off worth knowing: `Cushion.FrictionCoeff` couples *tangential-linear damping* (which steepens oblique rebounds) and *spin-to-linear transfer* (which produces the "rail-induced English" kick). They share one Coulomb friction coefficient in the current model. 0.15 is the chosen middle; lower values make the rebound angle more mirror-like but kill running-English effects.

## Notable physics fixes vs the original prototype

These are *behavioural* fixes — not refactoring. The numbers in `PhysicsConfig` may need re-tuning if any of these feel off:

1. **Cushion bounces damp spin on all axes** (was Y-only). Follow/draw no longer survives a rail bounce intact.
2. **`State = Sliding` after rail/jaw impact**. A rolling ball that bounces a rail no longer has its spin clobbered by `ApplyRolling` on the next tick.
3. **Cue squirt is a rotation of the aim vector**, not an additive component. Off-centre hits no longer add energy.
4. **Pocket trigger is at the throat with commitment depth** (was 2.6 cm *outside* the throat). Rail fillets adjacent to a pocket now get a chance to deflect off-line approaches before the pocket consumes the ball.
5. **CCD ignores rail back-faces** for balls on the wrong side. `CalculateBallSegmentImpactTime` no longer returns 0 when a ball is past the rail in the -normal direction (which previously caused balls to teleport against the nearest back face when struck in certain directions).
6. **Speed-dependent ball-ball restitution** (was constant 0.98).
7. **Deterministic break rack jitter** via seeded RNG.
8. **Rail rotation formula `atan2(-dir.Y, dir.X)`** (was `atan2(dir.Y, dir.X)`). The naive form gave a 180° rotation error for any rail with a non-zero Y direction — the four side rails. Effects: their playfield-facing edges pointed *outward*, and their `StartJaw`/`EndJaw` cutout depths swapped between the corner-pocket and side-pocket ends. Visually obvious in the top-down bench renderer; physically subtle in 3D rendering, which is why the bug survived in the original prototype.
9. **Rail jaw cutouts are on the playfield-facing edge**, not extending past the rail's back. The hexagonal outline corners changed: back at table boundary (straight, from `-halfLen` to `+halfLen`), playfield-facing edge shorter with jaws cutting inward at each end. Matches real-rail profile.
10. **Back-corner fillets removed**. Only the playfield-side throat/nose corners (the jaws) get filleted; back corners stay sharp since they meet adjacent rails at the table boundary.

## Build / run

- All projects target **net10.0**. Migrated 2026-05 to match s&box's `net10.0` requirement so a single `dotnet build` covers every target.
- s&box host: opens via the s&box editor - `src/longshot/Code/longshot.csproj`. The editor's compiler is NOT MSBuild and ignores `Directory.Build.props`, so all game C# (engine, shot, rules, host code) physically lives under `src/longshot/Code/` and is auto-discovered by the Razor SDK's default item discovery.
- Bench: `dotnet run --project src/LongShot.Bench`
- Train: `dotnet run --project src/LongShot.Train` (first run pulls ~250 MB of LibTorch binaries).
- TorchSharp pinned to `0.105.1` - ships `netstandard2.0`, works under net10 with no shim.

### The s&box compile constraint

Inside `longshot.dll` (s&box compile), `Sandbox.Vector3` is exposed at `global::Vector3` and shadows `System.Numerics.Vector3`. Every engine file therefore uses an explicit alias `using SnVector3 = System.Numerics.Vector3;` (and `SnVector2` where needed) and refers to `SnVector3` / `SnVector2` in code. This is invisible to Bench / Train - the alias resolves to the same underlying type. See `.claude/skills/sbox-longshot-port/SKILL.md` gotcha #3 for the full story.

### s&box skills — read these before s&box work

Two project skills capture hard-won s&box gotchas. Consult them at the start of any s&box rendering / spawn / API task — half the wins come from not re-discovering documented bugs:

- **`.claude/skills/sbox-longshot-port/SKILL.md`** — concrete API guidance for the LongShot project: assembly inclusion via Razor SDK auto-discovery, the namespace shadowing trick (`SnVector3` alias), s&box's compiler vs MSBuild, MCP setup options. Read when porting / adding code that crosses the s&box compile boundary.
- **`.claude/skills/sbox-runtime-rendering/SKILL.md`** — why ModelRenderers go invisible at runtime, the cloud-asset-vs-built-in distinction, native sizes of `models/dev/*`, FreeCam / camera quirks, MCP bridge caveats (especially that `take_screenshot` captures the editor viewport not the play camera), pitch sign convention. Read when "something's not rendering" or "transforms don't match".

**Update these skills whenever a new lesson is discovered.** If a non-obvious s&box behaviour costs more than 15 minutes to figure out, add an entry. The skills are append-mostly. Each entry should include: symptom, cause, fix, and a verified-on date so we know what to re-check after s&box updates.

## Known follow-ups (not yet done)

- **MSTest/xUnit unit tests** for `PhysicsMath` and the resolvers (deterministic math, low-friction additions).
- **Cue stick + aim UI inside s&box** (`MatchHost` currently has a one-button "fire forward" stub).
- **Rack-and-table visuals**: spawn the 6 pockets and 6 rails as `GameObject`s using `TableBuilder` output, share the filleted path with the physics layer.
- **Network the simulation**: gate `engine.Tick` behind `if (IsProxy) return;` and `[Sync]` the ball snapshots. The engine is already deterministic with a seeded ctor, so peer-side prediction becomes feasible once the input + seed are networked.
