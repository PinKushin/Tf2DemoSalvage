using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>`d_lightstylevalue`: each light style's current brightness, animated from its pattern (B415, world lighting).</summary>
/// <remarks>
/// **Read from `engine.dll` `0x1800d3ec0`, `R_AnimateLight`.** For each of the 64 styles it reads the pattern the
/// `lightstyles` string table holds; an empty one gives 256, and otherwise the value is
/// `( pattern[ (int)( time · 10 ) % length ] − 'a' ) · 22` — `'m'` is 264, the value a lightmap is stored at, so a scale
/// of one. A value that changed stamps the style, which is what sends its faces' lightmaps to be rebuilt.
/// </remarks>
public sealed class LightStyleValues
{
    /// <summary>How many styles the engine animates, `MAX_LIGHTSTYLES`.</summary>
    public const int Count = 64;

    /// <summary>The value a lightmap is stored at: `'m'`.</summary>
    private const float Stored = 264f;

    private readonly string[] _patterns = new string[Count];
    private readonly int[] _values = new int[Count];
    private readonly List<int> _changed = [];
    private bool _started;

    /// <summary>A style's pattern, as the table last set it.</summary>
    /// <param name="style">The style.</param>
    /// <param name="pattern">Letters from 'a' to 'z', or empty.</param>
    public void Set(int style, string pattern)
    {
        if (style is >= 0 and < Count)
        {
            _patterns[style] = pattern ?? string.Empty;
        }
    }

    /// <summary>Every style's value at a time, and which changed since the last call.</summary>
    /// <param name="seconds">The client's time.</param>
    /// <returns>The styles whose values changed; every one on the first call.</returns>
    public IReadOnlyList<int> Advance(double seconds)
    {
        _changed.Clear();

        int step = (int)((float)seconds * 10f);

        for (int style = 0; style < Count; style++)
        {
            string pattern = _patterns[style] ?? string.Empty;
            int value = pattern.Length == 0 ? 256 : (pattern[step % pattern.Length] - 'a') * 22;

            if (!_started || value != _values[style])
            {
                _values[style] = value;
                _changed.Add(style);
            }
        }

        _started = true;

        return _changed;
    }

    /// <summary>A style's value, `d_lightstylevalue`.</summary>
    /// <param name="style">The style.</param>
    /// <returns>The value; 264 is as stored.</returns>
    public int Value(int style) => style is >= 0 and < Count ? _values[style] : 0;

    /// <summary>What a style's lightmap is multiplied by: its value over 264.</summary>
    /// <param name="style">The style.</param>
    /// <returns>The scale.</returns>
    public float Scale(int style) => Value(style) / Stored;
}
