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

Option D — **build the mesh procedurally at runtime** (no `.vmdl` involved at all). The vertex buffer goes straight to the GPU and renders correctly regardless of spawn pattern. See the next section for the canonical recipe. Use this when you don't need authored assets — e.g. a billiards slate, rails, or any parametric geometry.

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

## Procedural meshes — the asset-free path

Verified 2026-05-17 against `Facepunch/sbox-public` (`QuakeModel.cs`). Procedurally-generated `Model`s **bypass the cloud-asset rendering bug entirely** — there's no `.vmdl` to load, no mounting, no edit-time serialization step. The vertex buffer goes straight to the GPU.

### Canonical recipe

```csharp
using Sandbox;
using System.Collections.Generic;

// 1) Get a material. CRITICAL: do NOT use Material.Create(name, shader) — that
//    produces an unfilled placeholder which renders as the magenta-checker
//    "missing texture" stand-in. Use Material.Load for a real .vmat.
var material = Material.Load("materials/default.vmat");   // plain white PBR; pairs well with ModelRenderer.Tint

// 2) Build a Sandbox.Mesh (NOT HalfEdgeMesh.Mesh — that's a different editor type).
var mesh = new Mesh(material);

// 3) Author vertices + indices. SimpleVertex has a 4-arg positional constructor:
//    (position, normal, tangent, uv0).
var verts = new List<SimpleVertex>();
var indices = new List<int>();
// ... fill verts + indices (CCW winding from outside view → outward-facing normals) ...

mesh.CreateVertexBuffer(verts.Count, verts);
mesh.CreateIndexBuffer(indices.Count, indices);
mesh.Bounds = new BBox(min, max);                          // explicit bounds for culling

// 4) Wrap the Mesh in a Model via Model.Builder.
//    Model.Builder is a STATIC PROPERTY on Model — NOT `new ModelBuilder()`.
Model model = Model.Builder
    .WithName("my-procedural-thing")
    .AddMesh(mesh)
    .Create();

// 5) Assign exactly like any other Model.
var renderer = go.AddComponent<ModelRenderer>();
renderer.Model = model;
renderer.Tint = Color.Cyan;       // multiplies against the default white material
```

For a worked example, see `src/longshot/Code/Visuals/ProceduralMeshes.cs` in this repo (`BuildBox(halfExtents, material)` — 24-vertex box with per-face normals + UVs).

### Why this works when cloud `.vmdl` doesn't

Cloud `.vmdl` assets need their mesh data baked-and-bound at compile/serialization time. `Model.Load(path)` at runtime returns metadata but the GPU buffers aren't wired up unless the model was edit-mode-serialized somewhere.

Procedural meshes build the GPU buffers in-process via `Mesh.CreateVertexBuffer` / `CreateIndexBuffer`. There's no intermediate `.vmdl` and no serialization step to fail — the renderer gets a fully-bound Model object directly.

### When to use procedural vs `models/dev/*.vmdl`

| Scenario | Recommended |
|---|---|
| Quick dev primitive (cube, sphere) | `Model.Load("models/dev/box.vmdl")` — 50u native, zero code |
| Parametric geometry (slate of size W×D×T, custom rail profiles) | Procedural — exact dimensions, no scale math |
| Anything sourced from an existing artist-authored `.vmdl` (cloud) | Author the GameObject in edit mode (Inspector slot or `create_gameobject` + `assign_model`) — see Option B/C above |
| Need per-face material assignment, runtime topology, or vertex colours | Procedural — only path that exposes the vertex buffer |

### Gotchas worth knowing

- **`Material.Create("name", "shader_name")` produces a placeholder** that renders as the magenta-checker "missing texture" stand-in. It's technically a valid `Material` but has no shader bindings filled in. Always prefer `Material.Load("materials/default.vmat")` (or another real `.vmat`). The default vmat is a plain white PBR material that `ModelRenderer.Tint` multiplies against — letting you pick colour per renderer without authoring more materials.
- **`Model.Builder` is a static property on `Model`** (not `new ModelBuilder()`). It returns a fresh `ModelBuilder` ready to chain. Pattern: `Model.Builder.WithName(...).AddMesh(...).Create()`.
- **`Sandbox.Mesh` ≠ `HalfEdgeMesh.Mesh`**. There are two types named `Mesh` in the s&box surface:
  - `Sandbox.Mesh` — the renderable mesh you pass to `Model.Builder.AddMesh`. Constructor takes a `Material`. Use this for runtime rendering.
  - `HalfEdgeMesh.Mesh` — an editor-side half-edge structure for authoring tools. NOT what `Model.Builder.AddMesh` accepts.

  Get the alias right: `using Sandbox;` then `new Mesh(material)` resolves to `Sandbox.Mesh`. If the compiler chooses the half-edge type, it means another `using` shadowed it — fully-qualify with `new Sandbox.Mesh(material)`.
- **Winding order matters.** CCW from outside view → normal points outward → renders to the camera. Reversed winding will look invisible (back-face culled) even with correct vertex data. The bench's per-face quad helper (`ProceduralMeshes.AddQuad`) shows the convention.
- **One vertex per face-corner, not per cube-corner**. A flat-shaded box needs 24 vertices (4 per face × 6 faces), not 8. Shared-corner verts would average normals across faces and give Gouraud-shaded rounding instead of crisp face normals. Same applies to UVs and per-face material.
- **`mesh.Bounds` should be set explicitly**. The renderer uses it for culling. If you forget it, the mesh may render correctly when on-camera but disappear under aggressive culling.

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

### Hot-reload + compile errors fail SILENTLY — verify with glider

Verified 2026-05-17 (cost a full debug cycle). When the bridge's `trigger_hotload` is called or `start_play` is invoked and your edited C# code has a compile error, the editor **keeps running the previously-compiled assembly** with zero indication that the new code didn't load:

- `trigger_hotload` returns `"Hotload triggered — s&box is recompiling scripts"` regardless of whether the new compile succeeded
- `start_play` returns `{"started": true, "method": "EditorScene.Play"}` and the game runs fine — just with the OLD code
- The scene hierarchy, runtime properties, and screenshots all look plausible but reflect the previous build
- No error log surfaces through the bridge

**How to detect**: read a runtime property that *should* reflect the new code. In our case, `mcp__sbox__get_runtime_property` on a rail's `Model.Name` returned `"procbox-..."` (the old box-based path) when the new code would have produced `"procprism-..."` — that's the smoking gun.

**How to find the actual error fast**: load the project in glider and read diagnostics. This caught a missing `using System;` (for `MathF`) in seconds:

```
mcp__glider__load { filePath: ".../longshot.csproj" }
mcp__glider__get_diagnostics { severity: "error" }
→ CS0103: The name 'MathF' does not exist in the current context  (line 165)
```

`dotnet build` on the corresponding wrapper project (e.g. `LongShot.Engine.csproj` if the error is in engine code) also catches it, but doesn't cover code that only lives under `src/longshot/Code/` (s&box editor compiles that itself — MSBuild won't see it). Glider is the universal diagnostic for s&box game code.

Bake-it-into-the-loop tip: any time the bridge "says yes" but the runtime behaviour looks like nothing changed, run `mcp__glider__get_diagnostics` before anything else.

### `take_screenshot` ignores the `path` parameter

Verified 2026-05-17: passing `path: "screenshots/foo.png"` results in the file being saved as `<sbox-install>/screenshots/sbox.<timestamp>.png` regardless. The tool's success `note` even claims it honoured the path, but the file isn't there. To find a screenshot you just took:

```bash
# Find the sbox install root once (cache the result):
wmic process where "name='sbox-dev.exe'" get ExecutablePath /format:list
# → ExecutablePath=F:\SteamLibrary\steamapps\common\sbox 590830\sbox-dev.exe

# Then list newest screenshot:
dir /b /od "<sbox-install>\screenshots" | tail -1
# → sbox.2026.05.17.20.29.47.png
```

The filename pattern is `sbox.YYYY.MM.DD.HH.MM.SS.png` in the editor's local timezone (BST on this machine, so screenshots taken at UTC 19:29 land as `20.29.*`).

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
- Procedural mesh reference (Facepunch/sbox-public `QuakeModel.cs`): canonical `Mesh` + `Model.Builder` usage pattern verified against the engine repo
- Local worked example: `src/longshot/Code/Visuals/ProceduralMeshes.cs`
