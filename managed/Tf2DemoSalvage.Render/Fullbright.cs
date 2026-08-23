namespace Tf2DemoSalvage.Render;

/// <summary>How lighting and albedo are substituted for a debug view — Valve's <c>mat_fullbright</c>.</summary>
/// <remarks>
/// **Three states, not two, and the name is what misleads.** `mat_fullbright` is declared `"0"` and
/// reads like a switch; every shader that consults it tests for a third value:
///
/// <code>
/// bool bLightingOnly = mat_fullbright.GetInt() == 2 &amp;&amp; !IS_FLAG_SET( MATERIAL_VAR_NO_DEBUG_OVERRIDE );
/// if( bLightingOnly )
///     s_pShaderAPI-&gt;BindStandardTexture( SHADER_SAMPLER1, TEXTURE_GREY );
/// </code>
///
/// <c>BaseVSShader.cpp:1094</c>, and the same expression appears in `WorldVertexTransition_dx8`,
/// `cable_dx9` and others. An implementation begun from the name would have shipped two thirds of
/// the feature and looked finished.
///
/// **Each state is a texture SUBSTITUTION rather than a shader branch**, which is why they compose
/// with everything else a material does — a substituted albedo still gets its detail texture, its
/// envmap and its alpha test. Both replacements are named in Valve's standard texture list
/// (<c>ishaderdynamic.h:60</c>).
///
/// **The two answer different questions**, which is the reason both exist:
///
/// | value | substitution | answers |
/// |---|---|---|
/// | 1 | lightmap becomes <c>TEXTURE_LIGHTMAP_FULLBRIGHT</c> | is that dark patch a shadow, or a missing texture? |
/// | 2 | albedo becomes <c>TEXTURE_GREY</c> | is that shape in the lighting, or painted into the texture? |
/// </remarks>
public enum Fullbright
{
    /// <summary>Lighting and albedo both as the material asks.</summary>
    Off = 0,

    /// <summary>The lightmap is replaced by a fully-lit one, so nothing is shadowed.</summary>
    NoLighting = 1,

    /// <summary>The albedo is replaced by flat grey, so only the lighting is visible.</summary>
    LightingOnly = 2,
}
