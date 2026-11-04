using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Where a skip has got to.</summary>
internal enum SkipPhase
{
    /// <summary>The frames `demo_gototick` renders on its way (<see cref="DemoSkip"/>).</summary>
    Frames,

    /// <summary>The model decals' replay of the ticks before the target, which is ours.</summary>
    Decals,

    /// <summary>Everything is done, and the next step shows the tick.</summary>
    Landing,
}

/// <summary>A skip under way across frames (B708): where its reading has got to, what it was asked for, what it has made.</summary>
/// <remarks>The state `ReplaySkip` held in locals when it ran in one call. `CDemoPlayer` holds the same: `m_nSkipToTick` and a packet count.</remarks>
/// <param name="timeline">The demo being walked.</param>
/// <param name="from">Where the skip started from, or null for a skip back, which reads from the demo's start.</param>
/// <param name="first">The first tick it reads.</param>
/// <param name="target">The tick it was asked for.</param>
/// <param name="tick">The moment to show on landing, which may fall between ticks.</param>
internal sealed class SkipWalk(DemoTimeline timeline, int? from, int first, int target, double tick)
{
    private int _cursor = first;

    public DemoTimeline Timeline { get; } = timeline;

    /// <summary>Where this leg of the walk started, or null when it reads from the demo's start.</summary>
    public int? From { get; private set; } = from;

    public int Target { get; private set; } = target;

    public double Tick { get; set; } = tick;

    public SkipPhase Phase { get; set; }

    /// <summary>The last tick shown on the way, or null before any.</summary>
    public int? Shown { get; set; }

    /// <summary>Whether its frames are read at all: with no sound scripts there is nothing they could deal.</summary>
    public bool ReadsFrames { get; init; } = true;

    public List<SkipFrame> Frames { get; } = [];

    /// <summary>The read loops run so far, the frames' and the empty one that ends them.</summary>
    public int Batches { get; set; }

    /// <summary>The frames drawn while it ran.</summary>
    public int FramesDrawn { get; set; }

    public SkipCounts Counts { get; private set; } = new();

    /// <summary>The next batch's last tick, or null when the batch that reaches the target is next: that is the landing's.</summary>
    /// <returns>The tick, every one before the target.</returns>
    public int? NextThrough()
    {
        if (!ReadsFrames)
        {
            return null;
        }

        int through = _cursor + DemoSkip.PacketsPerFrame - 1;

        if (through >= Target)
        {
            return null;
        }

        _cursor = through + 1;

        return through;
    }

    /// <summary>Goes on to a new target, from where it is, or from the demo's start for one behind it.</summary>
    /// <param name="target">The new tick.</param>
    /// <param name="start">The demo's first tick.</param>
    /// <remarks>`SkipToTick` (FUN_180073b10) reloads for a target behind the one it has reached, and otherwise just moves the target.</remarks>
    public void Retarget(int target, int start)
    {
        int reached = Phase == SkipPhase.Frames ? _cursor : Target + 1;

        if (target < reached)
        {
            _cursor = start;
            From = null;
        }
        else
        {
            _cursor = reached;
            From = reached - 1;
        }

        Target = target;
        Phase = SkipPhase.Frames;
        Frames.Clear();
        Counts = new SkipCounts();
    }
}

/// <summary>What a skip's report line says, added up over its frames.</summary>
internal sealed class SkipCounts
{
    public long Began { get; } = System.Diagnostics.Stopwatch.GetTimestamp();

    public int Heard { get; set; }

    public int Crossed { get; set; }

    /// <summary>Every event the frames crossed, the instrument's control: none at all would mean no walk, not no sound.</summary>
    public int Walked { get; set; }

    public int Steps { get; set; }

    /// <summary>What the frames made, in order — every HUD sound and every crossed event — so two builds compare exactly.</summary>
    public ulong Events { get; set; } = SequenceHash.Empty;

    public long HudTicks { get; set; }

    public long BuildTicks { get; set; }

    public long PoseTicks { get; set; }

    public long SampleTicks { get; set; }

    public long DrawListTicks { get; set; }

    public long ModelTicks { get; set; }

    public long SimulateTicks { get; set; }

    public long LookbackTicks { get; set; }

    public long BuildingTicks { get; set; }

    public long PosingTicks { get; set; }

    public long DecalBegan { get; set; }

    public int DecalStart { get; set; }

    public (long Simulate, long Setup, long Skin) DecalWas { get; set; }

    public bool Hud { get; set; }
}
