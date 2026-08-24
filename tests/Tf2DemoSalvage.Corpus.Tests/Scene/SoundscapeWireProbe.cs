using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Which demos carry the soundscape, which depends on how they were recorded.
/// </summary>
/// <remarks>
/// **The SDK says a SourceTV recording cannot carry it, and this asks the demos instead.** `m_audio`
/// lives in `DT_Local`, which reaches the wire through
/// `SendPropDataTable( "localdata", 0, DT_LocalPlayerExclusive, SendProxy_SendLocalDataTable )`
/// (<c>player.cpp:8199</c>), and that proxy is one line:
///
/// <code>
/// void* SendProxy_SendLocalDataTable( ... ) { pRecipients->SetOnly( objectID - 1 ); ... }
/// </code>
///
/// One recipient — the player who owns the entity. A point-of-view recording is made BY that player
/// and should carry theirs; SourceTV owns no player and should carry nobody's.
///
/// **This decides the whole shape of B173**, so it is measured. If POV demos carry it and STV demos
/// do not, the wire suffices for one and the map's own `env_soundscape` entities are needed for the
/// other — which is the owner's instinct: *"checking the stv demo will probably have everything, but
/// it might not, the bsp is guaranteed to show every sound file the map uses"*.
///
/// A probe, because the useful output is a table of what each kind carries rather than a verdict.
/// </remarks>
[Explicit("Reports whether demos carry soundscape audio params; run deliberately.")]
public sealed class SoundscapeWireProbe
{
    [TestCase("movement-test-pov-cp_process", "POV")]
    [TestCase("movement-test-stv-cp_process", "STV")]
    [TestCase("demostf-cp_process_f12-2026-08-08-2207", "STV")]
    public void Soundscape_WhatEachDemoKindCarries_IsReported(string name, string kind)
    {
        string path = Corpus.Demo(name);

        DemoTimeline timeline = TimelineCache.For(path);

        TestContext.Out.WriteLine(
            $"{Path.GetFileName(path)} [{kind}]: " +
            $"{timeline.Soundscapes.Count.ToString(CultureInfo.InvariantCulture)} soundscape samples");

        foreach ((int tick, SceneSoundscape soundscape) in timeline.Soundscapes.Take(10))
        {
            int used = Enumerable.Range(0, 8).Count(soundscape.HasPosition);

            int carried = soundscape.Positions.Count(slot => slot is not null);

            TestContext.Out.WriteLine(
                $"    tick {tick.ToString(CultureInfo.InvariantCulture)}: " +
                $"index {soundscape.Index.ToString(CultureInfo.InvariantCulture)}, " +
                $"entity {soundscape.EntityIndex.ToString(CultureInfo.InvariantCulture)}, " +
                $"bits {soundscape.PositionBits.ToString(CultureInfo.InvariantCulture)}, " +
                $"{used.ToString(CultureInfo.InvariantCulture)} marked used, " +
                $"{carried.ToString(CultureInfo.InvariantCulture)} vectors present");
        }

        // The distinct indices a recording visits — on cp_process the owner measured exactly three
        // in the live client (0 respawn, 41 Gorge.Outside, 42 Gorge.Inside), so a POV recording of
        // that map should visit a subset of those and nothing else.
        List<int> distinct =
        [
            .. timeline.Soundscapes.Select(sample => sample.Soundscape.Index).Distinct().Order(),
        ];

        TestContext.Out.WriteLine(
            $"    distinct indices: {(distinct.Count == 0 ? "none" : string.Join(", ", distinct))}");
    }
}
