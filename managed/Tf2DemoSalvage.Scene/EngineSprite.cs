using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// A sprite as loaded — its material, its size, its orientation and where its quad sits about its
/// origin. <c>CEngineSprite</c> (`game/client/spritemodel.h`).
/// </summary>
/// <param name="Material">What it is drawn with.</param>
/// <param name="Width">The material's width, in texels.</param>
/// <param name="Height">Its height.</param>
/// <param name="Orientation">The material's <c>$spriteorientation</c>, as the shader translated it.</param>
/// <param name="Extents">Where the quad's edges sit, from the material's <c>$spriteorigin</c>.</param>
/// <param name="ConstantColor">
/// The material's <c>$color</c> and <c>$alpha</c>, which <c>kRenderTransAdd</c> multiplies the texture
/// by; see <see cref="VmtMaterial.SpriteConstantColor"/>.
/// </param>
/// <param name="IgnoresVertexColors">
/// Whether <c>kRenderTransAdd</c> leaves the entity's color and brightness out; see
/// <see cref="VmtMaterial.IgnoresVertexColors"/>.
/// </param>
/// <param name="Shader">
/// The material's shader. <c>CEngineSprite::Init</c> stamps <c>$spriteRenderMode</c> on a copy of the material per
/// render mode (`spritemodel.cpp:287-290`), and only the <c>Sprite</c> shader reads it — a sprite trail's
/// <c>UnlitGeneric</c> or <c>Refract</c> material draws by its own text whatever the entity's mode says.
/// </param>
/// <param name="Modulation">
/// <c>$color</c> times <c>$color2</c>, and <c>$alpha</c> — what a non-Sprite shader multiplies its output by
/// (<see cref="VmtMaterial.Modulation"/>). The Sprite shader reads <see cref="ConstantColor"/> instead.
/// </param>
/// <param name="VertexColour">Whether a non-Sprite shader multiplies in the vertex colour — <c>$vertexcolor</c>.</param>
/// <param name="VertexAlpha">Whether it multiplies in the vertex alpha — <c>$vertexalpha</c>.</param>
/// <remarks>
/// **Built once, at load, as the engine builds it** (B390). `CEngineSprite::Init` reads the orientation
/// and the origin a single time and keeps four edges; nothing per frame looks at the material's text
/// again. Until B390 this project read neither key, so every sprite drew upright and centred whatever
/// its material said — `light_glow03` among them, which asks for `vp_parallel`.
///
/// **A wrapper around a <see cref="ParticleMaterial"/> rather than more fields on one**, because a
/// particle's material has no orientation and no origin: `SpriteCard` reads neither. The batch the
/// renderer draws still takes the particle material, so the draw path stays one path.
/// </remarks>
public readonly record struct EngineSprite(
    ParticleMaterial Material,
    int Width,
    int Height,
    SpriteOrientation Orientation,
    SpriteExtents Extents,
    (float Red, float Green, float Blue, float Alpha) ConstantColor,
    bool IgnoresVertexColors,
    string Shader = EngineSprite.SpriteShader,
    (float Red, float Green, float Blue, float Alpha)? Modulation = null,
    bool VertexColour = false,
    bool VertexAlpha = false)
{
    /// <summary>The shader that reads <c>$spriteRenderMode</c>.</summary>
    public const string SpriteShader = "Sprite";

    /// <summary>Whether this material is drawn by the <c>Sprite</c> shader, which switches on the entity's render mode.</summary>
    public bool IsSpriteShader => Shader.Equals(SpriteShader, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>A sprite as <c>CEngineSprite::Init</c> builds it from its material.</summary>
    /// <param name="texture">The material's texture.</param>
    /// <param name="sequences">The animation sequences that texture declares.</param>
    /// <param name="blend">How the material's own text says it blends.</param>
    /// <param name="orientation">Its <c>$spriteorientation</c>, as the shader translated it.</param>
    /// <param name="origin">Its <c>$spriteorigin</c>, or null when it declares no vector.</param>
    /// <param name="constantColor">Its <c>$color</c> and <c>$alpha</c>, as the Sprite shader packs them.</param>
    /// <param name="ignoresVertexColors">Its <c>$ignorevertexcolors</c>, true when absent.</param>
    /// <param name="shader">Its shader, <c>Sprite</c> unless the material names another.</param>
    /// <param name="modulation">Its <c>$color</c> × <c>$color2</c> and <c>$alpha</c>, for a non-Sprite shader.</param>
    /// <param name="vertexColour">Its <c>$vertexcolor</c>.</param>
    /// <param name="vertexAlpha">Its <c>$vertexalpha</c>.</param>
    /// <returns>The sprite.</returns>
    /// <remarks>
    /// **Sized by the MAPPING size, the texture as authored** (B390):
    ///
    /// <code>
    /// m_width = m_material[0]-&gt;GetMappingWidth();
    /// m_height = m_material[0]-&gt;GetMappingHeight();
    /// </code>
    ///
    /// (`spritemodel.cpp:294-295`). The material's mapping size is its representative texture's — the
    /// <c>$basetexture</c> for every sprite, per the disassembly `docs/RISKS.md` B390 quotes — and a
    /// texture's is its header's, never the level the quality cap decoded. Sized by the decoded level,
    /// a glow shrank by exactly the factor the cap dropped, and its edges moved in with it.
    /// </remarks>
    public static EngineSprite Init(
        MapTexture texture,
        IReadOnlyList<SheetSequence> sequences,
        SpriteBlend blend,
        SpriteOrientation orientation,
        (float X, float Y)? origin,
        (float Red, float Green, float Blue, float Alpha) constantColor,
        bool ignoresVertexColors,
        string shader = SpriteShader,
        (float Red, float Green, float Blue, float Alpha)? modulation = null,
        bool vertexColour = false,
        bool vertexAlpha = false) =>
        new(
            new ParticleMaterial(texture, sequences, blend),
            texture.MappingWidth,
            texture.MappingHeight,
            orientation,
            SpriteExtents.Of(texture.MappingWidth, texture.MappingHeight, origin),
            constantColor,
            ignoresVertexColors,
            shader,
            modulation,
            vertexColour,
            vertexAlpha);
}

/// <summary>
/// Where a sprite's quad sits about its origin, in texels — <c>CEngineSprite</c>'s <c>up</c>,
/// <c>down</c>, <c>left</c> and <c>right</c>.
/// </summary>
/// <param name="Left">The left edge; negative unless the origin is on or beyond it.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Up">The top edge.</param>
/// <param name="Down">The bottom edge; negative unless the origin is on or below it.</param>
public readonly record struct SpriteExtents(float Left, float Right, float Up, float Down)
{
    /// <summary>The edges <c>CEngineSprite::Init</c> derives (`spritemodel.cpp:311-328`).</summary>
    /// <param name="width">The material's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="origin">Its <c>$spriteorigin</c>, or null when it declares no vector.</param>
    /// <returns>The four edges.</returns>
    /// <remarks>
    /// <code>
    /// if( !originVar || ( originVar-&gt;GetType() != MATERIAL_VAR_TYPE_VECTOR ) )
    /// {
    ///     origin[0] = -m_width * 0.5f;
    ///     origin[1] = m_height * 0.5f;
    /// }
    /// else
    /// {
    ///     originVar-&gt;GetVecValue( &amp;originVarValue[0], 3 );
    ///     origin[0] = -m_width * originVarValue[0];
    ///     origin[1] = m_height * originVarValue[1];
    /// }
    ///
    /// up    = origin[1];
    /// down  = origin[1] - m_height;
    /// left  = origin[0];
    /// right = m_width + origin[0];
    /// </code>
    ///
    /// **Only the DEFAULT is symmetric.** `[ 0.50 0.00 ]`, which three shipped materials declare, gives
    /// <c>up = 0</c> and <c>down = -height</c>: the whole quad below its origin. The arithmetic this
    /// replaced took half the width and half the height either side, which is the default and nothing
    /// else.
    ///
    /// **X is negated and Y is not, and that is the engine's, not a slip.** The origin is a position in
    /// the texture measured from its top-left, with x running right and y running DOWN — so a larger X
    /// puts more of the quad left of the origin, and a larger Y puts more of it above.
    /// </remarks>
    public static SpriteExtents Of(int width, int height, (float X, float Y)? origin)
    {
        (float x, float y) = origin ?? (0.5f, 0.5f);

        float left = -width * x;
        float top = height * y;

        return new SpriteExtents(left, width + left, top, top - height);
    }
}
