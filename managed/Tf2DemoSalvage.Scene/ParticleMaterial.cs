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
    SpriteDepth Depth = SpriteDepth.TestNoWrite)
{
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
/// <remarks>
/// **A batch per material is not an optimisation, it is the minimum.** The quads of an additive
/// glow and a translucent smoke cannot share a draw call, because the blend state differs — and
/// `Device3D.SetParticles` used to take exactly one sheet, which is the limit its own remarks
/// flagged as *"a batching question to answer when a second effect exists rather than now"*. A
/// second effect now exists.
/// </remarks>
public readonly record struct ParticleBatch(
    IReadOnlyList<DetailSpriteVertex> Corners,
    ParticleMaterial Material);
