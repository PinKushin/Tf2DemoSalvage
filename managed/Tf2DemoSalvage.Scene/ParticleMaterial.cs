using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Everything drawing one particle system's material needs (B373).
/// </summary>
/// <param name="Sheet">Its texture, or null when it did not resolve.</param>
/// <param name="Sequences">The animation sequences that texture declares, empty when it has none.</param>
/// <param name="Blend">How it blends, from its own <c>$additive</c> and friends.</param>
/// <remarks>
/// **One of these per MATERIAL, not per system**, because that is what a draw call costs. A rocket
/// runs three systems — `rockettrail`, `rockettrail_burst`, `rockettrail_fire` — on three materials,
/// and the sheet and the blend are properties of the material rather than of the effect.
/// </remarks>
public readonly record struct ParticleMaterial(
    MapTexture? Sheet,
    IReadOnlyList<SheetSequence> Sequences,
    SpriteBlend Blend)
{
    /// <summary>A material that did not resolve, which draws nothing.</summary>
    public static ParticleMaterial None => new(null, [], SpriteBlend.Translucent);
}

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
