using System;
using System.Collections.Generic;
using System.Numerics;

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
/// **Batched per MATERIAL, which is not an optimisation.** An additive glow and a translucent sprite
/// cannot share a draw call because the blend state differs — the same reason `ParticleEffects` keys
/// its lists by material.
/// </remarks>
public sealed class EntitySpriteBatches
{
    private readonly Dictionary<string, List<DetailSpriteVertex>> _byMaterial =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<ParticleBatch> _batches = [];

    /// <summary>How many sprites the last build drew, for the census and the log.</summary>
    public int Drawn { get; private set; }

    /// <summary>How many were offered and produced no quad, with the reason folded in.</summary>
    /// <remarks>
    /// **Counted rather than silent**, because every reason a sprite draws nothing here is also a
    /// reason it would legitimately draw nothing: an unresolved material, a basis the engine refuses
    /// to build, a blend of zero. A count that never moves and a count that moves for a good reason
    /// look identical without this.
    /// </remarks>
    public int Skipped { get; private set; }

    /// <summary>Builds this moment's sprite quads.</summary>
    /// <param name="props">What the scene is drawing.</param>
    /// <param name="eye">Where the view is, for the distance fade.</param>
    /// <param name="viewRight">The view's right.</param>
    /// <param name="viewUp">The view's up.</param>
    /// <param name="viewForward">The view's forward.</param>
    /// <param name="materials">Each sprite's material, keyed by model path.</param>
    /// <param name="visible">
    /// Whether a sprite at a point can be seen — the occlusion fraction, all or nothing. See the
    /// remarks: this is NOT the engine's test and the difference is stated rather than hidden.
    /// </param>
    /// <returns>One batch per material, empty when nothing draws.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The order is `CSprite::DrawModel`'s** (`Sprite.cpp:753`): the render scale, then the basis,
    /// then the glow blend, then the corners. Each step is <see cref="EntitySprites"/>'s, which
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
    /// the nearest thing that exists here.
    ///
    /// The visible consequence, stated so nobody has to rediscover it: a glow behind a pillar in the
    /// same visleaf stays lit where TF2 would hide it, and one that TF2 dissolves gradually pops here.
    /// B378 carries this as the open half.
    /// </remarks>
    public IReadOnlyList<ParticleBatch> Build(
        IReadOnlyList<SceneProp> props,
        Vector3 eye,
        Vector3 viewRight,
        Vector3 viewUp,
        Vector3 viewForward,
        IReadOnlyDictionary<string, ParticleMaterial> materials,
        Func<Vector3, bool> visible)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(materials);
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

            if (!materials.TryGetValue(prop.ModelPath, out ParticleMaterial material) ||
                material.Sheet is not { } sheet)
            {
                Skipped++;
                continue;
            }

            SceneSprite state = prop.Pose.Sprite ?? SceneSprite.Default;

            Vector3 origin = new(prop.Pose.X, prop.Pose.Y, prop.Pose.Z);

            // **The material's orientation, defaulting to upright-parallel** — `CEngineSprite::Init`
            // reads `$spriteorientation` and falls back to `SPR_VP_PARALLEL_UPRIGHT`
            // (`spritemodel.cpp:309`). The VMT parse for that key is not implemented, so every
            // sprite takes the default here; TF2's `light_glow03` declares none, which is why the
            // default is also the right answer for the population this was written for.
            if (EntitySprites.Axes(
                    EntitySprites.ParallelUpright, origin, prop.Pose.Roll,
                    viewRight, viewUp, viewForward)
                is not var (right, up))
            {
                Skipped++;
                continue;
            }

            float scale = EntitySprites.RenderScale(
                state.Scale, state.ScaleIsWorldSpace, sheet.Width, sheet.Height);

            (float blend, float scaled) = EntitySprites.GlowBlend(
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

            if (!_byMaterial.TryGetValue(prop.ModelPath, out List<DetailSpriteVertex>? corners))
            {
                corners = [];
                _byMaterial[prop.ModelPath] = corners;
            }

            // **The blend multiplies the COLOUR and the brightness is the alpha**, which is the split
            // `DrawSprite` makes: `r *= blend; g *= blend; b *= blend;` with `alpha` passed through
            // untouched to the vertex colour (`c_sprite.cpp:354`).
            EntitySprites.Corners(
                origin,
                right,
                up,
                sheet.Width,
                sheet.Height,
                scaled,
                new Vector3(blend, blend, blend),
                state.Brightness / 255f,
                corners);

            Drawn++;
        }

        foreach ((string path, List<DetailSpriteVertex> corners) in _byMaterial)
        {
            if (corners.Count > 0 && materials.TryGetValue(path, out ParticleMaterial material))
            {
                _batches.Add(new ParticleBatch(corners, material));
            }
        }

        return _batches;
    }
}
