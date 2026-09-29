using System;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>What a static prop's screen fades need from the prop itself (B432).</summary>
/// <param name="Radius">
/// <c>prop+0x98</c>: half the diagonal of the model's render bounds, from <c>CStaticProp::Init</c> — the
/// sphere `FUN_180202c60` projects.
/// </param>
/// <param name="ForcedFadeScale">The lump's <c>m_flForcedFadeScale</c> (<c>prop+0x4c</c>).</param>
public readonly record struct StaticPropScreen(float Radius, float ForcedFadeScale);

/// <summary>The main view, as <c>ComputePixelWidthOfSphere</c> projects through it (B432).</summary>
/// <param name="Eye">The view origin.</param>
/// <param name="Forward">Unit view direction: a point's clip <c>w</c> is its depth along it.</param>
/// <param name="Up">Unit camera up — row 1 of the view matrix, which `FUN_180034f70` caches.</param>
/// <param name="ProjectionY">The projection's <c>[1][1]</c>, <c>cot(fovY / 2)</c>.</param>
/// <param name="ViewportHeight">The viewport's height in pixels (the 4th out of vtable <c>+0x138</c>).</param>
public readonly record struct ScreenFadeView(
    (float X, float Y, float Z) Eye,
    (float X, float Y, float Z) Forward,
    (float X, float Y, float Z) Up,
    float ProjectionY,
    float ViewportHeight)
{
    /// <summary>`materialsystem.dll` <c>ComputePixelWidthOfSphere</c>, <c>FUN_180012710</c>.</summary>
    /// <param name="center">The sphere's centre.</param>
    /// <param name="radius">Its radius.</param>
    /// <returns>Its projected width in pixels.</returns>
    /// <remarks>
    /// Twice <c>ComputePixelDiameterOfSphere</c> (<c>FUN_180030f90</c>): <c>center ± radius · up</c> through
    /// view-projection, each <c>y / w</c> — or <c>y · 1000</c> when <c>w &lt; 0.001</c> (<c>0x1800bd540</c>,
    /// <c>0x1800bd550</c>) — then <c>|Δ| · height · 0.5</c> (<c>0x1800b5b10</c>). The "diameter" is half
    /// the NDC span in pixels, so the width is the whole span.
    /// </remarks>
    public float PixelWidthOfSphere((float X, float Y, float Z) center, float radius)
    {
        float top = Ndc(center, radius);
        float bottom = Ndc(center, -radius);

        return 2f * (MathF.Abs(top - bottom) * ViewportHeight * 0.5f);
    }

    private float Ndc((float X, float Y, float Z) center, float along)
    {
        float x = center.X + (Up.X * along) - Eye.X;
        float y = center.Y + (Up.Y * along) - Eye.Y;
        float z = center.Z + (Up.Z * along) - Eye.Z;

        float clipY = ((x * Up.X) + (y * Up.Y) + (z * Up.Z)) * ProjectionY;
        float clipW = (x * Forward.X) + (y * Forward.Y) + (z * Forward.Z);

        return clipW < 0.001f ? clipY * 1000f : clipY / clipW;
    }
}

/// <summary>A level or view screen-fade range, as modelinfo keeps it at <c>+0x70</c> / <c>+0x7c</c> (B432).</summary>
/// <param name="Minimum">Width in pixels at and below which the prop is gone; 0 or less disables the fade.</param>
/// <param name="Maximum">Width at and above which it is opaque.</param>
/// <param name="Scale">255 over the band.</param>
/// <remarks>The default value is a disabled range, which is what the view fade is (<c>r_screenfademinsize 0</c>).</remarks>
public readonly record struct ScreenFadeRange(float Minimum, float Maximum, float Scale)
{
    private const float Opaque = 255f;

    /// <summary>`engine.dll` <c>FUN_1801c9e50</c>, the range setter.</summary>
    /// <param name="minimum">The minimum width.</param>
    /// <param name="maximum">The maximum width.</param>
    /// <returns><c>{min, max, 255/(max−min)}</c>, or <c>{min, min, 255}</c> when <c>max ≤ min</c>.</returns>
    public static ScreenFadeRange Set(float minimum, float maximum) =>
        maximum <= minimum
            ? new ScreenFadeRange(minimum, minimum, Opaque)
            : new ScreenFadeRange(minimum, maximum, Opaque / (maximum - minimum));

    /// <summary>Whether <see cref="Alpha"/> would read the width at all — its early return, inverted.</summary>
    /// <param name="forcedScale">The prop's forced fade scale.</param>
    /// <returns>False when the answer is 255 whatever the width.</returns>
    public bool Measures(float forcedScale) => Minimum > 0f && forcedScale > 0f;

    /// <summary>`engine.dll` <c>FUN_1801cb810</c>.</summary>
    /// <param name="pixels">The prop's projected width.</param>
    /// <param name="forcedScale">Its forced fade scale, which divides the width.</param>
    /// <returns>0 to 255.</returns>
    public byte Alpha(float pixels, float forcedScale)
    {
        if (!Measures(forcedScale))
        {
            return (byte)Opaque;
        }

        float width = pixels / forcedScale;

        if (width <= Minimum)
        {
            return 0;
        }

        if (Maximum < 0f || Maximum <= width)
        {
            return (byte)Opaque;
        }

        return (byte)Math.Clamp((int)((width - Minimum) * Scale), 0, (int)Opaque);
    }
}
