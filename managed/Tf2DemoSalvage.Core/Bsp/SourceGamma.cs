using System;

namespace Tf2DemoSalvage.Core.Bsp;

/// <summary>
/// The curve between Source's stored light and what a screen should show.
/// </summary>
/// <remarks>
/// **One definition, because two would drift.** Source stores baked light in linear space —
/// lightmap samples as <c>ColorRGBExp32</c>, static prop vertex colours as bytes written from the
/// same representation — and a renderer targeting sRGB has to take both through the same curve. A
/// second copy of it is a second thing to get wrong, and the way it goes wrong is subtle: the two
/// lighting paths disagree and one class of surface looks darker than everything around it.
///
/// **Measured, not assumed.** On cp_process_final, prop vertex colours average 0.23 raw against the
/// world's gamma-corrected lightmaps at 0.47. Taking the props through this curve gives 0.50 —
/// agreement to within five percent across two independent distributions, which is what identified
/// the missing step. Drawn without it, every prop on the map reads as a black blob, which is the
/// oldest open defect in this project.
/// </remarks>
public static class SourceGamma
{
    /// <summary>The exponent Source's own tools use for the screen.</summary>
    private const float ScreenGamma = 2.2f;

    /// <summary>Converts one linear channel, already normalised, to display space.</summary>
    /// <param name="linear">Linear light, where one is full brightness.</param>
    /// <returns>The value to send to an sRGB target, from zero to one.</returns>
    public static float ToDisplay(float linear) =>
        MathF.Pow(Math.Clamp(linear, 0f, 1f), 1f / ScreenGamma);

    /// <summary>Converts one linear channel to a display-space byte.</summary>
    /// <param name="linear">Linear light, where 255 is full brightness.</param>
    /// <returns>The byte to send to an sRGB target.</returns>
    public static byte ToDisplayByte(float linear) =>
        (byte)Math.Clamp(ToDisplay(linear / 255f) * 255f, 0f, 255f);
}
