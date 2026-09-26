using System;
using System.Globalization;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>Where a `.res` file puts a panel and how big it makes it, as `vgui_controls/Panel.cpp` computes it.</summary>
/// <remarks>
/// **Ported, not approximated, because this arithmetic IS the customisation.** Every HUD positions its panels with these
/// strings — `r10`, `c-50+s0.5`, `f0`, `p0.25` — and a HUD that works in TF2 must land in the same pixels here.
///
/// `ComputePos` (:8863): an optional `r`/`c` alignment, then an optional `s`/`p` fraction of the panel's own size or its
/// parent's; `atoi` of the rest is scaled when the panel is proportional; a trailing `+`/`-` recurses with that operator.
/// `ComputeWide` (:8698) / `ComputeTall`: `f` fills the parent less the value, `p` fills it less the scaled `atoi` and then
/// multiplies by the `atof`, `s` multiplies the panel's current size. C's `atoi`/`atof` and float-to-int truncation are kept.
///
/// **The proportional scale is passed in** — `scheme()->GetProportionalScaledValueEx` is in the closed `vgui2.dll`.
/// **Not built yet:** the `o` form (one axis from the other), which needs the scheme's inverse scale as well.
/// </remarks>
public static class PanelLayout
{
    /// <summary>`ISurface::GetProportionalBase`'s height: `vguimatsurface.dll` stores 640 by 480.</summary>
    private const double ProportionalBaseTall = 480.0;

    /// <summary>`GetProportionalScaledValue`: a value authored at 480 tall, at a real one.</summary>
    /// <param name="value">The value as a `.res` file writes it.</param>
    /// <param name="tall">The screen's (or the scheme's sizing panel's) height in pixels.</param>
    /// <returns>`(int)((double)tall / 480.0 * value)` — `vgui2.dll` 0x18000d590, truncating toward zero.</returns>
    public static int ProportionalScaled(int value, int tall) => (int)(tall / ProportionalBaseTall * value);

    /// <summary>`ComputePos`, with `OP_SET`: a panel's x or y.</summary>
    /// <param name="input">The `xpos` or `ypos` string, or null when the file names none.</param>
    /// <param name="current">Where the panel already is, kept when there is no string.</param>
    /// <param name="size">The panel's own size along this axis, for `s`.</param>
    /// <param name="parentSize">The parent's (or screen's) size along this axis.</param>
    /// <param name="proportional">Whether the panel is proportional.</param>
    /// <param name="scale">`GetProportionalScaledValueEx`.</param>
    /// <returns>The position.</returns>
    public static int Position(string? input, int current, int size, int parentSize, bool proportional, Func<int, int> scale)
    {
        ArgumentNullException.ThrowIfNull(scale);

        int position = current;

        Compute(input, ref position, size, parentSize, proportional, scale, Operation.Set);

        return position;
    }

    /// <summary>`ComputeWide` or `ComputeTall`, for every form but `o`.</summary>
    /// <param name="input">The `wide` or `tall` string, or null when the file names none.</param>
    /// <param name="current">The panel's current size, kept when there is no string.</param>
    /// <param name="parentSize">The parent's (or screen's) size along this axis.</param>
    /// <param name="proportional">Whether the panel is proportional.</param>
    /// <param name="scale">`GetProportionalScaledValueEx`.</param>
    /// <returns>The size.</returns>
    public static int Size(string? input, int current, int parentSize, bool proportional, Func<int, int> scale)
    {
        ArgumentNullException.ThrowIfNull(scale);

        if (input is null)
        {
            return current;
        }

        ReadOnlySpan<char> text = input;
        bool full = false;
        bool ofParent = false;
        bool ofSelf = false;

        if (text.Length > 0 && char.ToLowerInvariant(text[0]) == 'f')
        {
            full = true;
            text = text[1..];
        }
        else if (text.Length > 0 && char.ToLowerInvariant(text[0]) == 'p')
        {
            ofParent = true;
            text = text[1..];
        }
        else if (text.Length > 0 && char.ToLowerInvariant(text[0]) == 's')
        {
            ofSelf = true;
            text = text[1..];
        }

        float fraction = Atof(text);
        int value = Atoi(text);

        if (ofParent)
        {
            return (int)((parentSize - scale(value)) * fraction);
        }

        if (ofSelf)
        {
            return (int)(current * fraction);
        }

        if (proportional)
        {
            value = scale(value);
        }

        return full ? parentSize - value : value;
    }

    private enum Operation
    {
        Set,
        Add,
        Subtract,
    }

    private static void Compute(
        string? input, ref int position, int size, int parentSize, bool proportional, Func<int, int> scale, Operation operation)
    {
        if (input is null)
        {
            return;
        }

        ReadOnlySpan<char> text = input;
        bool right = false;
        bool centre = false;
        bool ofSelf = false;
        bool ofParent = false;

        if (text.Length > 0 && char.ToLowerInvariant(text[0]) == 'r')
        {
            right = true;
            text = text[1..];
        }
        else if (text.Length > 0 && char.ToLowerInvariant(text[0]) == 'c')
        {
            centre = true;
            text = text[1..];
        }

        if (text.Length > 0 && char.ToLowerInvariant(text[0]) == 's')
        {
            ofSelf = true;
            text = text[1..];
        }
        else if (text.Length > 0 && char.ToLowerInvariant(text[0]) == 'p')
        {
            ofParent = true;
            text = text[1..];
        }

        int value = Atoi(text);
        float fraction = Atof(text);

        if (proportional)
        {
            value = scale(value);
        }

        int delta;

        if (ofSelf)
        {
            delta = (int)(size * fraction);
        }
        else if (ofParent)
        {
            delta = (int)(parentSize * fraction);
        }
        else
        {
            delta = value;
        }

        int placed;

        if (right)
        {
            placed = parentSize - delta;
        }
        else if (centre)
        {
            placed = (parentSize / 2) + delta;
        }
        else
        {
            placed = delta;
        }

        position = operation switch
        {
            Operation.Add => position + placed,
            Operation.Subtract => position - placed,
            _ => placed,
        };

        // Past the sign and the number, then recurse on an operator.
        if (text.Length > 0 && text[0] is '-' or '+')
        {
            text = text[1..];
        }

        while (text.Length > 0 && (char.IsAsciiDigit(text[0]) || text[0] == '.'))
        {
            text = text[1..];
        }

        if (text.Length > 0 && text[0] is '+' or '-')
        {
            Compute(
                text[1..].ToString(),
                ref position,
                size,
                parentSize,
                proportional,
                scale,
                text[0] == '+' ? Operation.Add : Operation.Subtract);
        }
    }

    /// <summary>C's `atoi`: leading space, a sign, then digits; anything else stops it.</summary>
    internal static int Atoi(ReadOnlySpan<char> text)
    {
        text = text.TrimStart();
        int length = 0;

        if (length < text.Length && text[length] is '-' or '+')
        {
            length++;
        }

        while (length < text.Length && char.IsAsciiDigit(text[length]))
        {
            length++;
        }

        return int.TryParse(text[..length], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }

    /// <summary>`sscanf( text, "%d %d ..." )`: reads up to <c>values.Length</c> integers, writing only those it reads.</summary>
    /// <returns>How many were read.</returns>
    internal static int ScanInts(ReadOnlySpan<char> text, Span<int> values)
    {
        int count = 0;

        while (count < values.Length)
        {
            // The format's literal space matches any run of white space, including none.
            text = text.TrimStart();

            int length = 0;

            if (length < text.Length && text[length] is '-' or '+')
            {
                length++;
            }

            int digits = length;

            while (length < text.Length && char.IsAsciiDigit(text[length]))
            {
                length++;
            }

            if (length == digits)
            {
                break;
            }

            values[count++] = Atoi(text[..length]);
            text = text[length..];
        }

        return count;
    }

    /// <summary>C's `atof`: the longest prefix that reads as a decimal number, else zero.</summary>
    internal static float Atof(ReadOnlySpan<char> text)
    {
        text = text.TrimStart();

        for (int length = text.Length; length > 0; length--)
        {
            if (float.TryParse(
                    text[..length],
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture,
                    out float value))
            {
                return value;
            }
        }

        return 0f;
    }
}
