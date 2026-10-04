namespace Tf2DemoSalvage.Core.Scene;

/// <summary>What a <c>CSpriteTrail</c> says about itself — <c>DT_SpriteTrail</c>, plus the two <c>DT_Sprite</c> fields its render origin reads.</summary>
/// <param name="LifeTime"><c>m_flLifeTime</c>: how long a trail point lives, in seconds.</param>
/// <param name="StartWidth"><c>m_flStartWidth</c>: the width at a point's birth.</param>
/// <param name="EndWidth">
/// <c>m_flEndWidth</c>: the width at its death, or negative for "the start width throughout" — the constructor's −1
/// (`SpriteTrail.cpp:129`), which is what every baseball, arrow and repair-claw trail in the corpus keeps.
/// </param>
/// <param name="StartWidthVariance"><c>m_flStartWidthVariance</c>: the ± range a new point's width is drawn from.</param>
/// <param name="TextureRes"><c>m_flTextureRes</c>: texture repeats per unit of trail length.</param>
/// <param name="MinFadeLength"><c>m_flMinFadeLength</c>: the length over which the tail fades however young it is.</param>
/// <param name="SkyboxOrigin"><c>m_vecSkyboxOrigin</c>; zero unless the trail is in the 3D skybox.</param>
/// <param name="SkyboxScale"><c>m_flSkyboxScale</c>; one unless the trail is in the 3D skybox (`SpriteTrail.cpp:128`).</param>
/// <param name="AttachedTo">
/// <c>DT_Sprite.m_hAttachedToEntity</c>, a raw handle: <c>GetRenderOrigin</c> asks this entity for its attachment
/// (`SpriteTrail.cpp:537-553`), and the client dereferences it when it draws, so the serial is checked there.
/// </param>
/// <param name="Attachment"><c>DT_Sprite.m_nAttachment</c>: which attachment of that entity.</param>
/// <remarks>
/// **`DT_SpriteTrail` inherits `DT_Sprite`, so a trail was always admitted** — unlike a beam — and drawn as a plain sprite
/// quad of its own material. The trail is the points the CLIENT samples from where this entity is drawn each frame;
/// none of them is on the wire, so this record is only the parameters of that sampling.
/// </remarks>
public sealed record SceneSpriteTrail(
    float LifeTime,
    float StartWidth,
    float EndWidth,
    float StartWidthVariance,
    float TextureRes,
    float MinFadeLength,
    (float X, float Y, float Z) SkyboxOrigin,
    float SkyboxScale,
    int AttachedTo,
    int Attachment);
