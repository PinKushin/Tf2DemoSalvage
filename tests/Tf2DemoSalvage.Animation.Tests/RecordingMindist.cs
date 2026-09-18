using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A mindist of its own kind, whose slots 7 and 8 record that they were called instead of doing a plain mindist's work — the
/// control that a caller dispatches through the mindist's own table, as the engine's callers do, rather than doing the plain
/// mindist's work itself (B369).
/// </summary>
internal sealed class RecordingMindist() : IvpMindist(
    new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
    new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
    extraRadius: 0f)
{
    /// <summary>Which slots were called, in order: <c>freeze</c> or <c>collide</c>.</summary>
    public List<string> Slots { get; } = [];

    /// <summary>The manager slot 7 was last handed.</summary>
    public IvpMindistManager? FrozenBy { get; private set; }

    /// <summary>The queue slot 7 was last handed.</summary>
    public IvpMinList<IIvpTimeEvent>? FrozenQueue { get; private set; }

    /// <inheritdoc/>
    public override void Freeze(IvpMindistManager manager, IvpMinList<IIvpTimeEvent> queue)
    {
        Slots.Add("freeze");
        FrozenBy = manager;
        FrozenQueue = queue;
    }

    /// <inheritdoc/>
    public override void Collide(Action<IvpMindist> impact) => Slots.Add("collide");
}
