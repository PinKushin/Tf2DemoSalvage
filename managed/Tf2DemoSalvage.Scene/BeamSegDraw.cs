using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>One control point of a beam strip — <c>BeamSeg_t</c> (`tier2/beamsegdraw.h:29`).</summary>
/// <param name="Position"><c>m_vPos</c>, the strip's centre line here.</param>
/// <param name="Colour"><c>m_vColor</c>, 0 to 1 per channel.</param>
/// <param name="TexCoord"><c>m_flTexCoord</c>, the V coordinate; U runs 0 to 1 across the strip.</param>
/// <param name="Width"><c>m_flWidth</c>, the WHOLE width — the strip reaches half of it either side.</param>
/// <param name="Alpha"><c>m_flAlpha</c>.</param>
public readonly record struct BeamSegment(Vector3 Position, Vector3 Colour, float TexCoord, float Width, float Alpha);

/// <summary>
/// <c>CBeamSegDraw</c>: a camera-facing strip through a list of points — what every beam, sprite trail and rope is
/// drawn as.
/// </summary>
/// <remarks>
/// **The implementation is closed** — `tier2.lib`, statically linked into `client.dll` — so this is read out of
/// `beamsegdraw.obj` extracted from the SDK's own `src/lib/public/x64/tier2.lib` (Ghidra, `D:\ghidra-proj\tier2`):
/// <c>NextSeg</c> at `0x31290` and <c>SpecifySeg</c> at `0x31a40`. What they do, settled in the disassembly:
///
/// <code>
/// NextSeg( seg ):
///     camera = renderContext-&gt;GetWorldSpaceCameraPosition()              // vtable +0x4a0
///     if ( segsDrawn &gt; 0 )
///         n = fastnormalize( cross( m_Seg.pos − seg.pos, m_Seg.pos − camera ) )
///         ave = segsDrawn &gt; 1 ? fastnormalize( ( n + normalLast ) · 0.5 ) : n
///         normalLast = n
///         SpecifySeg( camera, ave )
///     m_Seg = seg; ++segsDrawn
///     if ( segsDrawn == totalSegs ) SpecifySeg( camera, normalLast )
///
/// SpecifySeg( camera, normal ):
///     p1 = m_Seg.pos + normal · width · 0.5      uv ( 0, texcoord )
///     p2 = m_Seg.pos − normal · width · 0.5      uv ( 1, texcoord )
///     colour = ( m_vColor, m_flAlpha ), packed to bytes
/// </code>
///
/// emitted as a <c>MATERIAL_TRIANGLE_STRIP</c> of <c>( nSegs − 1 ) · 2</c> triangles. **The normal is averaged with
/// the previous pair's, unaveraged one** — <c>m_vNormalLast</c> stores the raw normal, so a bend is mitred by half, not
/// by the running average.
///
/// **`fastnormalize` is <c>v · rsqrt( |v|² + 1e-10 )</c>** with one Newton step (`0x2edbe6ff` is 1e-10), so a zero
/// vector stays zero rather than becoming NaN: a strip folded back on itself, or a segment pointing at the camera, gets
/// a zero normal and draws as a line of no width. Taken here as the exact reciprocal root the Newton step converges to.
///
/// **Six corners per quad, two triangles**, because the renderer draws lists; the strip's own alternation would wind
/// every other triangle backwards, and the particle pass does not cull either way.
/// </remarks>
public static class BeamSegDraw
{
    /// <summary>The 1e-10 <c>VectorNormalizeFast</c> adds under the root (`beamsegdraw.obj` `0x2edbe6ff`).</summary>
    private const float NormalizeEpsilon = 1e-10f;

    /// <summary>Draws one strip through every segment, into six corners per quad.</summary>
    /// <param name="segments">The control points, in order; fewer than two draw nothing.</param>
    /// <param name="camera">Where the camera is — the strip turns to face it.</param>
    /// <param name="into">Where the corners go.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    public static void Draw(ReadOnlySpan<BeamSegment> segments, Vector3 camera, ICollection<DetailSpriteVertex> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (segments.Length < 2)
        {
            return;
        }

        Vector3 normalLast = Vector3.Zero;
        (Vector3 One, Vector3 Two, BeamSegment Of)? previous = null;

        for (int drawn = 0; drawn < segments.Length; drawn++)
        {
            if (drawn == 0)
            {
                continue;
            }

            BeamSegment current = segments[drawn - 1];
            BeamSegment next = segments[drawn];

            Vector3 normal = FastNormalize(Vector3.Cross(current.Position - next.Position, current.Position - camera));
            Vector3 averaged = drawn > 1 ? FastNormalize((normal + normalLast) * 0.5f) : normal;

            normalLast = normal;

            previous = Specify(current, averaged, previous, into);
        }

        // The last point takes the last pair's raw normal: `SpecifySeg( camera, m_vNormalLast )`.
        Specify(segments[^1], normalLast, previous, into);
    }

    /// <summary><c>SpecifySeg</c>: the two edge points at one control point, joined to the previous pair.</summary>
    private static (Vector3 One, Vector3 Two, BeamSegment Of) Specify(
        BeamSegment segment,
        Vector3 normal,
        (Vector3 One, Vector3 Two, BeamSegment Of)? previous,
        ICollection<DetailSpriteVertex> into)
    {
        Vector3 half = normal * (segment.Width * 0.5f);
        Vector3 one = segment.Position + half;
        Vector3 two = segment.Position - half;

        if (previous is { } last)
        {
            DetailSpriteVertex lastOne = Corner(last.One, 0f, last.Of);
            DetailSpriteVertex lastTwo = Corner(last.Two, 1f, last.Of);
            DetailSpriteVertex nextOne = Corner(one, 0f, segment);
            DetailSpriteVertex nextTwo = Corner(two, 1f, segment);

            into.Add(lastOne);
            into.Add(lastTwo);
            into.Add(nextOne);

            into.Add(nextOne);
            into.Add(lastTwo);
            into.Add(nextTwo);
        }

        return (one, two, segment);
    }

    /// <summary>One corner: the edge's U, the segment's V, and its colour as the vertex buffer holds it.</summary>
    private static DetailSpriteVertex Corner(Vector3 at, float u, BeamSegment segment)
    {
        float red = Packed(segment.Colour.X);
        float green = Packed(segment.Colour.Y);
        float blue = Packed(segment.Colour.Z);
        float alpha = Packed(segment.Alpha);

        return new DetailSpriteVertex(
            at.X, at.Y, at.Z, u, segment.TexCoord, red, green, blue, alpha, u, segment.TexCoord, 0f);
    }

    /// <summary><c>v · rsqrt( |v|² + 1e-10 )</c> — <c>VectorNormalizeFast</c>, which leaves a zero vector zero.</summary>
    internal static Vector3 FastNormalize(Vector3 vector) =>
        vector / MathF.Sqrt(vector.LengthSquared() + NormalizeEpsilon);

    /// <summary>
    /// A channel as the vertex holds it: <c>(int)( x · 255 + 2²³ ) &amp; 0xff</c>, the float-to-byte trick
    /// <c>SpecifySeg</c> packs a <c>D3DCOLOR</c> with (`0x4b000000` and `0x437f0000` at `0x31a40`).
    /// </summary>
    /// <remarks>
    /// **Rounded to nearest and WRAPPED, not clamped**: the low byte of the rounded integer is kept, so a channel over
    /// one comes back small. Every caller here hands it a value in 0..1, where the two agree; the wrap is stated so a
    /// caller that did not would see the engine's answer rather than a tidier one.
    /// </remarks>
    internal static float Packed(float channel) =>
        ((int)MathF.Round(channel * 255f, MidpointRounding.ToEven) & 0xFF) / 255f;
}
