using System;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// The post-effects `CWin32Font::GetCharRGBA` (vguimatsurface.dll 0x1800181e0) applies to a rasterised glyph, in the order
/// it applies them: dropshadow, outline, blur, scanlines, rotary.
/// </summary>
/// <remarks>
/// **Closed code, read from the disassembly and ported arithmetic for arithmetic** — `tf2vguimatsurface`. Each works in
/// place on RGBA bytes, row-major, red first. Valve's quirks are kept because they are what TF2 draws: the blur's window
/// at the far edge of each axis never reaches the centre, and its sum is truncated to a byte without clamping.
/// </remarks>
public static class VguiFontEffects
{
    /// <summary>0x18001c700: each transparent pixel becomes black, carrying the alpha of the pixel <paramref name="offset"/> up and left.</summary>
    /// <param name="wide">Width.</param>
    /// <param name="tall">Height.</param>
    /// <param name="rgba">The glyph.</param>
    /// <param name="offset">The shadow's offset; 0 does nothing.</param>
    public static void DropShadow(int wide, int tall, byte[] rgba, int offset)
    {
        ArgumentNullException.ThrowIfNull(rgba);

        if (offset == 0)
        {
            return;
        }

        // Bottom-right first, so every source is read before anything could have written it.
        for (int y = tall - 1; y >= offset; y--)
        {
            for (int x = wide - 1; x >= offset; x--)
            {
                int at = ((y * wide) + x) * 4;

                if (rgba[at + 3] == 0)
                {
                    rgba[at] = 0;
                    rgba[at + 1] = 0;
                    rgba[at + 2] = 0;
                    rgba[at + 3] = rgba[((((y - offset) * wide) + (x - offset)) * 4) + 3];
                }
            }
        }
    }

    /// <summary>0x18001d000: a transparent pixel with any neighbour within <paramref name="radius"/> whose four channels are all non-zero becomes opaque black.</summary>
    /// <param name="wide">Width.</param>
    /// <param name="tall">Height.</param>
    /// <param name="rgba">The glyph.</param>
    /// <param name="radius">The outline's reach; 0 does nothing.</param>
    public static void Outline(int wide, int tall, byte[] rgba, int radius)
    {
        ArgumentNullException.ThrowIfNull(rgba);

        if (radius == 0)
        {
            return;
        }

        for (int y = 0; y < tall; y++)
        {
            for (int x = 0; x < wide; x++)
            {
                int at = ((y * wide) + x) * 4;

                if (rgba[at + 3] == 0 && HasInkedNeighbour(wide, tall, rgba, x, y, radius))
                {
                    rgba[at] = 0;
                    rgba[at + 1] = 0;
                    rgba[at + 2] = 0;
                    rgba[at + 3] = 255;
                }
            }
        }
    }

    private static bool HasInkedNeighbour(int wide, int tall, byte[] rgba, int x, int y, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int nx = x + dx;
                int ny = y + dy;

                if ((dx == 0 && dy == 0) || nx < 0 || nx >= wide || ny < 0 || ny >= tall)
                {
                    continue;
                }

                int at = ((ny * wide) + nx) * 4;

                // A black outline pixel already written has zero RGB, so it never seeds another.
                if (rgba[at] != 0 && rgba[at + 1] != 0 && rgba[at + 2] != 0 && rgba[at + 3] != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>0x18001d190: every row whose index is not a multiple of <paramref name="lines"/> has its colour scaled by 0.7.</summary>
    /// <param name="wide">Width.</param>
    /// <param name="tall">Height.</param>
    /// <param name="rgba">The glyph.</param>
    /// <param name="lines">The spacing; 1 or less does nothing.</param>
    public static void Scanlines(int wide, int tall, byte[] rgba, int lines)
    {
        ArgumentNullException.ThrowIfNull(rgba);

        if (lines <= 1)
        {
            return;
        }

        for (int y = 0; y < tall; y++)
        {
            if ((uint)y % (uint)lines == 0)
            {
                continue;
            }

            for (int x = 0; x < wide; x++)
            {
                int at = ((y * wide) + x) * 4;

                rgba[at] = (byte)(int)(rgba[at] * 0.7f);
                rgba[at + 1] = (byte)(int)(rgba[at + 1] * 0.7f);
                rgba[at + 2] = (byte)(int)(rgba[at + 2] * 0.7f);
            }
        }
    }

    /// <summary>0x18001d140: the row at <c>(int)( tall × 0.5 )</c> becomes opaque grey, 0x7f.</summary>
    /// <param name="wide">Width.</param>
    /// <param name="tall">Height.</param>
    /// <param name="rgba">The glyph.</param>
    public static void Rotary(int wide, int tall, byte[] rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);

        int row = (int)(tall * 0.5);

        for (int x = 0; x < wide; x++)
        {
            int at = ((row * wide) + x) * 4;

            rgba[at] = 0x7f;
            rgba[at + 1] = 0x7f;
            rgba[at + 2] = 0x7f;
            rgba[at + 3] = 0xff;
        }
    }

    /// <summary>0x18001c7b0: a separable gaussian — across each row, then down each column — with RGB made white wherever alpha survives.</summary>
    /// <param name="wide">Width.</param>
    /// <param name="tall">Height.</param>
    /// <param name="rgba">The glyph.</param>
    /// <param name="blur">The radius; 0 does nothing.</param>
    /// <remarks>
    /// Weights: σ = 0.683 · blur, <c>(float)( 2.7^( −v² / 2σ² ) · (float)( 1 / √( σ · 6.28 · σ ) ) )</c> — Valve's own
    /// 2.7 and 6.28, not e and 2π. The window near the start of an axis is clipped; near the end it is
    /// <c>off − x + size</c> long, so it never reaches the centre (0x18001caa4). The engine's non-additive path is the only
    /// one `GetCharRGBA` takes.
    /// </remarks>
    public static void GaussianBlur(int wide, int tall, byte[] rgba, int blur)
    {
        ArgumentNullException.ThrowIfNull(rgba);

        if (blur == 0)
        {
            return;
        }

        int taps = (blur * 2) + 1;
        float[] weights = new float[taps];
        double sigma = blur * 0.683;
        float scale = (float)(1.0 / Math.Sqrt(sigma * 6.28 * sigma));

        for (int tap = 0; tap < taps; tap++)
        {
            int value = tap - blur;

            weights[tap] = (float)(Math.Pow(2.7, -(value * value) * (1.0 / ((sigma + sigma) * sigma))) * scale);
        }

        Pass(wide, tall, rgba, blur, weights, horizontal: true);
        Pass(wide, tall, rgba, blur, weights, horizontal: false);
    }

    private static void Pass(int wide, int tall, byte[] rgba, int blur, float[] weights, bool horizontal)
    {
        byte[] source = (byte[])rgba.Clone();
        int length = horizontal ? wide : tall;
        int across = horizontal ? tall : wide;

        for (int along = 0; along < length; along++)
        {
            int offset = along < blur ? blur - along : 0;
            int count = weights.Length - offset;

            if (along >= length - blur)
            {
                count = offset - along + length;
            }

            int first = along - blur + offset;

            for (int line = 0; line < across; line++)
            {
                float sum = 0f;

                for (int tap = 0; tap < count; tap++)
                {
                    int sample = first + tap;
                    int at = horizontal ? ((line * wide) + sample) * 4 : ((sample * wide) + line) * 4;

                    sum += source[at + 3] * weights[offset + tap];
                }

                int to = horizontal ? ((line * wide) + along) * 4 : ((along * wide) + line) * 4;
                byte alpha = (byte)(int)sum;
                byte white = alpha != 0 ? (byte)255 : (byte)0;

                rgba[to + 3] = alpha;
                rgba[to] = white;
                rgba[to + 1] = white;
                rgba[to + 2] = white;
            }
        }
    }
}
