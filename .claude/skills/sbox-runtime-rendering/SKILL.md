---
name: sbox-runtime-rendering
description: Hard-won s&box runtime gotchas - why things don't render, asset-loading rules, scale/transform conventions, camera + FreeCam interactions, MCP bridge caveats. Use when a ModelRenderer is invisible despite valid bounds, when runtime-spawned GameObjects don't show up, when scale/pitch values don't match expectations, or when MCP screenshots disagree with what the user sees in play mode.
---

# s&box runtime rendering & runtime-spawn gotchas

This skill captures bugs that were genuinely non-obvious. Most of them cost hours to find because the symptoms looked like something else.

> ## ⚠️ READ FIRST — when ModelRenderer is invisible at runtime
>
> If a runtime-spawned `ModelRenderer` reports `Active=True`, `Enabled=True`, non-null `SceneObject`, correct `Bounds`, correct `Model` — and still doesn't render — the asset you assigned is almost certainly a **mounted cloud asset** (e.g. anything under `models/<org>/<package>/...vmdl` whose package was added via `Package.Fetch + MountAsync` or the bridge's `asset_mount`).
>
> **Mounted cloud assets only render when assigned in edit mode** (saved into the scene file). Runtime assignment via `Model.Load(path)` or even `Cloud.Model(ident)` returns a Model object whose mesh data is **not bound to the SceneWorld for rendering until baked at compile time**.
>
> **Use built-in models** from `<sbox-install>/core/models/dev/`:
> - `models/dev/box.vmdl` — **50 unit native cube**
> - `models/dev/sphere.vmdl` — 50 unit native sphere
> - `models/dev/plane.vmdl`, `plane_large.vmdl`
> - `models/dev/error.vmdl` (pink question mark — useful fallback)
>
> These are local core assets, render at runtime via `Model.Load("models/dev/box.vmdl")`, no cloud workflow needed.

## The cloud-asset-doesn't-render bug

### Symptom

You spawn a GameObject at runtime, add a `ModelRenderer`, set `Model = Model.Load("models/vidya/cube_white_64.vmdl")` (a mounted cloud asset). Diagnostics:

- `ModelRenderer.Active`: `True`
- `ModelRenderer.Enabled`: `True`
- `ModelRenderer.Bounds`: correct (e.g. `mins -45.87, -26.18, -1, maxs 45.87, 26.18, 0`)
- `ModelRenderer.Model.Name`: correct (`"models/vidya/cube_white_64.vmdl"`)
- `ModelRenderer.SceneObject`: non-null (`Sandbox.SceneObject`)
- `GameObject.WorldPosition`, `LocalScale`: correct

Yet the mesh doesn't appear in the play viewport. The camera renders sky / background / other (edit-mode-assigned) models fine. Only the runtime-assigned cloud-asset renderer is invisible.

### Cause

`Model.Load(path)` for a mounted cloud asset returns a Model reference with valid metadata but the mesh data hasn't been properly bound to the renderer pipeline. `Cloud.Model(ident)` works at compile time (bakes the asset into the package) but doesn't fix runtime assignment — the model still needs to be edit-mode-serialized somewhere to fully load.

### Workaround (verified)

Option A — use core built-in models:
```csharp
var box = Model.Load("models/dev/box.vmdl");          // 50u native — RENDERS at runtime
var sphere = Model.Load("models/dev/sphere.vmdl");    // 50u native — RENDERS at runtime
```

Option B — expose the model as a `[Property]` and assign in the Inspector:
```csharp
[Property] public Model BoxModel { get; set; }
// ... in OnStart:
var box = BoxModel ?? Model.Load("models/dev/box.vmdl");
```
When you drag the cloud asset onto this slot in the editor, it gets serialized into the scene file as a fully-resolved reference. Runtime reads of `BoxModel` then return a fully-loaded Model.

Option C — pre-author the GameObjects in edit mode (orange ground plane, yellow cube earlier in this session). The bridge's `create_gameobject` + `assign_model` flow does this, and the resulting renderers always work in play mode because the model assignment is part of the scene serialization.

### Diagnostic procedure

When a runtime ModelRenderer doesn't render, swap the `Model` to `models/dev/box.vmdl` (50u cube). If THAT renders, the original asset is the issue, not the spawn code. If the dev box also doesn't render, look at camera framing / transform / scene clones / parent-disabled.

## Built-in dev primitive sizes

The s&box install ships primitives in `<install>/core/models/dev/`. These are LOCAL assets and render at runtime via `Model.Load`.

| Path | Native size | Notes |
|---|---|---|
| `models/dev/box.vmdl` | 50u cubed | Default fallback for anything box-shaped |
| `models/dev/sphere.vmdl` | ~50u (verify) | Default sphere |
| `models/dev/plane.vmdl` | unknown — verify with bounds | Single-sided |
| `models/dev/plane_large.vmdl` | larger plane | |
| `models/dev/error.vmdl` | the pink question mark | What loads when a model path is broken |

To verify a model's native size: spawn a GameObject with `LocalScale = (1,1,1)`, assign the model, then `get_runtime_property` for `Bounds`. The bounds extents × 2 = native size.

## Cloud-asset rendering matrix

| Asset source | When assigned | Renders at runtime? |
|---|---|---|
| Core built-in `models/dev/*.vmdl` | edit mode (Inspector) | ✅ |
| Core built-in `models/dev/*.vmdl` | runtime (`Model.Load` in OnStart) | ✅ |
| Mounted cloud asset (`models/<org>/<pkg>/*.vmdl`) | edit mode (Inspector or `create_gameobject` + `assign_model`) | ✅ |
| Mounted cloud asset | runtime (`Model.Load` or `Cloud.Model`) | ❌ silent failure |

The asymmetry on the last row is the bug.

## Vector3 / Vector2 / Sandbox math conventions

### `Sandbox.Vector3` lowercase fields

`Sandbox.Vector3` uses lowercase `.x .y .z` and `Length` is a *property* (not a method).
`System.Numerics.Vector3` uses uppercase `.X .Y .Z`, `.Length()` is a method, `Vector3.UnitY` exists.

The two types coexist in the s&box compile, and `using static Sandbox.Internal.GlobalGameNamespace` brings `global::Vector3` (Sandbox's version) into scope. Inside engine source that uses `using System.Numerics`, you'll get CS0576 trying to alias `Vector3`. Mitigation: aliases like `using SnVector3 = System.Numerics.Vector3;` (see `sbox-longshot-port` skill for the full pattern).

### `new Vector3(scalar)` IS uniform

Confirmed empirically: `new Sandbox.Vector3(1.5625f)` creates `(1.5625, 1.5625, 1.5625)`. Not `(scalar, 0, 0)`. Earlier suspicion (during debug) was wrong.

### Pitch convention

**Positive pitch = look DOWN.** Verified by decoding `WorldRotation` quaternion to a forward vector:

```
rotation (pitch=30, yaw=90, roll=0)
quaternion (-0.18301, 0.18301, 0.68301, 0.68301)
forward (0, 0.866, -0.5)   ← +Y is forward (yaw=90), -Z is down → pitch=30 looks down
```

s&box matches Source 2 / Quake conventions: `pitch > 0 → down`, `yaw > 0 → counterclockwise viewed from above`.

### Near pitch ±90 = gimbal lock

`set_transform({pitch:90, yaw:0, roll:0})` round-trips through quaternion and reads back as `pitch:89.98, yaw:180, roll:180`. The forward vector is correct (straight down), but Euler values look weird. Don't trust Euler readback near the poles; decode the quaternion.

## Camera and FreeCam

### FreeCam overrides rotation every frame

If a `FreeCam` component (or any free-look component) is attached and Enabled, its `OnUpdate` reads `Input.AnalogLook` and overwrites `WorldRotation` every frame. `set_transform` "works" but immediately gets overwritten.

To pin the camera to a known view: **disable the FreeCam component first**, then `set_transform`. Re-enable later if user input is wanted.

### `IsMainCamera` is a boolean, not a context action

To make a `CameraComponent` the one being rendered: set `IsMainCamera = true` on it. There's no "Set as Main Camera" button or selection state. If multiple cameras have `IsMainCamera = true`, the highest `Priority` wins.

### `editor_camera` is auto-managed

The s&box editor always inserts a `GameObject` named `editor_camera` with its own `CameraComponent`. It's the editor's viewport camera. Don't try to delete or repurpose it; it'll reappear. When `IsMainCamera = true` is set on your scene's Camera and `Priority` is reasonable, the game's play viewport uses yours, not the editor's.

## MCP bridge — what to trust, what to verify yourself

The Claude bridge (file-IPC: `LouSputthole/Sbox-Claude`, package `sboxskinsgg/claudebridge`) is the most reliable MCP we've used. But some tools have caveats:

### `take_screenshot` may NOT show the play camera's view

`take_screenshot` calls `EditorScene.TakeHighResScreenshot`, which renders from the **editor's free-fly viewport camera**, not the play scene's `CameraComponent`. During play, the editor viewport stays parked wherever the user last had it. If you `set_transform` the main Camera and then `take_screenshot`, you're capturing a completely unrelated angle.

**Use the user's Win+Shift+S screenshot as ground truth** when verifying what the player actually sees. The bridge screenshot is fine for verifying the scene is populated (you can see objects from any angle) but not for verifying camera framing.

### `is_playing` returns mixed signals

The return object has multiple flags:
```
{ "isPlaying": true, "isPaused": false, "gameFlag": true, "tracked": false, "sessionPlaying": false }
```
After certain crash/recover cycles `isPlaying=true` while `sessionPlaying=false` — the editor thinks play is on but the play session isn't actually live. Symptom: `start_play` fails with `Assert: IsValid` / `Attempted to create new GameEditorSession when one already exists!`. Fix: `stop_play` first, then `start_play`.

### Scene mutations refuse during play

`create_gameobject`, `set_property`, `delete_gameobject`, etc. refuse during play mode (clear error). Always `stop_play` before scene edits. `set_property` calls on Component **runtime** values during play go through `set_runtime_property` instead.

### Bridge sometimes lets through "refused" deletes

Observed: calling `delete_gameobject` during play mode returns `{"deleted": true}` but the object persists. If a delete didn't take effect, check `scene_get_hierarchy` and retry post-`stop_play`.

### Bridge crashes / port conflicts

Port 8098 (the bridge's HTTP listener) can collide with `spd.exe` (cFosSpeed) or stale `sbox-dev.exe` instances. Symptoms: bridge fails to start with disposed-`HttpListener` error, or another process owns the port. Fix:
```bash
netstat -ano | findstr :8098            # find owner
netsh http show servicestate view=session | grep -B1 -A6 "8098"  # owning PID
taskkill /F /PID <pid>                  # kill if it's a duplicate sbox-dev
net stop cfosspeeds                     # if cFosSpeed is grabbing the port
```
Then close + reopen the editor cleanly. The bridge will rebind to 8098.

### Use the right tool to read runtime state

- `get_property` reads from the **editor scene** (works in edit mode).
- `get_runtime_property` reads from the **play scene** (works in play mode).

During play, `get_property` may return stale or empty data. `get_runtime_property` reads the live state. Both accept `id` (GameObject GUID), `component` (type name string), `property` (name string).

## Runtime GameObject spawn — what works

Both of these patterns work at runtime to create a renderable GameObject (subject to the cloud-asset caveat above):

```csharp
// Pattern A: old-school
var go = new GameObject { Name = "Foo" };
go.WorldPosition = ...;
go.LocalScale = ...;
var mr = go.AddComponent<ModelRenderer>();
mr.Model = ...;
mr.Tint = ...;

// Pattern B: explicit
var go = Scene.CreateObject(true);
go.Name = "Foo";
go.WorldPosition = ...;
go.LocalScale = ...;
var mr = go.AddComponent<ModelRenderer>();
mr.Model = ...;
mr.Tint = ...;
```

Both produce a valid, registered GameObject. Neither needs `NetworkSpawn()` for rendering (NetworkSpawn is only required for networked sync, which the bridge handles separately even in single-player). Confirmed Pattern A renders the orange ground plane / yellow cube / red cube earlier in our debug session.

### Parenting

`go.SetParent(parentGameObject)` works. Children move with the parent. Children of the MatchHost GameObject rendered correctly in our test once cloud-asset issue was fixed; the earlier "parented children don't render" suspicion was a red herring caused by the cloud-asset bug masking everything.

## Scene-spawn order matters slightly

`AddComponent<T>()` triggers component lifecycle (`OnStart` etc.) on the next frame. Setting `mr.Model` BEFORE the next frame is fine. Setting `LocalScale` and `WorldPosition` in either order works.

## DrawGizmos vs ModelRenderer

`DrawGizmos()` is editor-only — it fires every editor frame when the GameObject is selected (or always, depending on `Gizmo.IsHovered`/`IsSelected` checks inside). It's for debug visuals using `Gizmo.Draw.LineBBox`, `Gizmo.Draw.Line`, etc. **It does not render in play mode.**

ModelRenderer is the only thing that renders in play mode. If your edit-mode view shows wireframes but play mode shows nothing, you're relying on DrawGizmos when you need a ModelRenderer.

## When new lessons surface

This skill is the canonical place to capture s&box rendering / runtime quirks discovered during dev. When something behaves unexpectedly and the explanation is non-obvious:

1. Add an entry here with: symptom, cause, fix, verified-on date.
2. Cross-reference from `CLAUDE.md` if it's project-impacting.
3. Re-read this skill at the start of any new s&box rendering / spawn task — half the wins come from not re-discovering documented bugs.

## Related skills

- `sbox-longshot-port` — concrete API guidance for the LongShot project (assembly inclusion, MSBuild vs s&box compiler, namespace shadowing, etc.). Different scope; some overlap on rendering hints.

## Sources

- Cloud Assets doc: https://sbox.game/dev/doc/assets/resources/cloud-assets.md
- Scene API: https://sbox.game/dev/doc/scene.md
- GameObject API: https://sbox.game/dev/doc/scene/gameobject.md
- Prefabs (runtime instantiation): https://sbox.game/dev/doc/scene/prefabs.md
- scenestaging Gun.cs (runtime spawn reference): https://github.com/Facepunch/sbox-scenestaging/blob/main/Code/ExampleComponents/Gun.cs
