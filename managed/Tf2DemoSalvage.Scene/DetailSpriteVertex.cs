namespace Tf2DemoSalvage.Scene;

/// <summary>One corner of a detail sprite's quad, in world space.</summary>
/// <param name="X">Where it is, east-west.</param>
/// <param name="Y">Where it is, north-south.</param>
/// <param name="Z">Where it is, vertically.</param>
/// <param name="U">Texture coordinate across the shared sprite sheet.</param>
/// <param name="V">Texture coordinate down it.</param>
/// <param name="Red">The colour <c>vrad</c> baked for the sprite, 0 to 1.</param>
/// <param name="Green">The colour <c>vrad</c> baked for the sprite, 0 to 1.</param>
/// <param name="Blue">The colour <c>vrad</c> baked for the sprite, 0 to 1.</param>
/// <param name="Alpha">
/// The distance fade, 0 to 1 — <c>CDetailModel::SetAlpha</c>, computed per view by
/// <see cref="DetailFade"/>. It is a whole-sprite value rather than a per-corner one, so all four
/// corners of a quad carry the same number.
/// </param>
/// <param name="NextU">
/// The same coordinate in the frame this one is animating TOWARD, across.
/// </param>
/// <param name="NextV">The same, down.</param>
/// <param name="Blend">
/// How far between the two frames, 0 to 1 — <c>0</c> means "show <paramref name="U"/> alone", which
/// is what a detail sprite passes.
/// </param>
/// <remarks>
/// **Its own vertex type, rather than the world's, because the world's has nowhere to put alpha.**
/// <see cref="WorldVertex"/>'s `Alpha` is the two-texture blend a `WorldVertexTransition`
/// displacement uses, not opacity — overloading it would give one field two meanings and a
/// displacement's blend would fade the grass.
///
/// **This is also what the engine has.** Detail sprites are drawn by `CDetailObjectSystem` through
/// one material of its own with its own mesh, not through the world's surface path.
///
/// **Two texture coordinate sets and a blend, because that is Valve's OWN vertex format for a
/// sprite card.** `spritecard.cpp:271` lists them: texcoord `0` is "sheet bounding uvs, frame0",
/// texcoord `1` is "frame 1", and texcoord `2` carries "frame blend, rot, radius". The pixel shader
/// samples both and mixes them — `blended_rgb = lerp( baseTex0, baseTex1, i.blendfactor0.x )`
/// (`spritecard_ps2x.fxc:77`) — and `BLENDFRAMES` defaults to ON (`spritecard.cpp:143`), so a
/// particle drawn from one frame is a divergence rather than a simplification (B373).
///
/// **A detail sprite passes <c>Blend</c> of zero and the same coordinates twice**, which makes the
/// lerp an identity and leaves that pass drawing exactly what it drew before. That is deliberate:
/// one sprite pipeline serves both, and two that must agree about blending is the defect shape this
/// project has already met.
/// </remarks>
public readonly record struct DetailSpriteVertex(
    float X, float Y, float Z, float U, float V,
    float Red, float Green, float Blue, float Alpha,
    float NextU, float NextV, float Blend);
