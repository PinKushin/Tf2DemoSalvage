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
/// **What this does NOT do yet, stated so a green test does not imply it**: the sequence sheet is
/// not read, so every particle takes the whole texture rather than its animation frame, and the
/// material's blend mode is the detail pass's rather than the one the particle system declares.
/// Both are named in B373.
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
        ICollection<DetailSpriteVertex> into)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(into);

        for (int index = 0; index < particles.Count; index++)
        {
            float radius = particles.RadiusOf(index);

            if (radius <= 0f || particles.AlphaOf(index) <= 0f)
            {
                // A particle with no size or no opacity costs six vertices and draws nothing.
                continue;
            }

            Vector3 centre = particles.PositionOf(index);
            Vector3 across = right * radius;
            Vector3 above = up * radius;

            float alpha = particles.AlphaOf(index);

            DetailSpriteVertex Corner(Vector3 at, float u, float v) =>
                new(at.X, at.Y, at.Z, u, v, 1f, 1f, 1f, alpha);

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
}
