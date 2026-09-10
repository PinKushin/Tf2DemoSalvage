using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Turns an entity sprite into the quad the engine draws for it — <c>C_SpriteRenderer</c> (B378).
/// </summary>
/// <remarks>
/// **`env_sprite` was offered to the draw path and rejected as not a studio model**, eleven times a
/// frame on `cp_granary` and 2,840 times over 355 sampled frames of a 2026 `cp_process`. A sprite is
/// not a model: it is one camera-facing quad with a material, which is why nothing downstream of
/// `SceneModelKind.Sprite` ever knew what to do with one.
///
/// **Three engine functions, in the order `DrawModel` calls them** (`Sprite.cpp:753`):
/// <c>GetSpriteAxes</c> chooses the quad's basis, <c>GlowBlend</c> decides how much of it survives,
/// and <c>DrawSpriteModel</c> emits the corners. Each is implemented below with its citation, and
/// what is deliberately NOT implemented is named where it would go rather than left silent.
/// </remarks>
public static class EntitySprites
{
    /// <summary>The sprite orientations, <c>C_SpriteRenderer::SPRITETYPE</c> (`Sprite.h:36`).</summary>
    /// <remarks>
    /// **A `.vmt` sprite's orientation comes from the MATERIAL, not the entity** — `CEngineSprite::
    /// Init` reads <c>$spriteorientation</c> and falls back to <see cref="ParallelUpright"/> when the
    /// material declares none (`spritemodel.cpp:309`). So the default here is the engine's default
    /// and not a choice.
    /// </remarks>
    public const int ParallelUpright = 0;

    /// <summary><c>SPR_FACING_UPRIGHT</c> — up is world up, right faces the origin.</summary>
    public const int FacingUpright = 1;

    /// <summary><c>SPR_VP_PARALLEL</c> — flat to the viewplane, the ordinary billboard.</summary>
    public const int Parallel = 2;

    /// <summary><c>SPR_ORIENTED</c> — the entity's own angles.</summary>
    public const int Oriented = 3;

    /// <summary><c>SPR_VP_PARALLEL_ORIENTED</c> — flat to the viewplane, rolled.</summary>
    public const int ParallelOriented = 4;

    /// <summary>Where a glow stops fading — 1,200 units, <c>c_sprite.cpp:147</c>.</summary>
    /// <remarks>
    /// Valve's own comment calls the pair magic: *"UNDONE: Tweak these magic numbers (1200 -
    /// distance at full brightness)"*. Kept exactly, because a glow's falloff is the whole of what a
    /// lamp halo looks like and a rounder number would be this project inventing an appearance.
    /// </remarks>
    public const float FullBrightnessDistance = 1200f;

    /// <summary><c>kRenderFxNoDissipation</c> — a glow that does not fade with distance.</summary>
    /// <remarks>Fourteenth in <c>RenderFx_t</c>, `const.h:384`.</remarks>
    public const int NoDissipation = 14;

    /// <summary>The angle at which an upright sprite's basis collapses — cos(1°).</summary>
    /// <remarks>
    /// **The engine REFUSES to draw rather than drawing something wrong.** Both upright orientations
    /// build their basis from a cross product against world up, and *"this will not work if the view
    /// direction is very close to straight up or down, because the cross product will be between two
    /// nearly parallel vectors"* — so `GetSpriteAxes` returns with the basis untouched, which leaves
    /// the caller's zeroed vectors and draws nothing (`c_sprite.cpp:226`).
    /// </remarks>
    public const float UprightLimit = 0.999848f;

    /// <summary>The quad's basis — <c>C_SpriteRenderer::GetSpriteAxes</c> (`c_sprite.cpp:226`).</summary>
    /// <param name="orientation">The material's <c>$spriteorientation</c>.</param>
    /// <param name="origin">Where the sprite is.</param>
    /// <param name="roll">The entity's roll, which two orientations use.</param>
    /// <param name="viewRight">The view's right, for the viewplane-parallel kinds.</param>
    /// <param name="viewUp">The view's up.</param>
    /// <param name="viewForward">The view's forward.</param>
    /// <returns>The right and up the quad is built from, or null when the engine draws nothing.</returns>
    /// <remarks>
    /// **The roll promotion comes FIRST and is easy to miss** — a parallel sprite with any roll at
    /// all becomes a rolled one before the switch runs:
    ///
    /// <code>
    /// if ( angles[2] != 0 &amp;&amp; type == SPR_VP_PARALLEL )
    ///     type = SPR_VP_PARALLEL_ORIENTED;
    /// </code>
    ///
    /// **Null rather than a fallback basis for the two upright refusals**, because that is what the
    /// engine does: it returns early leaving `forward`, `right` and `up` as the caller left them, and
    /// `DrawSpriteModel` then builds a degenerate quad from zero vectors. Answering null and drawing
    /// nothing is the same picture without relying on uninitialised memory to produce it.
    /// </remarks>
    public static (Vector3 Right, Vector3 Up)? Axes(
        int orientation,
        Vector3 origin,
        float roll,
        Vector3 viewRight,
        Vector3 viewUp,
        Vector3 viewForward)
    {
        if (roll != 0f && orientation == Parallel)
        {
            orientation = ParallelOriented;
        }

        switch (orientation)
        {
            case FacingUpright:
            {
                // `tvec` is the NEGATED origin, which is the engine's own shorthand for the vector
                // from the sprite to the world origin — not to the viewer. Kept because it is what
                // the engine computes; a version pointing at the eye would be a different sprite.
                Vector3 toward = -origin;

                if (toward.LengthSquared() <= 0f)
                {
                    return null;
                }

                toward = Vector3.Normalize(toward);

                if (toward.Z > UprightLimit || toward.Z < -UprightLimit)
                {
                    return null;
                }

                Vector3 right = Vector3.Normalize(new Vector3(toward.Y, -toward.X, 0f));

                return (right, Vector3.UnitZ);
            }

            case Parallel:
                return (viewRight, viewUp);

            case ParallelUpright:
            {
                if (viewForward.Z > UprightLimit || viewForward.Z < -UprightLimit)
                {
                    return null;
                }

                Vector3 right = Vector3.Normalize(new Vector3(viewForward.Y, -viewForward.X, 0f));

                return (right, Vector3.UnitZ);
            }

            case Oriented:
                // The entity's own angles. Left to the caller, which has the rotation already: this
                // returns null so an oriented sprite is not silently drawn as a billboard, which
                // would be a wrong picture rather than a missing one.
                return null;

            case ParallelOriented:
            {
                (float sine, float cosine) = MathF.SinCos(roll * (MathF.PI * 2f / 360f));

                return (
                    (viewRight * cosine) + (viewUp * sine),
                    (viewRight * -sine) + (viewUp * cosine));
            }

            default:
                return null;
        }
    }

    /// <summary>How much of a glow survives — <c>StandardGlowBlend</c> (`c_sprite.cpp:147`).</summary>
    /// <param name="renderMode">The entity's <c>m_nRenderMode</c>.</param>
    /// <param name="renderFx">Its <c>m_nRenderFX</c>.</param>
    /// <param name="brightness">Its <c>m_nBrightness</c>, which is the sprite's alpha.</param>
    /// <param name="distance">Eye-to-sprite distance, or non-positive when the line is blocked.</param>
    /// <param name="visible">The occlusion fraction; see the remarks.</param>
    /// <param name="scale">The render scale, which a screen-space glow multiplies.</param>
    /// <returns>The blend, and the scale after any screen-space correction.</returns>
    /// <remarks>
    /// <code>
    /// brightness = PixelVisibility_FractionVisible( params, queryHandle );
    /// if ( brightness &lt;= 0.0f ) return 0.0f;
    /// dist = GlowSightDistance( params.position, false );
    /// if ( dist &lt;= 0.0f ) return 0.0f;
    /// if ( renderfx == kRenderFxNoDissipation )
    ///     return (float)alpha * (1.0f/255.0f) * brightness;
    /// float fadeOut = (1200.0f*1200.0f) / (dist*dist);
    /// fadeOut = clamp( fadeOut, 0.0f, 1.0f );
    /// if (rendermode != kRenderWorldGlow)
    ///     *pscale *= dist * (1.0f/200.0f);
    /// return fadeOut * brightness;
    /// </code>
    ///
    /// **`kRenderWorldGlow` keeps its world size and `kRenderGlow` does not**, which is the one
    /// branch that decides whether a lamp halo stays put or grows with distance. TF2's
    /// `light_glow03` sprites are mode 9, `kRenderWorldGlow`, so they keep it.
    ///
    /// **The occlusion fraction is a GPU query in the engine and a TRACE here, and that is Valve's
    /// own fallback rather than a substitute invented for this project.**
    /// `PixelVisibility_FractionVisible` opens with
    /// <c>if ( !queryHandle ) return GlowSightDistance( params.position, true ) &gt; 0.0f ? 1.0f :
    /// 0.0f;</c> (`c_pixel_visibility.cpp:825`) — a line of sight, all or nothing. What a real client
    /// gets instead is a smooth fade as the halo goes behind a pillar, so a glow here pops where TF2
    /// dissolves. Named in B378 rather than hidden.
    /// </remarks>
    public static (float Blend, float Scale) GlowBlend(
        int renderMode, int renderFx, int brightness, float distance, float visible, float scale)
    {
        if (visible <= 0f || distance <= 0f)
        {
            return (0f, scale);
        }

        if (renderFx == NoDissipation)
        {
            return (brightness * (1f / 255f) * visible, scale);
        }

        float fade = Math.Clamp(
            FullBrightnessDistance * FullBrightnessDistance / (distance * distance), 0f, 1f);

        if (renderMode != RenderModes.WorldGlow)
        {
            scale *= distance * (1f / 200f);
        }

        return (fade * visible, scale);
    }

    /// <summary>The scale the quad is built at — <c>CSprite::DrawModel</c> (`Sprite.cpp:753`).</summary>
    /// <param name="scale"><c>m_flSpriteScale</c>.</param>
    /// <param name="worldSpace"><c>m_bWorldSpaceScale</c>.</param>
    /// <param name="width">The material's mapping width.</param>
    /// <param name="height">Its mapping height.</param>
    /// <returns>The multiplier on the sprite's own extents.</returns>
    /// <remarks>
    /// <code>
    /// float renderscale = GetRenderScale();
    /// if ( m_bWorldSpaceScale )
    /// {
    ///     float flMinSize = MIN( psprite-&gt;GetWidth(), psprite-&gt;GetHeight() );
    ///     renderscale /= flMinSize;
    /// }
    /// </code>
    ///
    /// **A world-space scale is divided by the material's SMALLER dimension**, which turns a size in
    /// world units into the multiplier the quad arithmetic wants. `GetRenderBounds` takes the same
    /// split from the other side, multiplying by <c>MAX( width, height )</c> only when the flag is
    /// clear — the two are not the same dimension, and using one for both would be wrong for any
    /// sprite that is not square.
    ///
    /// **A non-positive scale becomes one**, which `DrawSpriteModel` does before anything else:
    /// <c>if ( fscale &gt; 0 ) scale = fscale; else scale = 1.0f;</c>.
    /// </remarks>
    public static float RenderScale(float scale, bool worldSpace, int width, int height)
    {
        if (scale <= 0f)
        {
            scale = 1f;
        }

        if (!worldSpace)
        {
            return scale;
        }

        int smallest = Math.Min(width, height);

        return smallest > 0 ? scale / smallest : scale;
    }

    /// <summary>The four corners — <c>DrawSpriteModel</c> (`c_sprite.cpp:45`).</summary>
    /// <param name="origin">Where the sprite is.</param>
    /// <param name="right">The basis right, from <see cref="Axes"/>.</param>
    /// <param name="up">The basis up.</param>
    /// <param name="width">The material's mapping width.</param>
    /// <param name="height">Its mapping height.</param>
    /// <param name="scale">The render scale, from <see cref="RenderScale"/>.</param>
    /// <param name="colour">Red, green and blue after the glow blend, each 0..1.</param>
    /// <param name="alpha">The sprite's brightness, 0..1.</param>
    /// <param name="into">Where the six corners go — two triangles, as the renderer wants them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    /// <remarks>
    /// <code>
    /// VectorMA( origin, psprite-&gt;GetDown()  * scale, up,    vec_a );
    /// VectorScale( right, psprite-&gt;GetLeft()  * scale,       vec_b );
    /// VectorMA( origin, psprite-&gt;GetUp()    * scale, up,    vec_c );
    /// VectorScale( right, psprite-&gt;GetRight() * scale,       vec_d );
    /// </code>
    ///
    /// with the quad wound `a+b`, `c+b`, `c+d`, `a+d`.
    ///
    /// **The extents come from the MATERIAL and are centred by default** — `CEngineSprite::Init`
    /// takes <c>$spriteorigin</c>, falling back to <c>origin[0] = -width * 0.5f; origin[1] = height *
    /// 0.5f;</c>, and then sets <c>up = origin[1]; down = origin[1] - height; left = origin[0]; right
    /// = width + origin[0];</c> (`spritemodel.cpp:221`). So a default sprite spans ±width/2 by
    /// ±height/2 in texels, scaled. **`$spriteorigin` is not read here**: no TF2 sprite this project
    /// has met declares one, and inventing a parse for a key nothing sends is how a wrong default
    /// gets locked in (B378 says what would falsify that).
    ///
    /// **Six corners rather than four**, because `DetailSpriteRenderer` draws triangles where the
    /// engine's `MATERIAL_QUADS` does not — the same expansion `ParticleSprites.Build` makes.
    /// </remarks>
    public static void Corners(
        Vector3 origin,
        Vector3 right,
        Vector3 up,
        int width,
        int height,
        float scale,
        Vector3 colour,
        float alpha,
        ICollection<DetailSpriteVertex> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;

        Vector3 below = origin + (up * (-halfHeight * scale));
        Vector3 above = origin + (up * (halfHeight * scale));
        Vector3 leftward = right * (-halfWidth * scale);
        Vector3 rightward = right * (halfWidth * scale);

        DetailSpriteVertex bottomLeft = Corner(below + leftward, 0f, 1f, colour, alpha);
        DetailSpriteVertex topLeft = Corner(above + leftward, 0f, 0f, colour, alpha);
        DetailSpriteVertex topRight = Corner(above + rightward, 1f, 0f, colour, alpha);
        DetailSpriteVertex bottomRight = Corner(below + rightward, 1f, 1f, colour, alpha);

        into.Add(bottomLeft);
        into.Add(topLeft);
        into.Add(topRight);

        into.Add(bottomLeft);
        into.Add(topRight);
        into.Add(bottomRight);
    }

    /// <summary>One corner, in the renderer's own vertex.</summary>
    private static DetailSpriteVertex Corner(
        Vector3 at, float u, float v, Vector3 colour, float alpha) =>
        new(at.X, at.Y, at.Z, u, v, colour.X, colour.Y, colour.Z, alpha, u, v, 0f);
}
