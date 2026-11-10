using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where an attached entity sprite is drawn, and whether what it is attached to is a viewmodel.</summary>
/// <param name="Origin">
/// <c>CSprite::GetRenderOrigin</c>: the attached entity's attachment, or that entity's origin when it has none
/// (`Sprite.cpp:817-833`).
/// </param>
/// <param name="OnViewModel">
/// Whether the attached entity's render group is <c>RENDER_GROUP_VIEW_MODEL_OPAQUE</c> or <c>_TRANSLUCENT</c>, which
/// <c>DrawSprite</c> refuses in a reflection (`c_sprite.cpp:395-401`).
/// </param>
public readonly record struct SpritePlace(Vector3 Origin, bool OnViewModel);
