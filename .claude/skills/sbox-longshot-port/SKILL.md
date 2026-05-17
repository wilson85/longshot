---
name: sbox-longshot-port
description: Concrete s&box (Source 2 + .NET 10) API guidance for the LongShot billiards engine host. Use when working in `src/longshot/`, editing files under `Sandbox` namespace, modifying `longshot.csproj` / `longshot.sbproj`, or diagnosing "type or namespace 'LongShot' could not be found" errors when building inside the s&box editor.
---

# s&box port guide for LongShot

> ## ⚠️ READ FIRST — the one trap that bites every time
>
> **The s&box editor does NOT use MSBuild.** Its compile pipeline is `Sandbox.Compiler` (a Roslyn workspace built straight from the `.sbproj` + a regenerated `.csproj`). It ignores `Directory.Build.props`, `Directory.Build.targets`, and any out-of-tree `<Compile Include>` you try to inject.
>
> What it DOES use, transparently: the `Microsoft.NET.Sdk.Razor` default item discovery — every `*.cs` / `*.razor` under `src/longshot/Code/` (recursively) gets compiled into `longshot.dll`. That's the seam.
>
> So **the engine source physically lives under `src/longshot/Code/`** — not `src/LongShot.Engine/`. Layout:
>
> ```
> src/longshot/Code/
>   LongShot.Engine/       <-- canonical home of the engine source
>   LongShot.Shot/
>   LongShot.Rules/
>   Match/MatchHost.cs
>   Conversions.cs
>   longshot.csproj        <-- editor-regenerated; never add ProjectReferences here
> ```
>
> The standalone `src/LongShot.Engine/LongShot.Engine.csproj` (and the Shot / Rules siblings) still exists, but it's now a thin wrapper that pulls the same files back via `<Compile Include="..\longshot\Code\LongShot.Engine\*.cs" />` for the Bench / Train consoles. Single source of truth — the s&box tree.
>
> If you see `The type or namespace name 'LongShot' could not be found` inside the editor: something got moved OUT of `src/longshot/Code/`. Move it back.

s&box went 1.0 on **2026-04-28**, on **.NET 10** (since 2025-11-19). The engine source was open-sourced in update 26.04.08. The current API is the **Scene system** — `Scene` → `GameObject` → `Component` — not the legacy entity/player-controller model. LongShot's host code is one `Component` (`MatchHost`) per scene driving the `BilliardsEngine`; everything else (cue stick, rails, pockets, rack reset) hangs off that.

Sources verified against (all confirmed live 2026-05): `https://sbox.game/dev/doc`, `https://github.com/Facepunch/sbox-scenestaging` (the canonical example-components repo), `https://sbox.game/release-notes`, `https://wiki.facepunch.com/sbox/AccessList`.

## Build & project model

The freshly-scaffolded `src/longshot/Code/longshot.csproj` is `Microsoft.NET.Sdk.Razor`, `net10.0`, `LangVersion 14`, `Nullable disabled`, `RootNamespace Sandbox`. It references engine DLLs directly from Steam (absolute paths into `F:\SteamLibrary\...\bin\managed\`) and uses `Sandbox.Internal.GlobalGameNamespace` as a static using. **Do not change those defaults** — the editor regenerates this file from `longshot.sbproj` on certain actions.

All LongShot projects target `net10.0`. Migrated 2026-05 — TorchSharp is `netstandard2.0` so it works under net10 with no shim. s&box hot-reload only loads net10 assemblies, so a single TFM across the repo is the path of least friction.

**The source-inclusion model.** Engine / Shot / Rules `.cs` files live under `src/longshot/Code/` (each in their own subfolder named after the namespace). The Razor SDK's default item discovery pulls them into `longshot.dll` automatically — no explicit `<Compile Include>` and no `<ProjectReference>` in the regenerated csproj. The thin `src/LongShot.Engine/LongShot.Engine.csproj` (etc.) re-includes the same files via `<Compile Include="..\longshot\Code\LongShot.Engine\*.cs" />` with `<EnableDefaultCompileItems>false</EnableDefaultCompileItems>` so the Bench / Train consoles compile against the same source without duplication.

Why this rather than `Directory.Build.props` / `<ProjectReference>`: the s&box compiler is **not** MSBuild. Earlier attempts to inject source via `Directory.Build.props` worked under `dotnet build` from CLI but silently no-op'd inside the editor, producing `The type or namespace name 'LongShot' could not be found` despite a clean CLI build.

Side effect of inlining: types like `LongShot.Engine.BallState` live in `longshot.dll` when compiled inside s&box, but in `LongShot.Engine.dll` when run from Bench / Train. Fine — nothing crosses assembly boundaries between those worlds.

**Going forward**: to add another shared library, create `src/longshot/Code/MyNewLib/` and drop the `.cs` files in. If you also want Bench / Train to consume it as a named csproj, create `src/MyNewLib/MyNewLib.csproj` with the same `<EnableDefaultCompileItems>false</EnableDefaultCompileItems>` + `<Compile Include>` pattern.

**Access list (whitelist).** s&box restricts which BCL types your code can touch — `Process.Start`, `DllImport`, most reflection are blocked (`https://wiki.facepunch.com/sbox/AccessList`). `System.Numerics`, `System.Collections.Generic`, `System.Math`, `System.MathF`, `Span<T>` are all on the access list (the engine and `sbox-scenestaging` examples use them freely — `MathF.Sin`, `Vector3`, `List<T>` are everywhere). LongShot.Engine should be clean. **Flagged for verification**: `System.Random` is currently whitelisted, but check that any `Stopwatch`/diagnostic code in benches stays in `LongShot.Bench` (which won't be referenced by the s&box project).

**TFM mismatch tolerance**: a `net10.0` s&box project can reference a `net8.0` project assembly — but you'll see analyzer warnings and the access-list scanner runs against *your* referenced code. Cleaner to bump LongShot.Engine to net10.

## The host component pattern

`MatchHost` is the one `Component` that owns the engine instance. Lifecycle (`https://sbox.game/dev/doc/scene/components/component-methods`, verified against `sbox-scenestaging/Code/ExampleComponents/*`):

| Method            | When                                            | Use for                       |
|-------------------|-------------------------------------------------|-------------------------------|
| `OnAwake`         | Component instantiated, before scene start      | Field init, no scene queries  |
| `OnStart`         | Once, after enabled, before first FixedUpdate   | Engine construction, prefab spawn |
| `OnEnabled`       | Each time component becomes active              | Re-subscribe to events        |
| `OnFixedUpdate`   | Fixed tick (50 Hz per `longshot.sbproj`)        | **`engine.Tick(Time.Delta)`** |
| `OnUpdate`        | Every frame                                     | Mirror transforms, read input |
| `OnDisabled`      | Component disabled                              | Unsubscribe                   |
| `OnDestroy`       | Component removed                               | Cleanup                       |
| `DrawGizmos`      | Editor selection                                | Debug-draw rails, pockets     |

Verified call order: `OnAwake` → `OnEnabled` → `OnStart` → (loop: `OnFixedUpdate` / `OnUpdate`) → `OnDisabled` → `OnDestroy`. `OnStart` is guaranteed to run before the first `OnFixedUpdate` (`https://github.com/Facepunch/sbox-scenestaging/issues/107` resolved).

Skeleton — drop in `src/longshot/Code/Match/MatchHost.cs`:

```csharp
using Sandbox;
using LongShot.Engine;
using static LongShot.Engine.MathExtensions;

public sealed class MatchHost : Component
{
    [Property] public GameObject BallPrefab { get; set; }
    [Property] public float UnitsPerMetre { get; set; } = 39.3701f; // see Coordinate conversion

    BilliardsEngine engine;
    readonly List<GameObject> ballObjects = new();

    protected override void OnStart()
    {
        engine = new BilliardsEngine(seed: 1);
        engine.OnBallPocketed += HandlePocketed;
        engine.RackBreak(); // or your initial state setup

        for (int i = 0; i < GameSettings.MaxBalls; i++)
        {
            var go = BallPrefab.Clone(WorldPosition);
            go.Name = $"Ball_{i}";
            ballObjects.Add(go);
        }
    }

    // Physics: drive engine on the fixed tick.
    protected override void OnFixedUpdate()
    {
        if (IsProxy) return; // only the host authority runs simulation
        engine.Tick(Time.Delta);
    }

    // Visuals: mirror ball state on every render frame for smoothness.
    protected override void OnUpdate()
    {
        var balls = engine.GetBallStates();
        for (int i = 0; i < balls.Length && i < ballObjects.Count; i++)
        {
            var go = ballObjects[i];
            if (!go.IsValid()) continue;
            if (balls[i].State == MotionState.Pocketed) { go.Enabled = false; continue; }
            go.WorldPosition = ToZUp(balls[i].Position) * UnitsPerMetre;
            // Optional: angular orientation from accumulated rotation.
        }
    }

    void HandlePocketed(int id, System.Numerics.Vector3 dropPos)
    {
        if (id == 0) engine.RespawnBall(0, /*head spot*/ default);
    }
}
```

Notes verified against `sbox-scenestaging/Code/ExampleComponents/SpinComponent.cs`, `BrickBall.cs`, `Gun.cs`:
- `Component` is `Sandbox.Component`. Inherit directly. The class can be `public sealed`.
- `[Property]` exposes a field/property in the inspector (`https://sbox.game/api/Sandbox.Component`).
- `Time.Delta` inside `OnFixedUpdate` equals the fixed step (1/TickRate = 0.02s with `TickRate: 50` in `longshot.sbproj`); inside `OnUpdate` it's the frame delta.
- `WorldPosition` / `LocalPosition` / `WorldRotation` are properties on `GameObject` *and* on `Component` (the latter forwards). Use these — `Transform.Position` is older API and triggers an analyzer warning.
- `GameObject.Clone(position)` is the canonical spawn call (verified in `Gun.cs`).
- `IsProxy` is true on non-authoritative network peers — skip simulation there.

## Coordinate conversion

The engine internals stay Y-up metric SI. Convert once at the s&box boundary.

**s&box is Z-up, right-handed**, **+X forward, +Y left, +Z up** (Source 2 inherits Source's convention). One Source unit ≈ **1 inch** (`https://developer.valvesoftware.com/wiki/Unit`); 1 m ≈ **39.37 units**. The Citizen player is ~72 units tall. A 2.84 m pool table is ~112 units long — comparable to a Source map prop.

`LongShot.Engine.MathExtensions` already has the swap (signatures use `SnVector3` — see gotcha #3 for why):

```csharp
// From C:/code/longshot/src/LongShot.Engine/MathExtensions.cs
using SnVector3 = System.Numerics.Vector3;
public static SnVector3 ToZUp(SnVector3 yUp)   => new(yUp.Z, -yUp.X, yUp.Y);
public static SnVector3 FromZUp(SnVector3 zUp) => new(-zUp.Y, zUp.Z, zUp.X);
```

**Boundary recipe** (apply in this order, every direction):
1. `var sboxMetres = MathExtensions.ToZUp(engineVec);`
2. `var sboxUnits  = sboxMetres * UnitsPerMetre;` (39.3701 if you want SI; 39.37 is the engine convention)
3. Assign to `WorldPosition` / `WorldRotation` etc.

For the inverse path (input → engine, e.g. cue aim from mouse-picked world point):
1. `var metres = sboxWorldPos / UnitsPerMetre;`
2. `var engine = MathExtensions.FromZUp(metres);`

**Rotations**: `System.Numerics.Quaternion` is on the access list and round-trips into s&box `Rotation` (which is also a quaternion). If you carry ball orientation across, swap axes the same way as positions before constructing the `Rotation`. **[verify]** — `Rotation.FromAxis` is the safest constructor; double-check by spinning a textured ball.

**s&box `Vector3` vs `System.Numerics.Vector3`**: these are *different types* (see `https://github.com/Facepunch/sbox-issues/issues/7713`). s&box uses its own `Vector3` (3 floats, same memory layout) but with engine-aware operators and helpers. Implicit conversion exists in some directions but **not all** — write explicit converters in a single `Conversions.cs` helper inside `src/longshot/Code/` to keep the seam tight. The accessor pattern `new global::Sandbox.Vector3(sn.X, sn.Y, sn.Z)` is reliable.

## Input

Verified from `sbox-scenestaging/Code/ExampleComponents/NoClip.cs` and `Gun.cs` (current `main`, May 2026):

```csharp
protected override void OnUpdate()
{
    // Mouse look (yaw/pitch deltas).
    var look = Input.AnalogLook;       // Angles { pitch, yaw, roll } per-frame delta

    // WASD-style movement (also gamepad). Vector3 forward/right/up units.
    var move = Input.AnalogMove;

    // Action-bound buttons (set up in Project Settings → Input Actions).
    if (Input.Pressed("Attack1"))  Fire();     // edge: this frame
    if (Input.Down("Run"))         Sprint();   // held
    if (Input.Released("Attack1")) Release();  // edge: this frame

    // Raw mouse coords if you need them (cue aim from screen-space picking):
    var screenPos = Mouse.Position;  // Vector2 in pixels
    var delta     = Mouse.Delta;     // Vector2 movement this frame
}
```

**Cue stroke recipe** (still TODO in `MatchHost.cs` - currently a one-button "fire forward" stub):
- Aim: `Input.AnalogLook` rotates the cue around the cue ball (or pick a world point under `Mouse.Position` via `Scene.Trace.Ray(...)` from the camera).
- Power: hold a button (`Input.Down("Attack1")`) and accumulate while the user pulls back; release fires.
- Define `"FireCue"`, `"AimAdjust"`, etc. as project input actions in the editor — don't hard-code key codes.

## Top gotchas

1. **The s&box compiler is not MSBuild, and the editor regenerates `Code/longshot.csproj`.** ⚠️ The single most common port failure. Confirmed empirically 2026-05. Symptoms: `The type or namespace name 'LongShot' could not be found`, `Metadata file '...obj/Debug/ref/longshot.dll' could not be found`, `Broken Reference: package.local.longshot`. The editor rewrites the csproj from `longshot.sbproj` and keeps only the Sandbox.*.dll references / Base Library project ref / Using directives that come from its own template. `Directory.Build.props` is **ignored** by the editor's compile pipeline (`Sandbox.Compiler.BuildAsync`), even though `dotnet build` from CLI honours it. `<ProjectReference>` to an arbitrary external csproj gets stripped on regen. **Workaround**: keep all shared source physically under `src/longshot/Code/` (in namespace-named subfolders). The Razor SDK's default item discovery picks them up automatically; no manual injection needed. For Bench / Train consumption, the standalone csprojs under `src/LongShot.Engine/` etc. use `<EnableDefaultCompileItems>false</EnableDefaultCompileItems>` + `<Compile Include="..\longshot\Code\..." />` to pull the same files back, single source of truth.

2. **TFM**: every LongShot project (Engine, Shot, Rules, Bench, Train, s&box host) is `net10.0`. TorchSharp is netstandard2.0 so it works under net10 with no shim. Don't multi-target unless you have to: a single TFM keeps `obj/` cleaner and avoids the `bin/{tfm}/` path divergence that confuses the s&box editor's path probing.

3. **Two `Vector3` types — and Sandbox's lives at the GLOBAL namespace root**: `System.Numerics.Vector3` (in your engine) and `Sandbox.Vector3` (in s&box) coexist. They have identical layout but distinct types; implicit conversion is partial (`https://github.com/Facepunch/sbox-issues/issues/7713`).

   The killer wrinkle: **`Sandbox.Vector3` is exposed at `global::Vector3`** (the s&box global namespace pre-imports it as a top-level type). So in any source file with `using System.Numerics;`, bare `Vector3` resolves to `Sandbox.Vector3` — its props are lowercase (`.x .y .z`), `Length` is a property not a method, and there's no `UnitY`. Hence errors like `'Vector3' does not contain a definition for 'Z'` and `Non-invocable member 'Vector3.Length' cannot be used like a method` when LongShot.Engine source is inlined into `longshot.dll`.

   And the obvious "just add an alias" fix triggers **CS0576**: `Namespace '<global namespace>' contains a definition conflicting with alias 'Vector3'`. C# refuses to let you alias a name that already exists at the global root.

   **Workaround actually used in this codebase**: rename every `Vector3` and `Vector2` reference inside `LongShot.Engine` and `LongShot.Shot` to `SnVector3` / `SnVector2` (matching the `SnVec3` convention `src/longshot/Code/Conversions.cs` already uses for `System.Numerics.Vector3`). Top of every file:

   ```csharp
   using SnVector3 = System.Numerics.Vector3;
   using SnVector2 = System.Numerics.Vector2;  // only where used
   ```

   The aliases don't collide with `global::Vector3` because they're spelled differently. Consumers outside the s&box compilation (Bench, Train) don't care — `SnVector3` is fully transparent and resolves to `System.Numerics.Vector3` like any other alias.

   For `Sandbox.Vector3` (i.e. anywhere you cross the boundary into s&box-owned state — `WorldPosition`, networked types, `GameObject.LocalScale`), keep using bare `Vector3` in files under `src/longshot/Code/` and centralise the conversion in `src/longshot/Code/Conversions.cs`. Never let raw `System.Numerics.Vector3` reach the inspector or networked state.

   Diagnostic confirmation: run `dotnet build src/longshot/Code/longshot.csproj` from CLI. If you see `CS1061: 'Vector3' does not contain a definition for 'Z'` *or* `CS0576: ... conflicting with alias 'Vector3'`, you're hitting this gotcha. Identical pattern applies to `Matrix4x4` — currently only `TableBuilder.cs` uses it and Sandbox doesn't shadow `Matrix4x4` at the global root, but if a future port pulls in more types, alias them prophylactically (e.g. `SnMatrix4x4`).

4. **Hot-reload nukes static state in generics**: s&box can't migrate static fields on generic types during hotload and emits a warning (`https://sbox.game/dev/doc/code/advanced-topics/hotloading`). Anything cached via reflection or in a `static class Foo<T>` is lost. **Workaround**: mark such caches `[SkipHotload]` and repopulate after reload; keep LongShot.Engine's `PhysicsConfig.Default` as a const-like static field (non-generic, safe). Hotload also has a known crash mode after editing scene-system code (`https://github.com/Facepunch/sbox-issues/issues/4132`) — restart the editor if balls stop ticking.

5. **Network authority**: `longshot.sbproj` is `GameNetworkType: "Multiplayer"` by default. By default only the **host** runs simulation; other peers get `IsProxy == true`. **Workaround**: gate `engine.Tick` behind `if (IsProxy) return;`. Mirror ball state via `[Sync] public NetList<BallSnapshot> Balls { get; set; }` (or send via RPCs) so clients see the same simulation. The engine is already deterministic with a seeded ctor, so once you network the input + the seed, peer-side prediction is feasible later. For single-player iteration: change `GameNetworkType` to `"Singleplayer"` in the .sbproj. (Already set to Singleplayer as of 2026-05.)

6. **Rubikon will fight your custom physics**: s&box ships Box3D (a fork of Valve's Rubikon — `https://x.com/gvarados/status/1819448434057007190`). If you `AddComponent<Rigidbody>` to a ball GameObject, Box3D will fight LongShot.Engine for ownership of the transform. **Workaround**: do **not** add `Rigidbody` or any collider to the ball GameObjects. They are pure visuals; LongShot.Engine owns position/velocity. Only add static colliders (the table slate, rails) if you want raycasts for input picking — and even those can be replaced by a single `BoxCollider` on the table plane.

## Feedback loop — reading editor output without an MCP

The s&box editor logs everything (compile errors, runtime exceptions, asset failures) to a live text file you can read directly:

```
F:\SteamLibrary\steamapps\common\sbox 590830\logs\sbox-dev.log
```

(Or wherever Steam is installed — check the `OutputPath` of `Code/longshot.csproj` for the install root.)

Format: one line per event, tab-separated.

```
YYYY/MM/DD HH:MM:SS.fff	[Tag/Subtag]	message
2026/05/16 18:30:50.7156	[Generic]	Error | The type or namespace name 'X' could not be found ... File.cs:43,8
```

Useful filters when reading the log:

| Filter                                | Surfaces                                              |
|---------------------------------------|-------------------------------------------------------|
| `[Compiler/local.longshot.editor]`    | Compile pass result (success or failure)              |
| `[Generic]\s+Error\|`                 | Per-error messages with file:line:col                 |
| `[Generic]\s+Warning\|`               | Per-warning messages                                  |
| `[engine/ResourceSystem]`             | Missing assets — usually noise from base content      |
| `[engine/MaterialSystem]`             | Material/shader errors                                |
| `\b(MatchHost\|LongShot\|Conversions)\b` | Anything mentioning our code                       |

**Recommended workflow when iterating in the editor:**

1. Save changes (or trigger `Tools → Rebuild Solution`).
2. Read the last ~200 lines of `sbox-dev.log`. The most recent compile pass will be at the bottom.
3. If `[Generic] Error |` lines exist, those are the compile errors with file/line; fix and re-save.
4. If `[Compiler/...] failed` followed by no `[Generic] Error` lines, the failure is a project-level issue (missing reference, broken csproj) — check the lines just before.

Other useful artefacts:

- `F:\SteamLibrary\steamapps\common\sbox 590830\.vs\output\longshot.dll` mtime tells you when the last successful build happened.
- `F:\SteamLibrary\steamapps\common\sbox 590830\.vs\output\longshot.xml` is the XML doc, regenerates on every build.
- `logs/sbox-dev-YYYY-MM-DD.N.zip` archives are rotated older logs.

This is currently the best available signal — there's no official MCP server for s&box, no CLI compile command, no IDE-side hook. Reading the log is what works.

## Curated links

- Component methods + lifecycle: https://sbox.game/dev/doc/scene/components/component-methods
- Scene/GameObject overview: https://sbox.game/dev/doc/scene/
- Component API reference: https://sbox.game/api/Sandbox.Component
- Hotloading rules and `[SkipHotload]`: https://sbox.game/dev/doc/code/advanced-topics/hotloading
- Access list (BCL whitelist): https://wiki.facepunch.com/sbox/AccessList
- Networked objects + `[Sync]`: https://sbox.game/dev/doc/networking/networked-objects
- Release notes (track API churn): https://sbox.game/release-notes
- Canonical example components (CURRENT API, read these first when in doubt): https://github.com/Facepunch/sbox-scenestaging/tree/main/Code/ExampleComponents
- The official Sandbox gamemode (full reference game, MIT-licensed, demonstrates current patterns at game scale — `GameObjectSystem<T>`, `ISceneStartup`, partial-class managers, `[Sync(SyncFlags.FromHost)]`): https://github.com/Facepunch/sandbox
- Engine source (post-open-source, April 2026): https://github.com/Facepunch/sbox-public
- Source-unit reference (1 unit ≈ 1 inch): https://developer.valvesoftware.com/wiki/Unit
