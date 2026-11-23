using System;
using System.Collections.Generic;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>The world's and brush entities' face outlines, which <c>mat_wireframe 3</c> draws (B803).</summary>
internal sealed unsafe partial class WorldRenderer
{
    /// <summary>The world buffer's outline, <see cref="FaceFan.Outline"/>: two indices per vertex.</summary>
    private ComPtr<ID3D11Buffer> _worldOutline;

    /// <summary>Each brush entity's outline and its faces' planes, by its <c>*N</c> path, beside its <see cref="_modelBuffers"/> entry.</summary>
    private readonly Dictionary<string, (ComPtr<ID3D11Buffer> Buffer, FacePlane[] Faces)> _modelOutlines =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A vertex buffer's face outline as an index buffer, or a null handle for no vertices.</summary>
    /// <remarks>
    /// Built at upload because the faces are gone by draw time: the batches hold triangles, and only each corner's
    /// <see cref="WorldVertex.FaceEdge"/> still says which edges were the polygon's. Eight bytes a vertex, for a value most
    /// sessions never set; the engine holds the loop for every surface anyway (its vertex-index table). What that costs on
    /// a real map is in RISKS B803, "Review 1".
    ///
    /// **A buffer that is not whole triangles gets none, and says so** (B803 review 2): the outline is built at every
    /// upload whatever <c>mat_wireframe</c> is, so refusing it must not fail the map. Its runs then draw their triangles
    /// under 3, as under 1.
    /// </remarks>
    private ComPtr<ID3D11Buffer> CreateOutline(ComPtr<ID3D11Device> device, IReadOnlyList<WorldVertex> vertices, string owner)
    {
        ComPtr<ID3D11Buffer> buffer = default;

        if (vertices.Count == 0)
        {
            return buffer;
        }

        if (vertices.Count % 3 != 0)
        {
            // Once per map, not per brush entity (review 3, Sonnet): one cause, a builder bug, would name every model.
            if (!_partialOutlineReported)
            {
                _partialOutlineReported = true;
                _render.LogWarning(
                    "{Message}",
                    $"{owner}: {vertices.Count} vertices are not whole triangles, so mat_wireframe 3 draws its triangles, not "
                    + "its faces' outline; any more in this map are not reported");
            }

            return buffer;
        }

        uint[] indices = FaceFan.Outline(vertices);
        BufferDesc description = new()
        {
            ByteWidth = (uint)(indices.Length * sizeof(uint)),
            Usage = Usage.Immutable,
            BindFlags = (uint)BindFlag.IndexBuffer,
        };

        fixed (uint* first = indices)
        {
            SubresourceData initial = new() { PSysMem = first };

            SilkMarshal.ThrowHResult(device.CreateBuffer(in description, in initial, ref buffer));
        }

        return buffer;
    }

    /// <summary>Whether a queued wireframe run draws as its faces' outline rather than its triangles (B803).</summary>
    /// <param name="batch">The queued run.</param>
    /// <returns>True for a world or brush surface under <c>mat_wireframe 3</c>; a displacement keeps its triangles.</returns>
    /// <remarks>
    /// `engine.dll` `FUN_1800dc720` takes `FUN_1800e6780` for 3 on every list it is handed, and a displacement is never in one:
    /// `FUN_1800c2140` draws its `debugwireframe` mesh itself, whatever the value.
    /// </remarks>
    private bool DrawsOutline(WorldBatch batch) => Wireframe == 3 && batch.Category is not SurfaceCategory.Terrain;

    /// <summary>Draws a run's face outline as a line list, then hands the triangle list back.</summary>
    /// <param name="context">The device context.</param>
    /// <param name="batch">The run, as many vertices as the cap leaves of it.</param>
    /// <param name="outline">The bound vertex buffer's outline.</param>
    /// <param name="faces">The buffer's faces, to leave out those whose back the eye sees, or null to draw every one.</param>
    /// <param name="eye">The eye in the buffer's own space, read only with <paramref name="faces"/>; null draws every face.</param>
    /// <param name="cap">The B597 vertex cap, as the run's solid draw takes it; null draws the run whole.</param>
    /// <param name="translucent">Whether the run's own material draws in the translucent pass.</param>
    /// <param name="noCull">Whether the run's own material is <c>$nocull</c>.</param>
    /// <remarks>
    /// The engine's `MATERIAL_LINES` (primitive type 1 at `FUN_1800e6780`'s mesh setup). Two indices per vertex, so the run's
    /// lines are <c>2 × first</c> for <c>2 × count</c> in the outline.
    ///
    /// **A brush entity's back faces are left out by their planes, by the rule of the pass that draws them** (B803 reviews
    /// 1-3), `FacePlane.OutlinedFrom`. *Read*: the opaque path `FUN_1800dcb20` tests the plane of each surface on
    /// `FUN_1800df1a0`'s list (every surface without flag `0x20`), `dot − dist ≥ −0.01`, no flag read; the translucent path
    /// `FUN_1800de800` collects through `FUN_1800dbdb0`, a back-to-front walk of the model's nodes that keeps a surface with
    /// flag `0x20` that is NOCULL (`0x200`) or whose plane-back flag (`0x40`) matches the eye's side of its node, no
    /// tolerance; both lists reach `FUN_1800dc720`. *Interpolated*: that flag `0x20` is the viewer's blended materials
    /// (`IsBlendedMaterial`), that a face's node plane is its own, and that an eye exactly on a plane-back face's plane is
    /// negligible. *Read*: flag `0x200` is NOCULL, set from the material's <c>$nocull</c> (`0x1800fa3d0`, B457,
    /// <see cref="IsTwoSidedMaterial"/>), the viewer's <c>_noCull</c>. *Not read*: that `0x20` and `0x40` are set the
    /// same way, from the surface's own material and plane. A line is never culled by the rasteriser, so without the planes a door seen from behind was outlined
    /// under 3 and not under 1 or 2. The world needs none: its walk already leaves out a surface whose plane the eye is
    /// behind (<c>VisibleWorld.Surfaces</c>, `R_DrawLeaf`), keeping a NOCULL one.
    /// </remarks>
    private void DrawOutline(
        ComPtr<ID3D11DeviceContext> context,
        WorldBatch batch,
        ComPtr<ID3D11Buffer> outline,
        FacePlane[]? faces,
        Vector3? eye,
        int? cap,
        bool translucent,
        bool noCull)
    {
        int first = batch.FirstVertex;
        int end = first + ModelVertexClamp.Apply(batch.VertexCount, cap);

        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist);
        context.IASetIndexBuffer(outline, Silk.NET.DXGI.Format.FormatR32Uint, 0);

        if (faces is null || eye is not { } seenFrom)
        {
            DrawLines(context, first, end);
        }
        else
        {
            FaceFan.Runs(faces, first, end, (seenFrom.X, seenFrom.Y, seenFrom.Z), translucent, noCull, _outlineRuns);

            foreach ((int from, int to) in _outlineRuns)
            {
                DrawLines(context, from, to);
            }
        }

        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
    }

    /// <summary>The ranges <see cref="FaceFan.Runs"/> fills for the outline being drawn, kept so a draw allocates nothing.</summary>
    private readonly List<(int First, int End)> _outlineRuns = [];

    /// <summary>The lines of the vertices from <paramref name="first"/> to before <paramref name="end"/>.</summary>
    /// <remarks>Never handed a reversed range: <see cref="FaceFan.Runs"/> yields none, and a whole run's count is not negative.</remarks>
    private static void DrawLines(ComPtr<ID3D11DeviceContext> context, int first, int end)
    {
        SubmitCounters.Draw(); context.DrawIndexed((uint)(2 * (end - first)), (uint)(2 * first), 0);
    }

    /// <summary>The eye in a model's own space, from its placement: the space its faces' planes are in.</summary>
    /// <param name="matrix">The model's placement, sixteen floats row major, row vectors.</param>
    /// <param name="eye">The eye in the world, or null before any camera was set.</param>
    /// <returns>
    /// The eye moved by the placement's inverse, or null where it cannot be known — no eye, or a placement with no
    /// inverse — and then no face is left out (B803 review 2): an outline drawn whole is the safe error, a face lost is not.
    /// </returns>
    internal static Vector3? ModelEye(float[] matrix, (float X, float Y, float Z)? eye)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (eye is not { } at)
        {
            return null;
        }

        Matrix4x4 placement = new(
            matrix[0], matrix[1], matrix[2], matrix[3],
            matrix[4], matrix[5], matrix[6], matrix[7],
            matrix[8], matrix[9], matrix[10], matrix[11],
            matrix[12], matrix[13], matrix[14], matrix[15]);

        return Matrix4x4.Invert(placement, out Matrix4x4 inverse) ? Vector3.Transform(new Vector3(at.X, at.Y, at.Z), inverse) : null;
    }

    /// <summary>Whether this map's upload has already said a buffer was not whole triangles.</summary>
    private bool _partialOutlineReported;

    /// <summary>Releases the world's outline with its buffer, and lets the next map report a partial triangle again.</summary>
    private void ReleaseWorldOutline()
    {
        _partialOutlineReported = false;

        if (_worldOutline.Handle is not null)
        {
            _worldOutline.Dispose();
            _worldOutline = default;
        }
    }

    /// <summary>Releases every brush entity's outline with the model buffers.</summary>
    /// <remarks>
    /// A model with no outline (a partial triangle, review 2) holds a null handle, skipped as the world's is (review 3).
    /// Silk's <c>Dispose</c> of a null handle was measured a no-op too: the release test stays green with the check removed.
    /// </remarks>
    private void ReleaseModelOutlines()
    {
        foreach ((ComPtr<ID3D11Buffer> outline, _) in _modelOutlines.Values)
        {
            if (outline.Handle is not null)
            {
                outline.Dispose();
            }
        }

        _modelOutlines.Clear();
    }
}
