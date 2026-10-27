using System.Diagnostics;
using System.Globalization;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// `--measure`'s sun line: <see cref="LevelLighting.SunWork"/> differenced across WHOLE frames, every caller included, and
/// its mean per frame over the rate line's window.
/// </summary>
/// <remarks>
/// **Why the whole frame**: B594's census differences `SunCalls` across the two world `Instances` calls only, and the sun is
/// also asked by a static prop's first state, the viewmodel pass and an impact's colour — none inside that window. The
/// counts and ticks are the lighting's own, carried from inside `SunFrom` (B243); this only subtracts readings. **A new
/// map is a new counter**, so a change of lighting restarts the reading instead of subtracting across two maps.
/// </remarks>
public sealed class SunMeter
{
    private LevelLighting? _of;
    private SunWork _last;
    private SunWork _window;
    private int _frames;

    /// <summary>Takes one frame's work: the ticks go to <paramref name="parts"/> as `sun`, the counts to the window.</summary>
    /// <param name="lighting">The level being drawn, or null.</param>
    /// <param name="parts">The frame's parts, or null when not measuring.</param>
    public void Frame(LevelLighting? lighting, FrameParts? parts)
    {
        if (!ReferenceEquals(lighting, _of))
        {
            _of = lighting;
            _last = lighting?.SunWork ?? default;
            return;
        }

        if (lighting is null)
        {
            return;
        }

        SunWork now = lighting.SunWork;
        SunWork frame = now.Since(_last);

        _last = now;
        parts?.Add("sun", frame.Ticks);
        _window = new SunWork(
            _window.Calls + frame.Calls, _window.LightCache + frame.LightCache, _window.StaticProp + frame.StaticProp,
            _window.Direct + frame.Direct, _window.Ticks + frame.Ticks);
        _frames++;
    }

    /// <summary>The window's means per frame, and starts a new window; empty with no frame.</summary>
    /// <returns>`sun per frame N calls (cache, static props, direct) in M ms`, or empty.</returns>
    public string Report()
    {
        if (_frames == 0)
        {
            return string.Empty;
        }

        double frames = _frames;
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"sun per frame {_window.Calls / frames:0.##} calls (light cache {_window.LightCache / frames:0.##}" +
            $", static props {_window.StaticProp / frames:0.##}, direct {_window.Direct / frames:0.##})" +
            $" in {_window.Ticks * 1000d / Stopwatch.Frequency / frames:0.###} ms over {_frames} frames");

        _window = default;
        _frames = 0;

        return line;
    }
}
