namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// How a sprite's quad is turned to face the world — <c>C_SpriteRenderer::SPRITETYPE</c>
/// (`game/shared/Sprite.h:36-43`), with the engine's own numbers.
/// </summary>
/// <remarks>
/// **In the MATERIAL layer, beneath the renderer that draws it, because the material decides it**
/// (B390). The `Sprite` shader translates <c>$spriteorientation</c> from a name into one of these at
/// material init (`materialsystem/stdshaders/sprite_dx9.cpp:73`), before `CEngineSprite::Init` reads the
/// result (`game/client/spritemodel.cpp:308`). Both layers need the numbers, and the engine keeps two
/// copies of them for exactly that reason — the comment above `Sprite.h`'s enum says *"WARNING! Change
/// these in common/MaterialSystem/Sprite.cpp if you change them here!"* One definition beneath both
/// consumers is that warning acted on rather than copied.
/// </remarks>
public enum SpriteOrientation
{
    /// <summary><c>SPR_VP_PARALLEL_UPRIGHT</c> — up is world up, right lies in the viewplane. The default.</summary>
    ParallelUpright = 0,

    /// <summary><c>SPR_FACING_UPRIGHT</c> — up is world up, right turned away from the world origin.</summary>
    FacingUpright = 1,

    /// <summary><c>SPR_VP_PARALLEL</c> — flat to the viewplane, the ordinary billboard.</summary>
    Parallel = 2,

    /// <summary><c>SPR_ORIENTED</c> — the entity's own angles.</summary>
    Oriented = 3,

    /// <summary><c>SPR_VP_PARALLEL_ORIENTED</c> — flat to the viewplane, turned by the entity's roll.</summary>
    ParallelOriented = 4,
}
