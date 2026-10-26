using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.SdkReference;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, as `CorpusItemAnimSlotTests` beside it is.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// On f12, once its appearance is built and the weapon scripts are precached, asking every face its voices and every
/// player's weapon its muzzle flash over the whole recording reads no file on this thread (B595 part 6).
/// </summary>
/// <remarks>
/// **The output-level half of the two precaches.** The unit tests prove each precache answers as the lazy read on the
/// values they chose; this walks a real match through `DemoAppearance.Ensure` — the call the viewer makes at load — and
/// counts what `GameArchives.ThreadReadObserver` sees afterwards. Before the fix the viewer's `--measure` counted 36 such
/// reads a minute on f12 (`docs/measurements/2026-10-25-b595-part6-main-thread-reads-in-play.md`).
/// </remarks>
public sealed class CorpusMainThreadReadsInPlayTests
{
    [Test]
    public void FacesAndMuzzleFlashes_AfterLoadPrecache_ReadNoFileInPlay()
    {
        string path = Corpus.Demo("demostf-cp_process_f12-2026-08-08");
        string root = GameInstall.Require();
        DemoTimeline timeline = TimelineCache.For(path);
        GameContent game = GameContent.Open(root, NullLoggerFactory.Instance);

        IPlayerAppearance appearance = DemoAppearance.Ensure(DemoAppearance.None, timeline, game, NullLogger.Instance);
        FaceSources faces = appearance.Faces.ShouldNotBeNull();
        WeaponMuzzleFlashes muzzles = new(game.Archives.Read, game.Weapons.Items);
        muzzles.Precache();

        List<string> reads = [];
        int voices = 0;
        int flashes = 0;
        GameArchives.ThreadReadObserver = (read, _) => reads.Add(read);

        try
        {
            foreach (TimelineFrame frame in timeline.Frames)
            {
                double seconds = frame.Tick * (double)timeline.IntervalPerTick;

                foreach (ScenePlayer player in frame.Players)
                {
                    voices += faces.Voices(player.EntityIndex, seconds).Count;

                    if (muzzles.For(player.WeaponItem, player.Team ?? 0).Particle is not null)
                    {
                        flashes++;
                    }
                }
            }
        }
        finally
        {
            GameArchives.ThreadReadObserver = null;
        }

        // The controls: a sweep that asked for no voice and no flash would read nothing while measuring nothing.
        // `Voices` returns only lines with a sentence, and an MP3 has none, so the control is the recording's own VO MP3s —
        // the lengths the lazy read fetched in play (28 a minute, the measurement above).
        TestContext.Out.WriteLine($"voices answered {voices}, flashes {flashes}");
        timeline.Sounds.Count(sound => sound.Name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && !sound.IsStop)
            .ShouldBeGreaterThan(0, "f12 starts VO MP3 lines, whose lengths a face reads");
        flashes.ShouldBeGreaterThan(0, "f12's players hold weapons with muzzle flashes");
        reads.Distinct(StringComparer.OrdinalIgnoreCase).ShouldBeEmpty();
    }
}
