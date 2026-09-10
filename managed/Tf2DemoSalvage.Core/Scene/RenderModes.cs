// **Core rather than Scene, and one caller decided it** (B385). `ShouldInterpolate`'s third clause is
// `IsVisible()`, which is leaf-system membership, which `UpdateVisibility` sets from `ShouldDraw()` —
// and `ShouldDraw`'s first test is `m_nRenderMode == kRenderNone` (`c_baseentity.cpp:1444`). That
// makes the interpolation list a question `DemoTimeline` has to answer, and `Tf2DemoSalvage.Core`
// cannot see `Tf2DemoSalvage.Scene`. `FxBlend` and `RenderFx` stayed behind: they are what a renderer
// does with the mode, not what an entity is.
namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The <c>RenderMode_t</c> values, <c>public/const.h:351</c>.</summary>
/// <remarks>
/// **All eleven, and it used to be two.** The old note in `RenderGroups` said *"two of eleven,
/// because two are all that `GetRenderGroup` tests"* — right about the grouping and wrong as a
/// general rule, because `ComputeFxBlend`'s default branch tests <see cref="Normal"/> against
/// everything else. A real match carries <see cref="Glow"/>, <see cref="TransAdd"/> and 118
/// entities at <see cref="None"/> (`CorpusRenderModeTests`), so the other nine are facts about the
/// data rather than claims about what this project handles.
///
/// Sent as 8 bits unsigned, `baseentity.cpp:277`.
/// </remarks>
public static class RenderModes
{
    /// <summary><c>kRenderNormal</c> — the entity's own materials decide, and nothing else.</summary>
    public const int Normal = 0;

    /// <summary><c>kRenderTransColor</c> — <c>c*a+dest*(1-a)</c>.</summary>
    public const int TransColor = 1;

    /// <summary><c>kRenderTransTexture</c> — <c>src*a+dest*(1-a)</c>.</summary>
    public const int TransTexture = 2;

    /// <summary><c>kRenderGlow</c> — <c>src*a+dest</c>, no Z checks, fixed size in screen space.</summary>
    public const int Glow = 3;

    /// <summary><c>kRenderTransAlpha</c> — <c>src*srca+dest*(1-srca)</c>.</summary>
    public const int TransAlpha = 4;

    /// <summary><c>kRenderTransAdd</c> — <c>src*a+dest</c>.</summary>
    public const int TransAdd = 5;

    /// <summary><c>kRenderEnvironmental</c> — *"not drawn, used for environmental effects"*.</summary>
    public const int Environmental = 6;

    /// <summary><c>kRenderTransAddFrameBlend</c> — blends between animation frames.</summary>
    public const int TransAddFrameBlend = 7;

    /// <summary><c>kRenderTransAlphaAdd</c> — <c>src + dest*(1-a)</c>.</summary>
    public const int TransAlphaAdd = 8;

    /// <summary><c>kRenderWorldGlow</c> — as <see cref="Glow"/>, but not fixed in screen space.</summary>
    public const int WorldGlow = 9;

    /// <summary><c>kRenderNone</c> — *"Don't render."*</summary>
    public const int None = 10;
}
