using System;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>A static prop's fade entry, as the engine keeps it in the list at <c>CStaticPropMgr+0x78</c> (B430).</summary>
/// <param name="Minimum">Where the fade starts: SQUARED for a distance fade, as written for a screen-space one.</param>
/// <param name="Maximum">Where the prop is gone, in the same units.</param>
/// <param name="Scale">255 over the band, the multiplier the per-frame lerp uses.</param>
/// <param name="ScreenSpace"><c>STATIC_PROP_SCREEN_SPACE_FADE</c>: the band is a screen size, not a distance.</param>
/// <remarks>
/// **Not <c>EntityFade.DistanceAlpha</c>, though the lerp is the same shape.** An entity's
/// <c>ComputeDistanceFade</c> swaps reversed bounds, turns a negative minimum into
/// <c>max - 400</c> and skips a 0/0 band; the static prop path does none of that — the entry is
/// built once from the lump and used as it stands, so a prop whose flag is set with a zero maximum
/// is simply never drawn. Sharing the entity function would port those branches onto props that
/// the engine never gave them.
/// </remarks>
public readonly record struct StaticPropFade(float Minimum, float Maximum, float Scale, bool ScreenSpace)
{
    /// <summary><c>STATIC_PROP_FLAG_FADES</c>, `public/gamebspfile.h:126`.</summary>
    public const int FadesFlag = 0x1;

    /// <summary><c>STATIC_PROP_SCREEN_SPACE_FADE</c>, `public/gamebspfile.h:132`.</summary>
    public const int ScreenSpaceFlag = 0x20;

    /// <summary>The engine's opaque alpha.</summary>
    private const float Opaque = 255f;

    /// <summary>The entry `UnserializeModels` (`engine.dll` `0x180206590`) builds, or null when it builds none.</summary>
    /// <param name="flags">The lump's <c>m_Flags</c>.</param>
    /// <param name="minimum">The lump's <c>m_FadeMinDist</c>.</param>
    /// <param name="maximum">The lump's <c>m_FadeMaxDist</c>.</param>
    /// <returns>The entry, or null without <see cref="FadesFlag"/>.</returns>
    /// <remarks>
    /// Transcribed: the bounds are squared unless the fade is screen-space; the scale is 255 when
    /// they are equal (`piVar8[3] = 0x437f0000`), else <c>255 / (max - min)</c>, or
    /// <c>255 / (min - max)</c> for screen space.
    /// </remarks>
    public static StaticPropFade? For(int flags, float minimum, float maximum)
    {
        if ((flags & FadesFlag) == 0)
        {
            return null;
        }

        bool screen = (flags & ScreenSpaceFlag) != 0;

        if (!screen)
        {
            minimum *= minimum;
            maximum *= maximum;
        }

        // The engine's own exact comparison (`if (fVar18 == fVar17)`), so a range would diverge from it.
#pragma warning disable S1244
        if (maximum == minimum)
#pragma warning restore S1244
        {
            return new StaticPropFade(minimum, maximum, Opaque, screen);
        }

        float band = screen ? minimum - maximum : maximum - minimum;

        return new StaticPropFade(minimum, maximum, Opaque / band, screen);
    }

    /// <summary>The prop's alpha this frame — the distance branch of `engine.dll` `FUN_180202c60`.</summary>
    /// <param name="origin">The prop's render origin.</param>
    /// <param name="view">The view origin <c>ComputePropOpacity</c> (`0x180203030`) stored.</param>
    /// <param name="factor">
    /// <c>GetFOVDistanceAdjustFactor()</c>, stored beside it; negative (<c>cl_leveloverview</c>) disables fading.
    /// </param>
    /// <returns>0 to 255.</returns>
    /// <remarks>
    /// The factor multiplies the offset BEFORE squaring, so a half factor halves the distance. A
    /// screen-space entry is not a distance and is answered opaque here; see B430 for why that branch
    /// is not ported.
    /// </remarks>
    public byte Alpha((float X, float Y, float Z) origin, (float X, float Y, float Z) view, float factor)
    {
        // ponytail: screen-space entries draw opaque; the branch needs the view's projected screen size.
        if (factor < 0f || ScreenSpace)
        {
            return (byte)Opaque;
        }

        float x = (origin.X - view.X) * factor;
        float y = (origin.Y - view.Y) * factor;
        float z = (origin.Z - view.Z) * factor;
        float here = (x * x) + (y * y) + (z * z);

        if (here >= Maximum)
        {
            return 0;
        }

        if (Minimum < 0f || here <= Minimum)
        {
            return (byte)Opaque;
        }

        return (byte)Math.Clamp((int)((Maximum - here) * Scale), 0, (int)Opaque);
    }
}
