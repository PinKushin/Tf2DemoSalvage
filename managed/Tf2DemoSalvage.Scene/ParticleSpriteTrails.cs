using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>Turns live particles into velocity-stretched quads — <c>render_sprite_trail</c> (B415).</summary>
/// <remarks>
/// **Read out of `client.dll`, because the op ships only in `particles.lib`.** The trace — the unpack table naming the
/// members, the factory and vtable naming `Render`, the per-particle builder and every constant — is in
/// `docs/findings/58`. The builder, <c>FUN_107b8d30</c> in `client-live-x86.dll`, is reproduced here statement for
/// statement:
///
/// <code>
/// fade   = age &lt; m_flLengthFadeInTime ? age / m_flLengthFadeInTime : 1           // this+0x5c
/// delta  = PREV_XYZ − XYZ
/// inv    = rsqrt( |delta|² + 1e-10 )
/// length = fade · (1/dt) · inv · |delta|² · TRAIL_LENGTH
/// if ( length &gt; 0 ) {
///     if ( m_flMaxLength &lt;= length ) length = m_flMaxLength;                  // this+0x60
///     if ( length &lt;= m_flMinLength ) length = m_flMinLength;                  // this+0x64
///     dir   = delta · inv · length
///     width = min( radius, length )
///     side  = cross( XYZ − camera, dir ) · rsqrt( |…|² + 1e-10 )
///     v0 = XYZ + side·width·0.5    v1 = XYZ − side·width·0.5    v2 = v1 + dir    v3 = v0 + dir
///     indices 0,1,2  0,2,3
/// }
/// </code>
///
/// **One quad a particle, not a ribbon** — `GetParticlesToRender` answers four vertices and six indices each. A trail
/// is a streak from where the particle IS back along where it came from, as long as its speed times its
/// <c>TRAIL_LENGTH</c>, which is why an ember flying fast is a line and one at rest collapses to a point.
///
/// **What is NOT reproduced, stated so a green test does not imply it:**
///
/// - The engine walks a back-to-front SORTED render list; this walks the store's order, as `ParticleSprites` does.
///   Additive glows — every explosion child that uses this renderer — are order-independent; a translucent one is not.
/// - Colour is quantised to bytes in the engine (<c>tint·255 + 2²³</c>, low byte) and the render list's alpha likewise;
///   the vertex here takes floats, so the difference is under half a step of 8-bit output.
/// - The render list's radius and alpha include the VISIBILITY scale (`Visibility Proxy …`), which no renderer in
///   this project evaluates.
/// - The sheet is pre-sampled 1,024 times a play-through in the engine and indexed by
///   <c>round( age · rate · 1024 )</c>; `VtfSheet.At` maps age by frame count, which agrees for equal frame durations
///   and differs by at most one sample at a boundary.
/// </remarks>
public static class ParticleSpriteTrails
{
    /// <summary>The rectangle a particle takes when its material has no sheet — <c>DAT_10c37d10</c>, <c>{0, 0, 1, 1}</c>.</summary>
    private static readonly SheetFrame Whole = VtfSheet.Whole;

    /// <summary>Builds the corners for every live particle that is moving.</summary>
    /// <param name="particles">The live collection.</param>
    /// <param name="camera">Where the view is, which the quad turns to face about its own length.</param>
    /// <param name="into">Where corners are appended; NOT cleared.</param>
    /// <param name="sheet">The material's sheet sequences, or empty for a texture that carries none.</param>
    /// <param name="rate">The renderer's <c>animation rate</c> (<c>this+0x58</c>).</param>
    /// <param name="minLength">Its <c>min length</c> (<c>this+0x64</c>).</param>
    /// <param name="maxLength">Its <c>max length</c> (<c>this+0x60</c>).</param>
    /// <param name="lengthFadeIn">Its <c>length fade in time</c> (<c>this+0x5c</c>), in seconds.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Build(
        ParticleStore particles,
        Vector3 camera,
        ICollection<DetailSpriteVertex> into,
        IReadOnlyList<SheetSequence> sheet,
        float rate,
        float minLength,
        float maxLength,
        float lengthFadeIn)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(sheet);

        // `1.0 / m_flDt`, or exactly 1 when the collection has not stepped — `_DAT_10926e30` is 0.0 and the
        // comparison is an equality.
#pragma warning disable S1244 // Valve's own exact comparison against zero.
        float perSecond = particles.LastStep == 0f ? 1f : 1f / particles.LastStep;
#pragma warning restore S1244

        for (int index = 0; index < particles.Count; index++)
        {
            float alpha = particles.AlphaOf(index);

            if (alpha <= 0f)
            {
                // The engine returns on a zero alpha byte before touching anything else.
                continue;
            }

            float age = particles.AgeOf(index);
            float fade = age < lengthFadeIn ? age / lengthFadeIn : 1f;

            Vector3 position = particles.PositionOf(index);
            Vector3 delta = particles.PreviousOf(index) - position;

            float squared = delta.LengthSquared() + VectorMath.ZeroGuard;
            float inverse = VectorMath.ReciprocalSqrt(squared);

            // `inv · |delta|²` rather than a square root, which is the engine's own arithmetic and is not the same
            // float as `MathF.Sqrt`.
            float length = fade * perSecond * inverse * squared * particles.TrailLengthOf(index);

            if (!(length > 0f))
            {
                // A zero FADE lands here — a particle on the step it was born, with a fade-in. A particle at REST does
                // not: the 1e-10 guard leaves its length about 1e-5, so it takes the branch and comes out as a quad of
                // zero area, because `along` and `side` are both zero. Same as the engine, six vertices and all.
                continue;
            }

            // Two ifs in this order, so a minimum above the maximum wins, as it does in the engine.
            if (maxLength <= length)
            {
                length = maxLength;
            }

            if (length <= minLength)
            {
                length = minLength;
            }

            Vector3 along = delta * (inverse * length);

            float width = MathF.Min(particles.RadiusOf(index), length);

            Vector3 side = Vector3.Cross(position - camera, along);
            side *= VectorMath.ReciprocalSqrt(side.LengthSquared() + VectorMath.ZeroGuard);

            Vector3 near = position + (side * (width * 0.5f));
            Vector3 far = position + (side * (width * -0.5f));

            SheetFrame frame = sheet.Count == 0
                ? Whole
                : VtfSheet.At(
                    sheet[((particles.SequenceOf(index) % sheet.Count) + sheet.Count) % sheet.Count],
                    age,
                    rate,
                    asFramesPerSecond: false,
                    particles.LifetimeOf(index),
                    fitLifetime: false).Frame;

            Vector3 tint = particles.TintOf(index) / 255f;

            DetailSpriteVertex Corner(Vector3 at, float u, float v) =>
                new(at.X, at.Y, at.Z, u, v, tint.X, tint.Y, tint.Z, alpha, u, v, 0f);

            // The engine's own corner order and texture corners: the head takes the rectangle's bottom edge
            // (V1) and the tail its top (V0).
            DetailSpriteVertex v0 = Corner(near, frame.U0, frame.V1);
            DetailSpriteVertex v1 = Corner(far, frame.U1, frame.V1);
            DetailSpriteVertex v2 = Corner(far + along, frame.U1, frame.V0);
            DetailSpriteVertex v3 = Corner(near + along, frame.U0, frame.V0);

            into.Add(v0);
            into.Add(v1);
            into.Add(v2);

            into.Add(v0);
            into.Add(v2);
            into.Add(v3);
        }
    }
}
