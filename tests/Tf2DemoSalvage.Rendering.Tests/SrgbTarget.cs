using System;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The curve between a shader's linear output and the byte <see cref="OffscreenTarget.PixelAt"/> reads: the target's
/// view is <c>B8G8R8A8_UNORM_SRGB</c>, as the window's back buffer is (B476).
/// </summary>
internal static class SrgbTarget
{
    /// <summary>The stored byte for a linear value, as the hardware encodes it on write.</summary>
    /// <param name="linear">The shader's output, 0 to 1.</param>
    /// <returns>The byte.</returns>
    public static int Encode(double linear)
    {
        double clamped = Math.Clamp(linear, 0.0, 1.0);
        double encoded = clamped <= 0.0031308 ? clamped * 12.92 : (1.055 * Math.Pow(clamped, 1 / 2.4)) - 0.055;

        return (int)Math.Round(255.0 * encoded);
    }

    /// <summary>The linear value a stored byte decodes to.</summary>
    /// <param name="stored">The byte.</param>
    /// <returns>The linear value.</returns>
    public static double Decode(int stored)
    {
        double c = stored / 255.0;

        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
