using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Longshot;

/// <summary>
/// Runtime mesh generation helpers. Builds <see cref="Model"/>s from scratch using s&amp;box's
/// SimpleVertex + Mesh + ModelBuilder pipeline — no dependency on baked .vmdl assets.
///
/// Why this exists: mounted cloud .vmdl assets silently fail to render when assigned to a
/// ModelRenderer at runtime (see <c>.claude/skills/sbox-runtime-rendering/SKILL.md</c>).
/// Procedurally-generated models bypass that entirely; the vertex buffers go straight to
/// the GPU and render correctly under any spawn pattern.
///
/// Pattern verified against Facepunch/sbox-public mounting code (QuakeModel.cs).
/// </summary>
internal static class ProceduralMeshes
{
    /// <summary>
    /// Build a centred axis-aligned box of given half-extents. The box has 24 vertices (4 per
    /// face × 6 faces) so each face has its own normals + UVs — required for flat shading and
    /// per-face material assignment.
    /// </summary>
    /// <param name="halfExtents">Half-size of the box on each axis (in s&amp;box units).</param>
    /// <param name="material">Material to render with. Pass <c>null</c> to use the default tint-only material.</param>
    /// <returns>A renderable <see cref="Model"/> that can be assigned to <c>ModelRenderer.Model</c>.</returns>
    public static Model BuildBox(Vector3 halfExtents, Material material = null)
    {
        // Default to the built-in materials/default.vmat — a plain white PBR material that
        // ModelRenderer.Tint can multiply against. Material.Create with a shader name produces
        // an unfilled placeholder that renders as the magenta-checker "missing texture" stand-in,
        // which is technically functional but visually broken. Loading a real .vmat ships pre-baked.
        material ??= Material.Load("materials/default.vmat");
        var mesh = new Mesh(material);

        // Box centred at origin, half-extents (hx, hy, hz). Eight corner points:
        float hx = halfExtents.x, hy = halfExtents.y, hz = halfExtents.z;
        var c000 = new Vector3(-hx, -hy, -hz);
        var c100 = new Vector3( hx, -hy, -hz);
        var c010 = new Vector3(-hx,  hy, -hz);
        var c110 = new Vector3( hx,  hy, -hz);
        var c001 = new Vector3(-hx, -hy,  hz);
        var c101 = new Vector3( hx, -hy,  hz);
        var c011 = new Vector3(-hx,  hy,  hz);
        var c111 = new Vector3( hx,  hy,  hz);

        // Six faces. Each face: 4 vertices (CCW from outside view) + 2 triangles.
        // CCW winding (looking from outside) → normal points outward → renders to camera.
        var verts = new List<SimpleVertex>(24);
        var indices = new List<int>(36);

        // +X face (right)
        AddQuad(verts, indices, c100, c110, c111, c101, Vector3.Right);
        // -X face (left)
        AddQuad(verts, indices, c010, c000, c001, c011, Vector3.Left);
        // +Y face (forward / "front" in s&box's +Y-left convention)
        AddQuad(verts, indices, c110, c010, c011, c111, Vector3.Forward);
        // -Y face (back)
        AddQuad(verts, indices, c000, c100, c101, c001, Vector3.Backward);
        // +Z face (up)
        AddQuad(verts, indices, c001, c101, c111, c011, Vector3.Up);
        // -Z face (down)
        AddQuad(verts, indices, c010, c110, c100, c000, Vector3.Down);

        mesh.CreateVertexBuffer(verts.Count, verts);
        mesh.CreateIndexBuffer(indices.Count, indices);
        mesh.Bounds = new BBox(-halfExtents, halfExtents);

        return Model.Builder
            .WithName($"procbox-{halfExtents.x:0.##}x{halfExtents.y:0.##}x{halfExtents.z:0.##}")
            .AddMesh(mesh)
            .Create();
    }

    /// <summary>
    /// Build a centred axis-aligned slate/floor — a thin box with the top face at world Z = 0
    /// (i.e. centred at (0, 0, -thickness/2)). The slate's playfield surface ends up flush with
    /// the world origin plane, matching how the physics engine treats Y = 0 as the table top.
    /// </summary>
    /// <param name="width">Total X extent (units).</param>
    /// <param name="depth">Total Y extent (units).</param>
    /// <param name="thickness">Z extent (units).</param>
    public static Model BuildSlate(float width, float depth, float thickness, Material material = null) =>
        BuildBox(new Vector3(width * 0.5f, depth * 0.5f, thickness * 0.5f), material);

    /// <summary>
    /// Extrude a 2D outline polygon vertically (along Z) into a prism. The outline is taken as a
    /// closed loop of points in the XY plane (each point connects to the next, and the last to the
    /// first). Caps are emitted at <c>z = ±halfHeight</c> with per-face normals; side walls are
    /// emitted one per edge, also flat-shaded.
    /// <para>
    /// Winding contract: the outline MUST be CCW when viewed from +Z. The top cap then faces +Z,
    /// the bottom cap faces -Z, and side-wall normals point outward (right of each edge direction).
    /// Reversed winding will yield back-facing geometry that renders as invisible.
    /// </para>
    /// <para>
    /// Triangulation: fan from <c>outline[0]</c> for the caps. This is only valid for convex
    /// polygons; the rail outline (hexagon with filleted jaw corners) is convex by construction.
    /// </para>
    /// </summary>
    /// <param name="outline">Closed-loop 2D points in s&amp;box units, CCW when viewed from +Z.</param>
    /// <param name="halfHeight">Half the prism's vertical extent (units). The mesh spans Z ∈ [-halfHeight, +halfHeight].</param>
    /// <param name="material">Material; defaults to <c>materials/default.vmat</c> if null.</param>
    public static Model BuildPrism(IList<Vector2> outline, float halfHeight, Material material = null)
    {
        if (outline is null || outline.Count < 3)
            throw new System.ArgumentException("outline must have at least 3 points", nameof(outline));

        material ??= Material.Load("materials/default.vmat");
        var mesh = new Mesh(material);

        int n = outline.Count;
        // Cap verts: n top + n bottom. Wall verts: 4 per edge × n edges = 4n. Total = 6n.
        var verts = new List<SimpleVertex>(6 * n);
        var indices = new List<int>(6 * n * 3);

        // -------- Top cap (z = +halfHeight, normal = +Z, CCW from above) --------
        int topBase = verts.Count;
        for (int i = 0; i < n; i++)
        {
            var p = outline[i];
            verts.Add(new SimpleVertex(
                new Vector3(p.x, p.y, +halfHeight),
                Vector3.Up,
                new Vector3(1, 0, 0),
                new Vector2(p.x, p.y)));   // planar XY UV - matches the slate convention
        }
        for (int i = 1; i < n - 1; i++)
        {
            indices.Add(topBase + 0);
            indices.Add(topBase + i);
            indices.Add(topBase + i + 1);
        }

        // -------- Bottom cap (z = -halfHeight, normal = -Z, reverse winding) --------
        int bottomBase = verts.Count;
        for (int i = 0; i < n; i++)
        {
            var p = outline[i];
            verts.Add(new SimpleVertex(
                new Vector3(p.x, p.y, -halfHeight),
                Vector3.Down,
                new Vector3(1, 0, 0),
                new Vector2(p.x, p.y)));
        }
        for (int i = 1; i < n - 1; i++)
        {
            indices.Add(bottomBase + 0);
            indices.Add(bottomBase + i + 1);   // swapped vs top for CW-from-above → CCW-from-below
            indices.Add(bottomBase + i);
        }

        // -------- Side walls (one flat-shaded quad per edge) --------
        // For a CCW polygon viewed from +Z, the OUTWARD normal of edge p0 → p1 is on the right
        // of the edge direction; rotating (dx, dy) by -90° in XY gives (dy, -dx). Each wall has
        // 4 unique verts so the edge can be flat-shaded without averaging across the seam.
        for (int i = 0; i < n; i++)
        {
            var p0 = outline[i];
            var p1 = outline[(i + 1) % n];

            float dx = p1.x - p0.x;
            float dy = p1.y - p0.y;
            float edgeLen = MathF.Sqrt(dx * dx + dy * dy);
            if (edgeLen < 1e-5f) continue;          // degenerate edge - skip

            float inv = 1f / edgeLen;
            var tangent = new Vector3(dx * inv, dy * inv, 0);
            var normal  = new Vector3(dy * inv, -dx * inv, 0);   // right-of-edge = outward

            int baseIdx = verts.Count;
            // CCW from outside: bottom-p0 → bottom-p1 → top-p1 → top-p0
            verts.Add(new SimpleVertex(new Vector3(p0.x, p0.y, -halfHeight), normal, tangent, new Vector2(0, 0)));
            verts.Add(new SimpleVertex(new Vector3(p1.x, p1.y, -halfHeight), normal, tangent, new Vector2(1, 0)));
            verts.Add(new SimpleVertex(new Vector3(p1.x, p1.y, +halfHeight), normal, tangent, new Vector2(1, 1)));
            verts.Add(new SimpleVertex(new Vector3(p0.x, p0.y, +halfHeight), normal, tangent, new Vector2(0, 1)));

            indices.Add(baseIdx + 0);
            indices.Add(baseIdx + 1);
            indices.Add(baseIdx + 2);
            indices.Add(baseIdx + 0);
            indices.Add(baseIdx + 2);
            indices.Add(baseIdx + 3);
        }

        mesh.CreateVertexBuffer(verts.Count, verts);
        mesh.CreateIndexBuffer(indices.Count, indices);

        // Bounds from outline extents — required for renderer culling.
        float xmin = float.MaxValue, xmax = float.MinValue;
        float ymin = float.MaxValue, ymax = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            var p = outline[i];
            if (p.x < xmin) xmin = p.x;
            if (p.x > xmax) xmax = p.x;
            if (p.y < ymin) ymin = p.y;
            if (p.y > ymax) ymax = p.y;
        }
        mesh.Bounds = new BBox(new Vector3(xmin, ymin, -halfHeight), new Vector3(xmax, ymax, +halfHeight));

        return Model.Builder
            .WithName($"procprism-n{n}-h{halfHeight:0.##}")
            .AddMesh(mesh)
            .Create();
    }

    /// <summary>
    /// Add a quad face: 4 vertices in CCW order (viewed from the outside, normal direction).
    /// Emits 4 verts + 6 indices (two triangles: 0-1-2 and 0-2-3).
    /// </summary>
    private static void AddQuad(List<SimpleVertex> verts, List<int> indices,
        Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal)
    {
        int baseIdx = verts.Count;

        // Tangent in the plane of the face. Use any in-plane vector; for unlit/colored materials
        // it doesn't matter, but it must be non-zero to keep some shaders happy.
        Vector3 edge = (v1 - v0).Normal;
        Vector3 tangent = edge;  // close enough for solid-colour rendering

        verts.Add(new SimpleVertex(v0, normal, tangent, new Vector2(0, 0)));
        verts.Add(new SimpleVertex(v1, normal, tangent, new Vector2(1, 0)));
        verts.Add(new SimpleVertex(v2, normal, tangent, new Vector2(1, 1)));
        verts.Add(new SimpleVertex(v3, normal, tangent, new Vector2(0, 1)));

        indices.Add(baseIdx + 0);
        indices.Add(baseIdx + 1);
        indices.Add(baseIdx + 2);
        indices.Add(baseIdx + 0);
        indices.Add(baseIdx + 2);
        indices.Add(baseIdx + 3);
    }
}
