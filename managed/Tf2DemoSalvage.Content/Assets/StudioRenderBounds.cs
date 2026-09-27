using System;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>A model's render bounds, as <c>C_BaseAnimating::GetRenderBounds</c> would report them at rest.</summary>
/// <remarks>
/// **The header carries two boxes** — see <see cref="StudioLayout.HeaderHullMinOffset"/>'s own remarks:
/// <c>view_bbmin</c>/<c>view_bbmax</c> (the "clipping bounding box") when the modeller authored one, else
/// <c>hull_min</c>/<c>hull_max</c> (the "ideal movement hull size"). Nothing here reads a model's actual mesh
/// vertices — the header's own boxes are exactly what <c>CBaseModelPanel::LookAtBounds</c> (basemodel_panel.cpp:392,
/// via <c>GetBoundingBox</c>) fits against too, so this is not an approximation of what Valve reads, it is the same
/// two fields.
/// </remarks>
public static class StudioRenderBounds
{
    /// <summary>The view box if authored (either extent non-zero), else the hull box.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>Min and max corners, model space. Both default (0,0,0) when the file is too short to hold them.</returns>
    public static ((float X, float Y, float Z) Min, (float X, float Y, float Z) Max) Of(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        (float X, float Y, float Z) viewMin = ReadVector(bytes, StudioLayout.HeaderViewBoundsMinOffset);
        (float X, float Y, float Z) viewMax = ReadVector(bytes, StudioLayout.HeaderViewBoundsMaxOffset);

        if (viewMin != default || viewMax != default)
        {
            return (viewMin, viewMax);
        }

        return (ReadVector(bytes, StudioLayout.HeaderHullMinOffset), ReadVector(bytes, StudioLayout.HeaderHullMaxOffset));
    }

    private static (float X, float Y, float Z) ReadVector(ReadOnlySpan<byte> bytes, int offset)
    {
        if (bytes.Length < offset + 12)
        {
            return default;
        }

        return (
            BitConverter.ToSingle(bytes.Slice(offset, 4)),
            BitConverter.ToSingle(bytes.Slice(offset + 4, 4)),
            BitConverter.ToSingle(bytes.Slice(offset + 8, 4)));
    }
}
