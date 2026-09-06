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
/// <remarks>
/// **Its own vertex type, rather than the world's, because the world's has nowhere to put alpha.**
/// <see cref="WorldVertex"/>'s `Alpha` is the two-texture blend a `WorldVertexTransition`
/// displacement uses, not opacity — overloading it would give one field two meanings and a
/// displacement's blend would fade the grass.
///
/// **This is also what the engine has.** Detail sprites are drawn by `CDetailObjectSystem` through
/// one material of its own with its own mesh, not through the world's surface path.
/// </remarks>
public readonly record struct DetailSpriteVertex(
    float X, float Y, float Z, float U, float V,
    float Red, float Green, float Blue, float Alpha);
