using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>Triggerable soundscapes against what the SERVER wrote into a recording player's audio params (B483).</summary>
/// <remarks>
/// **A differential: the server's own answer is in the demo.** A point-of-view recording carries the recorder's
/// <c>m_audio</c> (<c>DT_LocalPlayerExclusive</c>), which is what the server's trigger touches and radius contest wrote.
/// Replaying the recorder's eye through the viewer's production path must name the same entity at every sample —
/// <c>entIndex</c> is <c>m_soundscapeEntityId</c>, the 1-based position in the list every <c>CEnvSoundscape</c> joins
/// at construction (<c>soundscape_system.cpp:281-287</c>), and 0 when the last trigger was left
/// (<c>soundscape.cpp:457</c>).
///
/// The specimen was recorded 2026-10-05 through the tf2 MCP on koth_lakeside_final, whose ambience is all triggerables:
/// red spawn (Inside, entity 6), <c>setpos</c> into the Wood trigger (entity 3), into the open with no trigger
/// (entity 0), and back to spawn.
/// </remarks>
public sealed class TriggerSoundscapeCorpusTests
{
    /// <summary>The recorder's class is soldier: `getpos` minus the dumped origin, measured while recording.</summary>
    private const float SoldierViewHeight = 68f;

    [Test]
    public void Touch_TheRecordersEyeOnKothLakeside_NamesTheEntityTheServerWrote()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("tf2-2026-pov-koth_lakeside-triggers"));

        byte[] bytes = File.ReadAllBytes(GameInstall.RequireFile("maps/koth_lakeside_final.bsp"));
        SoundscapeCatalog catalog = SoundscapeCatalog.ForLevel(
            PakFile.ReadFrom(bytes), GameArchives.Open(GameInstall.Require()).Read, "koth_lakeside_final");
        BspLeafTree leaves = BspLeafTree.Read(bytes);
        SoundscapePlacements placements = SoundscapePlacements.From(
            BspEntities.ReadFrom(bytes), catalog, leaves, BspModels.Read(bytes));

        SoundscapeTouches touches = new();
        SoundscapePlacement? current = null;
        int sample = 0;
        HashSet<int> seen = [];

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick && sample < timeline.Soundscapes.Count; tick++)
        {
            if (timeline.RecordedViewAt(tick) is not { } view)
            {
                continue;
            }

            // The recorded view origin is the player's ORIGIN — `soundscape_dumpclient` printed the same z as it while
            // `getpos` printed 68 more, the soldier's view offset — so the ear is lifted by that measured 68.
            (float X, float Y, float Z) eye = (view.ViewOrigin.X, view.ViewOrigin.Y, view.ViewOrigin.Z + SoldierViewHeight);

            current = placements.Touch(touches, trigger => SoundscapeSystem.Touches(leaves, trigger, eye), current);
            current = placements.Choose(
                eye.X,
                eye.Y,
                eye.Z,
                (from, to) => leaves.Trace(
                    from.X, from.Y, from.Z, to.X, to.Y, to.Z, 0f, 0, SoundscapeSystem.LineOfSightMask).Fraction >= 1f,
                current,
                leaves.ClusterAt(eye.X, eye.Y, eye.Z),
                BspVisibility.Read(bytes));

            while (sample < timeline.Soundscapes.Count && timeline.Soundscapes[sample].Tick <= tick)
            {
                (int at, SceneSoundscape written) = timeline.Soundscapes[sample++];

                (current is { } placed ? placed.Id + 1 : 0).ShouldBe(
                    written.EntityIndex, $"tick {at}, eye {eye}: the entity the server wrote");

                seen.Add(written.EntityIndex);
            }
        }

        sample.ShouldBe(timeline.Soundscapes.Count, "every sample the recording carries was compared");
        seen.ShouldBe([6, 3, 0], ignoreOrder: true, "the control: spawn, the Wood trigger and no trigger were all met");
    }
}
