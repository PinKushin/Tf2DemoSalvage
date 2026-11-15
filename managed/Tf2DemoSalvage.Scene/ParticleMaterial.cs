using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Everything drawing one particle system's material needs (B373).
/// </summary>
/// <param name="Sheet">Its texture, or null when it did not resolve.</param>
/// <param name="Sequences">The animation sequences that texture declares, empty when it has none.</param>
/// <param name="Blend">How it blends, from its own <c>$additive</c> and friends.</param>
/// <param name="Alpha">
/// Its own <c>$alpha</c>, which scales every vertex's: `particle/particle_smokegrenade` states <c>srgb?$alpha .27</c>, and
/// TF2 on the PC takes the sRGB branch. Read for the legacy impact effects (B415); one for a material that states none.
/// </param>
/// <param name="Depth">
/// The depth state. Tested and unwritten for every particle and detail sprite; an entity sprite's render
/// mode can choose otherwise (B391).
/// </param>
/// <param name="Refract">
/// A <c>Refract</c> material's parameters, which warp a copy of the frame instead of drawing <see cref="Sheet"/>;
/// null for every other shader (B476).
/// </param>
/// <remarks>
/// **One of these per MATERIAL, not per system**, because that is what a draw call costs. A rocket
/// runs three systems — `rockettrail`, `rockettrail_burst`, `rockettrail_fire` — on three materials,
/// and the sheet and the blend are properties of the material rather than of the effect.
/// </remarks>
public readonly record struct ParticleMaterial(
    MapTexture? Sheet,
    IReadOnlyList<SheetSequence> Sequences,
    SpriteBlend Blend,
    float Alpha = 1f,
    SpriteDepth Depth = SpriteDepth.TestNoWrite,
    RefractMaterial? Refract = null)
{
    /// <summary>
    /// A <c>Cable</c> material's <c>$bumpmap</c>, whose blue squared shades the strip across its width
    /// (`cable_ps2x.fxc:40-49`); null for every other shader (B478).
    /// </summary>
    public MapTexture? CableBump { get; init; }

    /// <summary>
    /// Whether the material is drawn in GAMMA space: its texture read as stored, its colour scaled by the gamma light
    /// scale, and the result blended onto the frame's stored values — the <c>Sprite</c> shader under <c>$nosrgb</c>,
    /// which is its default (`sprite_dx9.cpp:63-67`, `:124`, `:163`; B521). False for every other shader, which work
    /// in linear light.
    /// </summary>
    /// <remarks>
    /// **Why it is a property of the material and not a detail of the draw**: adding a glow in gamma space and adding
    /// it in linear light are different pictures. Over a dark surface the stored sum is far brighter — a lamp's halo
    /// read 47-77% of TF2's until this was carried.
    /// </remarks>
    public bool GammaSpace { get; init; }

    /// <summary>
    /// <c>SpriteCard</c>'s <c>$overbrightfactor</c>, which multiplies the texel before the vertex colour
    /// (`spritecard_ps2x.fxc:143-144`); one for a material that states none and for every other shader (B523).
    /// </summary>
    public float Overbright { get; init; } = 1f;

    /// <summary>SpriteCard's size clamps and fades by distance (B529); the shader's defaults, which touch nothing, unless stated.</summary>
    public SpriteCardSize Size { get; init; } = SpriteCardSize.Default;

    /// <summary>
    /// Whether a particle's tint is a GAMMA colour the shader converts to linear light: <c>SpriteCard</c>'s vertex
    /// shader does, `float4 tint = GammaToLinear( v.vTint )` (`spritecard_vsxx.fxc:182`), and no other shader a
    /// particle draws through here (B538).
    /// </summary>
    public bool TintIsGamma { get; init; }

    /// <summary>Whether a shader converts a particle's tint from gamma — <c>SpriteCard</c> alone.</summary>
    /// <param name="shader">The material's shader name, or null when it did not resolve.</param>
    /// <returns>True for <c>SpriteCard</c>, in any case.</returns>
    public static bool TintIsGammaOf(string? shader) =>
        string.Equals(shader, "SpriteCard", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>The factor a material draws with, from its shader and what it states.</summary>
    /// <param name="shader">The material's shader name, or null when it did not resolve.</param>
    /// <param name="stated">Its <c>$overbrightfactor</c> as text — the <c>srgb?</c> form first, as for <c>$alpha</c> — or null.</param>
    /// <returns>The factor; one unless a <c>SpriteCard</c> material states a number.</returns>
    /// <remarks>
    /// `spritecard.cpp:81` declares the parameter and `:110` sets it to 1.0 when unset; no other shader this pass draws
    /// through reads it. The medigun's beam (<c>effects/medicbeam_curl</c>) states 3.
    /// </remarks>
    public static float OverbrightOf(string? shader, string? stated) =>
        string.Equals(shader, "SpriteCard", System.StringComparison.OrdinalIgnoreCase) &&
        float.TryParse(
            stated,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out float factor)
            ? factor
            : 1f;

    /// <summary>The material's shader name, or null when its VMT did not resolve.</summary>
    public string? Shader { get; init; }

    /// <summary>
    /// <c>IMaterial::IsTranslucent()</c>, which decides whether a particle definition's effect is a translucent renderable
    /// (B734, <see cref="IsTranslucentOf"/>); true unless the material was read and found opaque.
    /// </summary>
    public bool IsTranslucent { get; init; } = true;

    /// <summary>Whether a material is translucent to <c>IMaterial::IsTranslucent()</c> (B734).</summary>
    /// <param name="vmt">The material as read, or null when it did not resolve.</param>
    /// <param name="baseTextureHasAlpha">Whether its base texture has an alpha channel.</param>
    /// <returns>The answer; true for a material that did not resolve.</returns>
    /// <remarks>
    /// <c>CMaterial::IsTranslucentInternal</c> (materialsystem.dll build 11191711, <c>0x180008440</c>) answers true when the
    /// shader's render state is translucent (flag bit 6 at <c>+0x60</c>), when the alpha modulation is below one, or when
    /// the shader's own <c>IsTranslucent</c> does — <c>MATERIAL_VAR_TRANSLUCENT</c>. Per shader, from published source:
    /// <c>SpriteCard</c> enables alpha blending in every branch (`spritecard.cpp:255-267`); <c>Refract</c> sets
    /// <c>MATERIAL_VAR_TRANSLUCENT</c> on every material (`refract_dx9_helper.cpp:22`); <c>Sprite</c> blends in every
    /// render mode but <c>kRenderNormal</c> (`sprite_dx9.cpp:229-330`); the rest take <c>EvaluateBlendRequirements</c> —
    /// alpha modulation, <c>$vertexalpha</c>, or a base texture with alpha that is not alpha-tested, with <c>$additive</c>
    /// blending too (`BaseVSShader.cpp:1580-1606`).
    ///
    /// ***Interpolated:*** that render-state bit 6 is "the shader enabled blending in its snapshot" — its writer is in
    /// the closed shader system; every case above is consistent with it. A material that did not resolve is the engine's
    /// error material, whose translucency is not read here; it stays translucent, as every material was before B734.
    /// </remarks>
    public static bool IsTranslucentOf(VmtMaterial? vmt, bool baseTextureHasAlpha)
    {
        if (vmt is null)
        {
            return true;
        }

        if (Stated(vmt, "$translucent") || Stated(vmt, "$additive") ||
            (float.TryParse(
                vmt.Value("srgb?$alpha") ?? vmt.Value("$alpha"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float alpha) && alpha < 1f))
        {
            return true;
        }

        if (vmt.Shader.Equals("SpriteCard", System.StringComparison.OrdinalIgnoreCase) ||
            vmt.Shader.Equals("Refract", System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (vmt.Shader.Equals("Sprite", System.StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(vmt.Value("$spriterendermode"), out int mode) && mode != 0;
        }

        return Stated(vmt, "$vertexalpha") || (baseTextureHasAlpha && !Stated(vmt, "$alphatest"));
    }

    /// <summary>Whether a flag is stated and not zero.</summary>
    private static bool Stated(VmtMaterial vmt, string key) => vmt.Value(key) is { Length: > 0 } value && value.Trim() != "0";

    /// <summary>Whether the material's linear path ends in <c>TONEMAP_SCALE_LINEAR</c> (B378), from its shader.</summary>
    /// <remarks>
    /// Each shader's <c>FinalOutput</c>, read in published source:
    /// <code>
    /// SpriteCard     spritecard_ps2x.fxc:192                     TONEMAP_SCALE_LINEAR
    /// UnlitGeneric   vertexlit_and_unlit_generic_ps2x.fxc:498,504 TONEMAP_SCALE_LINEAR
    /// Cable          cable_ps2x.fxc:54                           TONEMAP_SCALE_LINEAR
    /// Sprite         sprite_ps2x.fxc:49 (SRGB; :51 is the gamma path, GammaSpace)  TONEMAP_SCALE_LINEAR
    /// UnlitTwoTexture unlittwotexture_ps2x.fxc:73                TONEMAP_SCALE_LINEAR
    /// Refract        refract_ps2x.fxc:207,249                    TONEMAP_SCALE_NONE
    /// </code>
    /// A shader not in the table, or none, keeps the linear scale: that was every sprite's behaviour before this was
    /// keyed, and the table holds every shader this pass is known to draw through.
    /// </remarks>
    public bool ScalesByToneMap => ScalesByToneMapOf(Shader) && Refract is null;

    /// <summary>The same question of a shader name alone.</summary>
    /// <param name="shader">The shader, or null.</param>
    /// <returns>False for <c>Refract</c> alone.</returns>
    public static bool ScalesByToneMapOf(string? shader) =>
        !string.Equals(shader, "Refract", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>A material that did not resolve, which draws nothing.</summary>
    public static ParticleMaterial None => new(null, [], SpriteBlend.Translucent);
}

/// <summary>How a sprite pass uses the depth buffer — the <c>Sprite</c> shader's per-mode state (B391).</summary>
public enum SpriteDepth
{
    /// <summary>Tested, not written: <c>EnableDepthWrites( false )</c>, every blended mode but the glows.</summary>
    TestNoWrite,

    /// <summary>Tested and written: <c>kRenderNormal</c>, which changes nothing from the initial shadow state.</summary>
    TestAndWrite,

    /// <summary>Neither: the glow modes' <c>EnableDepthTest( false )</c> (`sprite_dx9.cpp:265`).</summary>
    Off,
}

/// <summary>One draw of a sprite — the blend and depth state of one <c>Draw()</c> in the shader (B391).</summary>
/// <param name="Blend">The blend.</param>
/// <param name="Depth">The depth state.</param>
public readonly record struct SpritePass(SpriteBlend Blend, SpriteDepth Depth);

/// <summary>
/// One draw's worth of particle quads: the corners, and the material they belong to.
/// </summary>
/// <param name="Corners">Six per particle, camera-facing.</param>
/// <param name="Material">What to draw them with.</param>
/// <param name="Placement">Where in the translucent walk it draws, or null for after it (B734).</param>
/// <param name="Pass">Which of the view's passes draws it (B734).</param>
/// <remarks>
/// **A batch per material is not an optimisation, it is the minimum.** The quads of an additive
/// glow and a translucent smoke cannot share a draw call, because the blend state differs — and
/// `Device3D.SetParticles` used to take exactly one sheet, which is the limit its own remarks
/// flagged as *"a batching question to answer when a second effect exists rather than now"*. A
/// second effect now exists.
/// </remarks>
public readonly record struct ParticleBatch(
    IReadOnlyList<DetailSpriteVertex> Corners,
    ParticleMaterial Material,
    TranslucentPlacement? Placement = null,
    ParticlePass Pass = ParticlePass.Translucent);

/// <summary>Which of a view's passes draws a batch (B734).</summary>
public enum ParticlePass
{
    /// <summary>
    /// The translucent walk at its <see cref="ParticleBatch.Placement"/> (<c>DrawTranslucentRenderables</c>); with no placement,
    /// after the view, unsorted — the sun's overlay and the client effects (<c>DrawEffects</c>, `viewrender.cpp:1462-1463`).
    /// </summary>
    Translucent,

    /// <summary>
    /// The end of the view's opaque pass, after its opaque models (<c>DrawOpaqueRenderables</c>): an opaque renderable — a
    /// <c>kRenderNormal</c> sprite, whose sprite model is never translucent to <c>IsTranslucent</c> — and the rope cache
    /// (`viewrender.cpp:4267-4271`).
    /// </summary>
    Opaque,

    /// <summary>
    /// <c>DrawNoZBufferTranslucentRenderables</c> after the walk (`viewrender.cpp:4427-4458`): a translucent renderable whose
    /// <c>IgnoresZBuffer</c> is true — a glow-mode sprite or trail, any beam — drawn in the walk's order by its placement.
    /// </summary>
    IgnoresDepth,

    /// <summary>
    /// The main view's <c>DrawViewModels</c>, after the viewmodels, through their camera (`viewrender.cpp:1117-1150`): a
    /// particle effect whose definition is a <c>view model effect</c>, filed <c>RENDER_GROUP_VIEW_MODEL_TRANSLUCENT</c>
    /// (`particlemgr.cpp:1277-1280`). No other view draws it.
    /// </summary>
    ViewModel,

    /// <summary>
    /// <c>DrawParticleSingletons</c>, at the start of a view's translucent pass and before its leaf walk, uncollated
    /// (`viewrender.cpp:4570`, `particles_ez.cpp:138-159`): the simple singleton's particles, which <c>AddSimpleParticle</c>
    /// feeds (B734).
    /// </summary>
    Singletons,
}

/// <summary>Where a translucent renderable draws in its view's translucent walk (B734).</summary>
/// <param name="Place">Its nearest listed leaf's place in the view's world list — <c>ComputeTranslucentRenderLeaf</c>.</param>
/// <param name="Centre">Its world box centre, the key <c>SortEntities</c> dots with the view forward.</param>
/// <param name="Renderable">
/// Which renderable it is, so one effect's batches on several materials draw as one entry: <c>DrawModel</c> renders an
/// effect whole (<c>particles_new.cpp:509</c>).
/// </param>
/// <param name="Origin">
/// Its render origin, <c>m_vSortOrigin</c> (<c>particles_new.h:224-227</c>): the detail sprites of its leaf farther than
/// this draw before it (<c>viewrender.cpp:4605-4607</c>).
/// </param>
/// <remarks>
/// A batch with none draws after the walk: the sun's overlay, which the engine draws there, and what ignores the depth
/// buffer — the beams and the glow-mode sprites and trails — which <c>DrawNoZBufferTranslucentRenderables</c> draws there
/// (`viewrender.cpp:4427-4458`). The sub-views' sprites and trails, and the impacts, are unplaced and open (B734).
/// </remarks>
public readonly record struct TranslucentPlacement(
    int Place, (float X, float Y, float Z) Centre, int Renderable, (float X, float Y, float Z) Origin);
