namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// How a <c>SpriteCard</c> material blends with what is behind it (B373).
/// </summary>
/// <remarks>
/// **Three, because the shader offers exactly three** — `spritecard.cpp:255-270`:
///
/// <code>
/// if ( bAdditive2ndTexture || bAddOverBlend || bAddSelf )
///     EnableAlphaBlending( SHADER_BLEND_ONE, SHADER_BLEND_ONE_MINUS_SRC_ALPHA );
/// else if ( IS_FLAG_SET(MATERIAL_VAR_ADDITIVE) )
///     EnableAlphaBlending( SHADER_BLEND_SRC_ALPHA, SHADER_BLEND_ONE );
/// else
///     EnableAlphaBlending( SHADER_BLEND_SRC_ALPHA, SHADER_BLEND_ONE_MINUS_SRC_ALPHA );
/// </code>
///
/// **The order matters and is not obvious**: `$addself` and `$addoverblend` OUTRANK `$additive`, so
/// a material setting both takes the first branch. Testing `$additive` first would give 41 of TF2's
/// materials the wrong blend — the ones that set `$addself`.
///
/// **Measured**, with `particles materials`: 304 of 697 shipped `SpriteCard` materials set
/// `$additive`, 41 set `$addself`, 3 set `$addoverblend`. Drawing all of them translucent is not an
/// edge case going wrong; it is nearly half of every effect in the game.
/// </remarks>
public enum SpriteBlend
{
    /// <summary>Source alpha over one minus source alpha — smoke, and anything not marked.</summary>
    Translucent,

    /// <summary>Source alpha plus destination — sparks, glows, muzzle flashes.</summary>
    Additive,

    /// <summary>One plus one minus source alpha, for a material that adds itself.</summary>
    AddOver,
}
