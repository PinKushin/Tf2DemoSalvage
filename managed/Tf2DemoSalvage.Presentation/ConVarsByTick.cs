using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// The client ConVars as each demo tick of one frame reads them (B839) [no engine counterpart: the engine reads each tick's
/// packets straight after that tick's console run, where the viewer hands a frame's game events to the HUD at its end].
/// </summary>
/// <remarks>
/// `_Host_RunFrame` (`engine.dll` build 1007 FUN_1801a5eb0) runs, for each host tick, `Cbuf_Execute` in the input step and
/// then the client step, whose `CL_ReadPackets` fires that tick's game events and user messages (FUN_1801a5860). So an
/// event of tick T hears the ConVars as the run before T left them, and not what a later tick's run set. Each set a run
/// makes inside the frame is kept with the value it replaced; a tick before that run reads the replaced value.
/// </remarks>
public sealed class ConVarsByTick
{
    /// <summary>Each set the frame's runs made, in order: the first tick that reads it, the ConVar, the value it replaced.</summary>
    private readonly List<(int FirstTick, string Name, string? Before)> _sets = [];

    /// <summary>The first demo tick the console run now running is read by, or null when no tick's run is running.</summary>
    private int? _running;

    /// <summary>Starts a frame: the last frame's sets are all in force now.</summary>
    public void BeginFrame() => _sets.Clear();

    /// <summary>
    /// Runs one host tick's `Cbuf_Execute`, made while the clock is on <paramref name="tick"/>: the client step after it
    /// reads the next tick's packets, so the ticks from that one on read its sets.
    /// </summary>
    /// <param name="tick">The tick the clock is on when the run is made.</param>
    /// <param name="run">The run, handed the tick; a cached delegate, so a played tick allocates nothing (B839 review 1, Gemini).</param>
    public void BeforeTick(int tick, Action<int> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        _running = tick + 1;

        try
        {
            run(tick);
        }
        finally
        {
            _running = null;
        }
    }

    /// <summary>Notes a set, if a tick's run made it; a set made anywhere else is in force for every tick of the frame.</summary>
    /// <param name="name">The ConVar.</param>
    /// <param name="before">The value it replaced, or null for its default.</param>
    public void Set(string name, string? before)
    {
        if (_running is { } firstTick)
        {
            _sets.Add((firstTick, name, before));
        }
    }

    /// <summary>The ConVars as a demo tick reads them, or null where it reads them as they are now.</summary>
    /// <param name="tick">The demo tick.</param>
    /// <param name="now">The values now.</param>
    /// <returns>A lookup, or null when no set this frame came after the tick.</returns>
    public Func<string, string?>? At(int tick, Func<string, string?> now)
    {
        foreach ((int firstTick, _, _) in _sets)
        {
            if (firstTick > tick)
            {
                return name => ValueAt(tick, name, now);
            }
        }

        return null;
    }

    /// <summary>The value a tick reads: the one the first later set replaced, else the value now.</summary>
    private string? ValueAt(int tick, string name, Func<string, string?> now)
    {
        foreach ((int firstTick, string set, string? before) in _sets)
        {
            if (firstTick > tick && string.Equals(set, name, StringComparison.OrdinalIgnoreCase))
            {
                return before;
            }
        }

        return now(name);
    }
}
