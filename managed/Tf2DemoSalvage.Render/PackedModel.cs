using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Render;

/// <summary>One model's own geometry, ready to become a static mesh.</summary>
/// <param name="Vertices">Its triangles in model space, and only its own.</param>
/// <param name="Frames">Its runs per baked animation frame, offset within its OWN vertices.</param>
/// <remarks>
/// **The offsets are local to this model, which is the whole point of the type.** The packing keeps
/// every model's vertices in one shared list and records batch offsets into that list; a per-model
/// static mesh needs them rebased to zero, and doing that at the seam keeps the renderer from
/// knowing anything about the shared list at all.
///
/// This is the shape `CreateStaticMesh` expects — one mesh, its own vertices, its own runs — rather
/// than a window onto a larger buffer.
/// </remarks>
public sealed record PackedModel(
    IReadOnlyList<WorldVertex> Vertices,
    IReadOnlyList<IReadOnlyList<WorldBatch>> Frames);
