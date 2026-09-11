using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Every entity sprite in a moment, as the batches the renderer draws (B378).
/// </summary>
/// <remarks>
/// **A sprite is a particle with one quad**, so it produces `ParticleBatch` and goes through the same
/// renderer: a texture, a blend and six corners. Building a second path would give the two the same
/// answer only until one of them gained a feature.
///
/// **Batched per material AND blend, which is not an optimization.** An additive glow and a
/// translucent sprite cannot share a draw call because the blend state differs — and since B391 the
/// blend belongs to the ENTITY's render mode rather than to the material, one material can be drawn
/// both ways in the same moment, exactly as `CEngineSprite` keeps a material per render mode.
/// </remarks>
public sealed class EntitySpriteBatches
{
    private readonly Dictionary<(string Path, SpriteBlend Blend), List<DetailSpriteVertex>> _byMaterial =
        [];

    private readonly List<ParticleBatch> _batches = [];

    /// <summary>How many sprites the last build drew, for the census and the log.</summary>
    public int Drawn { get; private set; }

    /// <summary>How many were offered and produced no quad, with the reason folded in.</summary>
    /// <remarks>
    /// **Counted rather than silent**, because every reason a sprite draws nothing here is also a
    /// reason it would legitimately draw nothing: an unresolved material, a render mode with no
    /// material, a basis the engine refuses to build, a glow faded to nothing. A count that never moves
    /// and a count that moves for a good reason look identical without this.
    /// </remarks>
    public int Skipped { get; private set; }

    /// <summary>Builds this moment's sprite quads.</summary>
    /// <param name="props">What the scene is drawing.</param>
    /// <param name="eye">Where the view is, for the distance fade.</param>
    /// <param name="viewRight">The view's right.</param>
    /// <param name="viewUp">The view's up.</param>
    /// <param name="viewForward">The view's forward.</param>
    /// <param name="sprites">Each sprite as loaded, keyed by model path.</param>
    /// <param name="visible">
    /// Whether a glow at a point can be seen — the occlusion fraction, all or nothing. See the
    /// remarks: this is NOT the engine's test and the difference is stated rather than hidden.
    /// </param>
    /// <returns>One batch per material and blend, empty when nothing draws.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The order is `CSprite::DrawModel` then `DrawSprite`'s** (`Sprite.cpp:753`,
    /// `c_sprite.cpp:407-422`): the render scale, then — for the two glow modes and no others — the
    /// glow blend, then the basis, then the corners. Each step is <see cref="EntitySprites"/>'s, which
    /// carries the citations.
    ///
    /// **What the occlusion gate is, exactly.** The engine asks
    /// `PixelVisibility_FractionVisible`, a GPU occlusion query against a proxy quad of
    /// `m_flGlowProxySize`, which returns a FRACTION and makes a glow dissolve smoothly as it goes
    /// behind a pillar. Without a query handle it degenerates to
    /// <c>GlowSightDistance( position, true ) &gt; 0.0f ? 1.0f : 0.0f</c> — a line trace from the eye,
    /// all or nothing (`c_pixel_visibility.cpp:825`). **This project has neither**: no occlusion query
    /// and no world line trace, so the caller supplies a test built from the frustum and the PVS,
    /// which is what `C_TFRagdoll::IsRagdollVisible` uses for a corpse (`c_tf_player.cpp:1350`) and is
    /// the nearest thing that exists here. It is asked only of a glow, because only `GlowBlend` asks.
    ///
    /// The visible consequence, stated so nobody has to rediscover it: a glow behind a pillar in the
    /// same visleaf stays lit where TF2 would hide it, and one that TF2 dissolves gradually pops here.
    /// B378 carries this as the open half, and B391 the depth test that waits on it.
    /// </remarks>
    public IReadOnlyList<ParticleBatch> Build(
        IReadOnlyList<SceneProp> props,
        Vector3 eye,
        Vector3 viewRight,
        Vector3 viewUp,
        Vector3 viewForward,
        IReadOnlyDictionary<string, EngineSprite> sprites,
        Func<Vector3, bool> visible)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(visible);

        foreach (List<DetailSpriteVertex> corners in _byMaterial.Values)
        {
            corners.Clear();
        }

        _batches.Clear();
        Drawn = 0;
        Skipped = 0;

        foreach (SceneProp prop in props)
        {
            if (prop.Kind != SceneModelKind.Sprite)
            {
                continue;
            }

            // **`kRenderNone` refuses before anything else, as `ShouldDraw` does** — the sprite is in
            // the scene because its children might need it, not because it draws (B240).
            if (prop.Pose.RenderMode == RenderModes.None)
            {
                Skipped++;
                continue;
            }

            if (!sprites.TryGetValue(prop.ModelPath, out EngineSprite sprite) ||
                sprite.Material.Sheet is null)
            {
                Skipped++;
                continue;
            }

            // **The blend is the ENTITY's render mode's, never the material text's** (B391).
            // `CEngineSprite::Init` builds a material per render mode and the shader switches on it;
            // `light_glow03`'s text reads translucent, and drawing it by that text painted its opaque
            // black around every lamp. A mode with no material at all draws nothing.
            if (EntitySprites.BlendFor(prop.Pose.RenderMode) is not { } blending)
            {
                Skipped++;
                continue;
            }

            SceneSprite state = prop.Pose.Sprite ?? SceneSprite.Default;

            Vector3 origin = new(prop.Pose.X, prop.Pose.Y, prop.Pose.Z);

            float scale = EntitySprites.RenderScale(
                state.Scale, state.ScaleIsWorldSpace, sprite.Width, sprite.Height);

            // **`DrawSprite`'s own structure** (`c_sprite.cpp:407-422`, B391):
            //
            //     if ( rendermode != kRenderNormal )
            //     {
            //         float blend = render->GetBlend();
            //         if (( rendermode == kRenderGlow ) || ( rendermode == kRenderWorldGlow ))
            //         {
            //             blend *= GlowBlend( psprite, effect_origin, rendermode, renderfx, alpha, &scale );
            //             r *= blend; g *= blend; b *= blend;
            //         }
            //         ...
            //
            // **The glow rule is for the two glow modes and nothing else.** This ran `GlowBlend` for
            // every sprite, so an additive sprite far away was faded, and — since `GlowBlend` grows a
            // non-world glow's scale by `dist / 200` — swelled as the camera backed away.
            // `render->GetBlend()` is the closed engine's and is one here, which B391 names.
            bool glows = prop.Pose.RenderMode is RenderModes.Glow or RenderModes.WorldGlow;
            float blend = 1f;

            if (glows)
            {
                (blend, scale) = EntitySprites.GlowBlend(
                    prop.Pose.RenderMode,
                    prop.Pose.RenderFx,
                    state.Brightness,
                    Vector3.Distance(origin, eye),
                    visible(origin) ? 1f : 0f,
                    scale);

                if (blend <= 0f)
                {
                    Skipped++;
                    continue;
                }
            }

            // **The material's orientation, as the shader translated it at load** (B390). This passed
            // the literal `SPR_VP_PARALLEL_UPRIGHT` for every sprite until then — so `light_glow03`,
            // which asks for `vp_parallel`, stood upright: foreshortened from above, and refused
            // outright within a degree of straight down.
            if (EntitySprites.Axes(
                    sprite.Orientation,
                    origin,
                    (prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll),
                    viewRight,
                    viewUp,
                    viewForward)
                is not var (right, up))
            {
                Skipped++;
                continue;
            }

            if (!_byMaterial.TryGetValue((prop.ModelPath, blending), out List<DetailSpriteVertex>? corners))
            {
                corners = [];
                _byMaterial[(prop.ModelPath, blending)] = corners;
            }

            // **The entity's color, not white** (B391): `CSprite::DrawModel` passes
            // `m_clrRender->r/g/b` as the color and its brightness as the alpha (`Sprite.cpp:795-806`),
            // a glow multiplies the color by its blend, and `DrawSpriteModel` writes `{ r, g, b, a }`
            // into every vertex for the pixel shader to multiply the texture by (`sprite_ps2x.fxc:35`).
            //
            // **`kRenderNormal` is the texture alone**: its shader branch declares no vertex color
            // (`SetSpriteCommonShadowState( 0 )`), so neither the color nor the brightness reaches it.
            // **`kRenderTransAdd` is the material's color**, and the vertex color only on request; see
            // `TransAdd`.
            Vector3 vertexColor = Tint(prop.Pose.RenderColor) * blend;
            float vertexAlpha = state.Brightness / 255f;

            (Vector3 color, float alpha) = prop.Pose.RenderMode switch
            {
                RenderModes.Normal => (Vector3.One, 1f),
                RenderModes.TransAdd => TransAdd(sprite, vertexColor, vertexAlpha),
                _ => (vertexColor, vertexAlpha),
            };

            EntitySprites.Corners(origin, right, up, sprite.Extents, scale, color, alpha, corners);

            Drawn++;
        }

        foreach (((string path, SpriteBlend blending), List<DetailSpriteVertex> corners) in _byMaterial)
        {
            if (corners.Count > 0 && sprites.TryGetValue(path, out EngineSprite sprite))
            {
                _batches.Add(new ParticleBatch(corners, sprite.Material with { Blend = blending }));
            }
        }

        return _batches;
    }

    /// <summary>What <c>kRenderTransAdd</c> multiplies the texture by (B391).</summary>
    /// <param name="sprite">The sprite, carrying its material's constant color.</param>
    /// <param name="vertexColor">The entity's color, which the vertices carry.</param>
    /// <param name="vertexAlpha">The entity's brightness, which the vertices carry as alpha.</param>
    /// <returns>The color and alpha the corners are given.</returns>
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
    /// <c>sample *= g_Color;</c> (`sprite_ps2x.fxc:35-41`). The constant is the MATERIAL's
    /// <c>$color</c> and <c>$alpha</c>. `$ignorevertexcolors` defaults to one, so by default the
    /// entity's tint and brightness never reach the pixels; this drew them as it draws the other
    /// modes.
    /// </remarks>
    private static (Vector3 Color, float Alpha) TransAdd(
        EngineSprite sprite, Vector3 vertexColor, float vertexAlpha)
    {
        (float red, float green, float blue, float constantAlpha) = sprite.ConstantColor;
        Vector3 constant = new(red, green, blue);

        return sprite.IgnoresVertexColors
            ? (constant, constantAlpha)
            : (constant * vertexColor, constantAlpha * vertexAlpha);
    }

    /// <summary><c>m_clrRender</c>'s three bytes as the renderer's zero-to-one color.</summary>
    private static Vector3 Tint((byte Red, byte Green, byte Blue) color) =>
        new(color.Red / 255f, color.Green / 255f, color.Blue / 255f);
}
