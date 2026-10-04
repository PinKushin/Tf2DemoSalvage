using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
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
    /// <param name="orientation">The material's <c>$spriteorientation</c>, as the shader translated it.</param>
    /// <param name="origin">Where the sprite is.</param>
    /// <param name="angles">
    /// The entity's angles — all three for an oriented sprite, and the roll for the two that use it.
    /// </param>
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
    ///
    /// **Every branch is reachable from a real sprite now, and until B390 one was.** The only caller
    /// passed the literal <c>SPR_VP_PARALLEL_UPRIGHT</c> for every sprite, so the other four answered
    /// their own tests and nothing on screen — while `light_glow03` asks for `vp_parallel`.
    /// </remarks>
    public static (Vector3 Right, Vector3 Up)? Axes(
        SpriteOrientation orientation,
        Vector3 origin,
        (float Pitch, float Yaw, float Roll) angles,
        Vector3 viewRight,
        Vector3 viewUp,
        Vector3 viewForward)
    {
        if (angles.Roll != 0f && orientation == SpriteOrientation.Parallel)
        {
            orientation = SpriteOrientation.ParallelOriented;
        }

        switch (orientation)
        {
            case SpriteOrientation.FacingUpright:
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

            case SpriteOrientation.Parallel:
                return (viewRight, viewUp);

            case SpriteOrientation.ParallelUpright:
            {
                if (viewForward.Z > UprightLimit || viewForward.Z < -UprightLimit)
                {
                    return null;
                }

                Vector3 right = Vector3.Normalize(new Vector3(viewForward.Y, -viewForward.X, 0f));

                return (right, Vector3.UnitZ);
            }

            case SpriteOrientation.Oriented:
            {
                // **The entity's own angles, through Valve's one decomposition** —
                // `AngleVectors( angles, &forward, &right, &up )` (`c_sprite.cpp:323`). This branch
                // answered null until B390, on the note that the caller "has the rotation already";
                // no caller ever supplied it, so an oriented sprite drew nothing at all.
                (float rightX, float rightY, float rightZ) =
                    AngleVectors.Right(angles.Pitch, angles.Yaw, angles.Roll);
                (float upX, float upY, float upZ) =
                    AngleVectors.Up(angles.Pitch, angles.Yaw, angles.Roll);

                return (new Vector3(rightX, rightY, rightZ), new Vector3(upX, upY, upZ));
            }

            case SpriteOrientation.ParallelOriented:
            {
                (float sine, float cosine) = MathF.SinCos(angles.Roll * (MathF.PI * 2f / 360f));

                return (
                    (viewRight * cosine) + (viewUp * sine),
                    (viewRight * -sine) + (viewUp * cosine));
            }

            default:
                return null;
        }
    }

    /// <summary>How a sprite is drawn — the <c>Sprite</c> shader's switch on its render mode (B391).</summary>
    /// <param name="renderMode">The entity's <c>m_nRenderMode</c>.</param>
    /// <returns>Each draw in order, or none for the modes that have no material and draw nothing.</returns>
    /// <remarks>
    /// **The ENTITY decides, never the material's text.** `CEngineSprite::Init` builds one material per
    /// render mode, stamping each with <c>$spriteRenderMode</c> (`spritemodel.cpp:279-289`), and the
    /// shader switches on it (`sprite_dx9.cpp:227`): the glow and additive modes blend
    /// <c>SRC_ALPHA, ONE</c>, the translucent ones <c>SRC_ALPHA, ONE_MINUS_SRC_ALPHA</c>, and
    /// `kRenderNone` and `kRenderEnvironmental` get no material at all (`spritemodel.cpp:281`).
    /// `light_glow03`'s text reads translucent — its `$additive` is commented out — and drawing it by
    /// that text is what painted its opaque black around every lamp.
    ///
    /// **The depth state is the mode's too.** Every blended branch calls <c>EnableDepthWrites( false )</c>;
    /// the two glow modes alone add <c>EnableDepthTest( false )</c> (`sprite_dx9.cpp:264-265`), which
    /// the engine can afford because `GlowBlend`'s visibility gate has already refused a hidden glow
    /// — `GlowSight` here. `kRenderNormal` (`:229-241`) sets neither blending nor depth, so it is
    /// opaque, tested and written.
    ///
    /// **`kRenderTransAlphaAdd` draws twice** (`:297-330`): translucent, then
    /// <c>ONE_MINUS_SRC_ALPHA, ONE</c>, both depth-tested and unwritten.
    /// </remarks>
    public static IReadOnlyList<SpritePass> PassesFor(int renderMode) => renderMode switch
    {
        RenderModes.Normal => NormalPass,
        RenderModes.Glow or RenderModes.WorldGlow => GlowPass,
        RenderModes.TransAdd or RenderModes.TransAddFrameBlend => AdditivePass,
        RenderModes.TransColor or RenderModes.TransTexture or RenderModes.TransAlpha => TranslucentPass,
        RenderModes.TransAlphaAdd => TransAlphaAddPasses,
        _ => [],
    };

    /// <summary>How a sprite MATERIAL draws at a render mode — the Sprite shader's switch, or a non-Sprite shader's own blend.</summary>
    /// <param name="sprite">The material, as loaded.</param>
    /// <param name="renderMode">The mode the caller draws it at.</param>
    /// <returns>Each draw in order; none for a Sprite-shader mode that has no material.</returns>
    /// <remarks>
    /// **The render mode reaches only the Sprite shader.** <c>CEngineSprite::Init</c> copies the material once per mode
    /// and sets <c>$spriteRenderMode</c> on each copy (`spritemodel.cpp:287-290`); a shader that never reads the variable
    /// — a sprite trail's <c>UnlitGeneric</c> — draws every copy the same way, by its own <c>$translucent</c> or
    /// <c>$additive</c>, tested and unwritten as a blended material is.
    /// </remarks>
    public static IReadOnlyList<SpritePass> PassesFor(EngineSprite sprite, int renderMode) =>
        sprite.IsSpriteShader ? PassesFor(renderMode) : [new SpritePass(sprite.Material.Blend, SpriteDepth.TestNoWrite)];

    /// <summary>What the shader multiplies the texture by, given the colour and alpha a vertex carries (B391).</summary>
    /// <param name="sprite">The material, as loaded.</param>
    /// <param name="renderMode">The mode the caller draws it at.</param>
    /// <param name="vertexColour">The vertex colour, 0 to 1.</param>
    /// <param name="vertexAlpha">The vertex alpha, 0 to 1.</param>
    /// <returns>The colour and alpha the corner must carry for the renderer's texture-times-colour to match.</returns>
    /// <remarks>
    /// **The Sprite shader's own branches** (`sprite_dx9.cpp:227-484`): <c>kRenderNormal</c> declares no vertex colour
    /// (<c>SetSpriteCommonShadowState( 0 )</c>), so the texture alone reaches the screen; <c>kRenderTransAdd</c> multiplies
    /// the material's constant <c>$color</c> and <c>$alpha</c>, and the vertex colour only when
    /// <c>$ignorevertexcolors</c> is cleared — it defaults to one (`:39`, `:332-338`);
    /// <c>kRenderTransAddFrameBlend</c> is <c>$alpha</c> as grey; every other mode is the vertex colour.
    ///
    /// **A non-Sprite shader is the material's modulation, and the vertex colour where it asks for it** —
    /// <c>UnlitGeneric</c>'s <c>VERTEXCOLOR</c> combo and its <c>$vertexalpha</c> lerp
    /// (`vertexlit_and_unlit_generic_ps2x.fxc:345-348`, `:419`), against <c>$color</c> × <c>$color2</c> and <c>$alpha</c>.
    ///
    /// **Moved here from `EntitySpriteBatches`** so a beam, which draws a sprite material at a mode of its own choosing,
    /// gets the same answer as an entity sprite — the rule belongs to the material, not to whoever draws it.
    /// </remarks>
    public static (Vector3 Colour, float Alpha) ShaderInput(
        EngineSprite sprite, int renderMode, Vector3 vertexColour, float vertexAlpha)
    {
        if (!sprite.IsSpriteShader)
        {
            (float red, float green, float blue, float alpha) = sprite.Modulation ?? (1f, 1f, 1f, 1f);

            return (
                (sprite.VertexColour ? vertexColour : Vector3.One) * new Vector3(red, green, blue),
                (sprite.VertexAlpha ? vertexAlpha : 1f) * alpha);
        }

        return renderMode switch
        {
            RenderModes.Normal => (Vector3.One, 1f),
            RenderModes.TransAdd => TransAdd(sprite, vertexColour, vertexAlpha),
            RenderModes.TransAddFrameBlend => FrameBlend(sprite, vertexColour, vertexAlpha),
            _ => (vertexColour, vertexAlpha),
        };
    }

    /// <summary>What <c>kRenderTransAdd</c> multiplies the texture by (B391).</summary>
    /// <remarks>
    /// **The shader's branch** (`sprite_dx9.cpp:332-338`):
    ///
    /// <code>
    /// unsigned int flags = SHADER_USE_CONSTANT_COLOR;
    /// if( !params[ IGNOREVERTEXCOLORS ]-&gt;GetIntValue() )
    ///     flags |= SHADER_USE_VERTEX_COLOR;
    /// </code>
    ///
    /// and the pixel shader multiplies the texture by each one present — <c>sample *= i.color;</c>, then
    /// <c>sample *= g_Color;</c> (`sprite_ps2x.fxc:35-41`). The constant is the MATERIAL's <c>$color</c> and
    /// <c>$alpha</c>. `$ignorevertexcolors` defaults to one, so by default the entity's tint and brightness never reach
    /// the pixels.
    /// </remarks>
    private static (Vector3 Color, float Alpha) TransAdd(EngineSprite sprite, Vector3 vertexColor, float vertexAlpha)
    {
        (float red, float green, float blue, float constantAlpha) = sprite.ConstantColor;
        Vector3 constant = new(red, green, blue);

        return sprite.IgnoresVertexColors
            ? (constant, constantAlpha)
            : (constant * vertexColor, constantAlpha * vertexAlpha);
    }

    /// <summary>What <c>kRenderTransAddFrameBlend</c> multiplies the texture by (B391).</summary>
    /// <remarks>
    /// **Two draws in the shader, one here, and the sum is the same** (`sprite_dx9.cpp:356-484`). Each sets
    /// <c>color[0] = color[1] = color[2] = flFade * frameBlendAlpha; color[3] = 1.0f;</c> with
    /// <c>flFade = params[ALPHA]</c> — `$color` is never read — and the weights are <c>1 - frac( $frame )</c> on frame
    /// <c>(int)$frame</c> and <c>frac( $frame )</c> on the next. This project draws a sprite's first frame only, so both
    /// draws sample one texture and add to one draw of grey `$alpha`. The vertex color follows `$ignorevertexcolors`,
    /// as in mode 5.
    /// </remarks>
    private static (Vector3 Color, float Alpha) FrameBlend(EngineSprite sprite, Vector3 vertexColor, float vertexAlpha)
    {
        Vector3 grey = new(sprite.ConstantColor.Alpha);

        return sprite.IgnoresVertexColors ? (grey, 1f) : (grey * vertexColor, vertexAlpha);
    }

    private static readonly SpritePass[] NormalPass = [new(SpriteBlend.Opaque, SpriteDepth.TestAndWrite)];

    private static readonly SpritePass[] GlowPass = [new(SpriteBlend.Additive, SpriteDepth.Off)];

    private static readonly SpritePass[] AdditivePass = [new(SpriteBlend.Additive, SpriteDepth.TestNoWrite)];

    private static readonly SpritePass[] TranslucentPass = [new(SpriteBlend.Translucent, SpriteDepth.TestNoWrite)];

    private static readonly SpritePass[] TransAlphaAddPasses =
    [
        new(SpriteBlend.Translucent, SpriteDepth.TestNoWrite),
        new(SpriteBlend.InverseAlphaAdd, SpriteDepth.TestNoWrite),
    ];

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
    /// <param name="extents">Where the quad's edges sit about the origin, from <see cref="SpriteExtents.Of"/>.</param>
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
    /// **The four edges are the sprite's own, and only the default is symmetric** (B390). They are
    /// `CEngineSprite::Init`'s arithmetic on the material's <c>$spriteorigin</c>. This took half the
    /// width and half the height either side until then, and its remark defended that on the grounds
    /// that no TF2 sprite declared the key — 202 shipped materials do, and three of them hang the quad
    /// entirely below its origin.
    ///
    /// **Six corners rather than four**, because `DetailSpriteRenderer` draws triangles where the
    /// engine's `MATERIAL_QUADS` does not — the same expansion `ParticleSprites.Build` makes.
    /// </remarks>
    public static void Corners(
        Vector3 origin,
        Vector3 right,
        Vector3 up,
        SpriteExtents extents,
        float scale,
        Vector3 colour,
        float alpha,
        ICollection<DetailSpriteVertex> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        Vector3 below = origin + (up * (extents.Down * scale));
        Vector3 above = origin + (up * (extents.Up * scale));
        Vector3 leftward = right * (extents.Left * scale);
        Vector3 rightward = right * (extents.Right * scale);

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
