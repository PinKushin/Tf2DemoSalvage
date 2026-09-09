using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Turns live particles into camera-facing quads — <c>render_animated_sprites</c> (B373).
/// </summary>
/// <remarks>
/// **The most-used renderer in the game**, by the census: `render_animated_sprites` appears in 8,503
/// of TF2's shipped systems, more than any other function except `Lifetime Random`. Every trail,
/// smoke and spark goes through it.
///
/// **It reuses the detail-sprite pass rather than adding a second one.** `DetailSpriteRenderer`
/// already uploads CPU-built corners with a tint and an alpha and draws them against a sheet, which
/// is exactly a particle quad — so this produces <see cref="DetailSpriteVertex"/> and nothing new
/// reaches the device. Two sprite pipelines that must agree about blending is a defect waiting to
/// happen, and this project has met that shape before.
///
/// **Camera-facing is done HERE and not in a shader**, because the existing pass takes explicit
/// corner positions. The basis is the view's right and up, so a quad is
/// `centre ± right·radius ± up·radius` — which is what "billboard" means and is the same
/// construction the detail sprites use.
///
/// **The sheet IS read now** — `VtfSheet` gives each particle the frame its sequence and age put it
/// on, and the frame it is mixing toward. What that replaced was every particle stretching the whole
/// 512×256 texture across itself: four columns of smoke at once, on every puff, identically.
///
/// **What this does NOT do yet, stated so a green test does not imply it**: the material's blend
/// mode is the detail pass's rather than the one the particle system declares, and `ROTATION` is
/// carried by the store and ignored here — the engine passes it in texcoord 2 beside the frame blend
/// (`spritecard.cpp:271`) and rotates the card. Both are named in B373.
/// </remarks>
public static class ParticleSprites
{
    /// <summary>How many vertices one quad costs — two triangles, no index buffer.</summary>
    public const int CornersPerParticle = 6;

    /// <summary>Builds the corners for every live particle.</summary>
    /// <param name="particles">The live collection.</param>
    /// <param name="right">The camera's right vector, normalised.</param>
    /// <param name="up">The camera's up vector, normalised.</param>
    /// <param name="into">Where corners are appended; NOT cleared.</param>
    /// <param name="sheet">
    /// The sequences the material's texture declares, or an empty list for a texture that carries no
    /// sheet — in which case every particle takes the whole image, which is what a non-sheet texture
    /// means.
    /// </param>
    /// <param name="rate">The renderer's <c>animation rate</c>.</param>
    /// <param name="asFramesPerSecond">Its <c>use animation rate as FPS</c>.</param>
    /// <param name="fitLifetime">Its <c>animation_fit_lifetime</c>.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Two triangles rather than a quad**, because the existing pass draws a triangle list. The
    /// winding follows the detail sprites' own, so a particle is not culled where a detail sprite
    /// is drawn.
    ///
    /// **Tint is 0..255 and alpha is 0..1**, which is the split the attributes themselves use —
    /// `TINT_RGB` is a colour and `ALPHA` is a scalar — and mixing them up produces a picture that
    /// is either invisible or fully saturated rather than one that looks slightly wrong.
    /// </remarks>
    public static void Build(
        ParticleStore particles,
        Vector3 right,
        Vector3 up,
        ICollection<DetailSpriteVertex> into,
        IReadOnlyList<SheetSequence> sheet,
        float rate,
        bool asFramesPerSecond,
        bool fitLifetime)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(sheet);

        for (int index = 0; index < particles.Count; index++)
        {
            float radius = particles.RadiusOf(index);

            if (radius <= 0f || particles.AlphaOf(index) <= 0f)
            {
                // A particle with no size or no opacity costs six vertices and draws nothing.
                continue;
            }

            Vector3 centre = particles.PositionOf(index);

            // **`ROTATION` spins the CARD about the view axis**, which is what the engine does with
            // it: `spritecard.cpp:271` passes rotation in texcoord 2 beside the frame blend and the
            // vertex shader turns the quad. This pass builds corners on the CPU, so the basis is
            // rotated here instead and the result is identical.
            //
            // **Without it a trail is a visible grid.** `rockettrail` gives every puff
            // `rotation_initial -45` plus 0..45 degrees precisely so that no two cards line up; drawn
            // axis-aligned, the same five tiles repeat in lockstep across the whole plume.
            (float sine, float cosine) = MathF.SinCos(particles.RotationOf(index));

            Vector3 across = ((right * cosine) + (up * sine)) * radius;
            Vector3 above = ((up * cosine) - (right * sine)) * radius;

            float alpha = particles.AlphaOf(index);

            // **`TINT_RGB` is 0..255 and the vertex takes 0..1**, which is the split this class's
            // remarks already named — and which was being sidestepped by writing white. A rocket
            // trail is firelight on smoke: `Color Random` gives it (247 194 117) to (251 142 0), and
            // ignoring that drew the raw texture at full brightness.
            Vector3 tint = particles.TintOf(index) / 255f;

            // **The sequence is the particle's own, wrapped rather than clamped.** `SEQUENCE_NUMBER`
            // is drawn from `sequence_min`..`sequence_max`, which the file states independently of
            // how many sequences the texture happens to carry — so a definition and a texture that
            // disagree must still draw something rather than throwing at the renderer.
            (SheetFrame frame, SheetFrame next, float blend) = sheet.Count == 0
                ? (VtfSheet.Whole, VtfSheet.Whole, 0f)
                : VtfSheet.At(
                    sheet[((particles.SequenceOf(index) % sheet.Count) + sheet.Count) % sheet.Count],
                    particles.AgeOf(index),
                    rate,
                    asFramesPerSecond,
                    particles.LifetimeOf(index),
                    fitLifetime);

            DetailSpriteVertex Corner(Vector3 at, float across0, float down0) =>
                new(
                    at.X,
                    at.Y,
                    at.Z,
                    Mix(frame.U0, frame.U1, across0),
                    Mix(frame.V0, frame.V1, down0),
                    tint.X,
                    tint.Y,
                    tint.Z,
                    alpha,
                    Mix(next.U0, next.U1, across0),
                    Mix(next.V0, next.V1, down0),
                    blend);

            DetailSpriteVertex topLeft = Corner(centre - across + above, 0f, 0f);
            DetailSpriteVertex topRight = Corner(centre + across + above, 1f, 0f);
            DetailSpriteVertex bottomLeft = Corner(centre - across - above, 0f, 1f);
            DetailSpriteVertex bottomRight = Corner(centre + across - above, 1f, 1f);

            into.Add(topLeft);
            into.Add(topRight);
            into.Add(bottomLeft);

            into.Add(bottomLeft);
            into.Add(topRight);
            into.Add(bottomRight);
        }
    }

    /// <summary>Where a corner sits inside a frame's rectangle.</summary>
    /// <remarks>
    /// **A frame's UVs are its BOUNDS, not a single point** — `spritecard.cpp:271` calls texcoord 0
    /// "sheet bounding uvs". So a quad's corners interpolate across that rectangle, and the code this
    /// replaced emitted a fixed 0..1, which is the whole texture: four columns of smoke drawn on
    /// every particle at once.
    /// </remarks>
    private static float Mix(float from, float to, float along) => from + ((to - from) * along);
}
