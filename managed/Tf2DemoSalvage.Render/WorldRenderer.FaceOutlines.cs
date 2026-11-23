using System;
using System.Collections.Generic;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>The world's and brush entities' face outlines, which <c>mat_wireframe 3</c> draws (B803).</summary>
internal sealed unsafe partial class WorldRenderer
{
    /// <summary>The world buffer's outline, <see cref="FaceFan.Outline"/>: two indices per vertex.</summary>
    private ComPtr<ID3D11Buffer> _worldOutline;

    /// <summary>Each brush entity's outline, by its <c>*N</c> path, beside its <see cref="_modelBuffers"/> entry.</summary>
    private readonly Dictionary<string, ComPtr<ID3D11Buffer>> _modelOutlines = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A vertex buffer's face outline as an index buffer, or a null handle for no vertices.</summary>
    /// <remarks>
    /// Built at upload because the faces are gone by draw time: the batches hold triangles, and only each corner's
    /// <see cref="WorldVertex.FaceEdge"/> still says which edges were the polygon's. Eight bytes a vertex, for a value most
    /// sessions never set; the engine holds the loop for every surface anyway (its vertex-index table).
    /// </remarks>
    private static ComPtr<ID3D11Buffer> CreateOutline(ComPtr<ID3D11Device> device, IReadOnlyList<WorldVertex> vertices)
    {
        ComPtr<ID3D11Buffer> buffer = default;

        if (vertices.Count == 0)
        {
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
    /// <remarks>
    /// The engine's `MATERIAL_LINES` (primitive type 1 at `FUN_1800e6780`'s mesh setup). Two indices per vertex, so the run's
    /// lines are <c>2 × first</c> for <c>2 × count</c> in the outline.
    /// </remarks>
    private static void DrawOutline(ComPtr<ID3D11DeviceContext> context, WorldBatch batch, ComPtr<ID3D11Buffer> outline)
    {
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist);
        context.IASetIndexBuffer(outline, Silk.NET.DXGI.Format.FormatR32Uint, 0);
        SubmitCounters.Draw(); context.DrawIndexed((uint)(2 * batch.VertexCount), (uint)(2 * batch.FirstVertex), 0);
        context.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
    }

    /// <summary>Releases the world's outline with its buffer.</summary>
    private void ReleaseWorldOutline()
    {
        if (_worldOutline.Handle is not null)
        {
            _worldOutline.Dispose();
            _worldOutline = default;
        }
    }

    /// <summary>Releases every brush entity's outline with the model buffers.</summary>
    private void ReleaseModelOutlines()
    {
        foreach (ComPtr<ID3D11Buffer> outline in _modelOutlines.Values)
        {
            outline.Dispose();
        }

        _modelOutlines.Clear();
    }
}
