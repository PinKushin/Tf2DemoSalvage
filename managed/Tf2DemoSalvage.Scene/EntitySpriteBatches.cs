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
/// both ways in the same moment, exactly as `CEngineSprite` keeps a material per render mode. The key
/// is the whole PASS — blend and depth state — since a glow and a `kRenderTransAdd` sprite share a
/// blend and not a depth test (B391).
/// </remarks>
public sealed class EntitySpriteBatches
{
    private readonly Dictionary<(string Path, SpritePass Pass), List<DetailSpriteVertex>> _byMaterial =
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
    /// all or nothing (`c_pixel_visibility.cpp:825`). The caller supplies that trace
    /// (<see cref="GlowSight"/>). It is asked only of a glow, because only `GlowBlend` asks.
    ///
    /// The visible consequence of having no query: a glow that TF2 dissolves gradually as it goes
    /// behind a pillar pops here. B378 carries this as the open half.
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

            // **A beam's model IS a sprite, and its class draws it as a beam.** `C_Beam::DrawModel` hands the
            // entity to `beams->DrawBeam` (`beam_shared.cpp:1000`) and never reaches `CSprite::DrawModel`, so a
            // beam drawn here as well would put a glow quad at every spotlight's lamp — `EntityBeams` is its pass.
            // **A trail's model is a sprite too, and `CSpriteTrail::DrawModel` overrides the sprite's** (`SpriteTrail.cpp:422`):
            // it draws a ribbon through the points it sampled, never a quad at its head — `EntityTrails` is its pass (B474).
            if (prop.Pose.Beam is not null || prop.Pose.SpriteTrail is not null)
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
            IReadOnlyList<SpritePass> passes = EntitySprites.PassesFor(sprite, prop.Pose.RenderMode);

            if (passes.Count == 0)
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
            // Stryker disable all : a mutant that empties the guard body leaves 'right' and 'up'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
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

            // Stryker restore all

            // **The entity's color, not white** (B391): `CSprite::DrawModel` passes
            // `m_clrRender->r/g/b` as the color and its brightness as the alpha (`Sprite.cpp:795-806`),
            // a glow multiplies the color by its blend, and `DrawSpriteModel` writes `{ r, g, b, a }`
            // into every vertex for the pixel shader to multiply the texture by (`sprite_ps2x.fxc:35`).
            //
            // What the shader then does with that vertex colour per mode — the texture alone for
            // `kRenderNormal`, the material's constant colour for `kRenderTransAdd` — is the material's
            // rule and lives with it: `EntitySprites.ShaderInput`.
            Vector3 vertexColor = Tint(prop.Pose.RenderColor) * blend;
            float vertexAlpha = state.Brightness / 255f;

            (Vector3 color, float alpha) = EntitySprites.ShaderInput(sprite, prop.Pose.RenderMode, vertexColor, vertexAlpha);

            // **One set of corners per DRAW, in the shader's order** — `kRenderTransAlphaAdd` is two.
            foreach (SpritePass pass in passes)
            {
                if (!_byMaterial.TryGetValue((prop.ModelPath, pass), out List<DetailSpriteVertex>? corners))
                {
                    corners = [];
                    _byMaterial[(prop.ModelPath, pass)] = corners;
                }

                EntitySprites.Corners(origin, right, up, sprite.Extents, scale, color, alpha, corners);
            }

            Drawn++;
        }

        foreach (((string path, SpritePass pass), List<DetailSpriteVertex> corners) in _byMaterial)
        {
            if (corners.Count > 0 && sprites.TryGetValue(path, out EngineSprite sprite))
            {
                _batches.Add(new ParticleBatch(
                    corners, sprite.Material with { Blend = pass.Blend, Depth = pass.Depth }));
            }
        }

        return _batches;
    }

    /// <summary><c>m_clrRender</c>'s three bytes as the renderer's zero-to-one color.</summary>
    private static Vector3 Tint((byte Red, byte Green, byte Blue) color) =>
        new(color.Red / 255f, color.Green / 255f, color.Blue / 255f);
}
