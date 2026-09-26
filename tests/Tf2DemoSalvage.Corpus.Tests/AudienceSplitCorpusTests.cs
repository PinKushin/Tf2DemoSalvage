using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// The local/shared weapon split, measured on a POV and an STV recording of the SAME session.
/// </summary>
/// <remarks>
/// `SendProxy_SendLocalWeaponDataTable` (basecombatweapon_shared.cpp:2739) sends `m_iClip1` to the
/// owner alone. The prediction was that SourceTV, never the owner, would lack it. **It does not**: the
/// 2011 STV file carries the owner's clip on every sampled tick, so the SourceTV client is handed the
/// owner-only table too (finding 64). Only real bytes can show what the writer does, which is why
/// this is a corpus test (D38).
/// </remarks>
public sealed class AudienceSplitCorpusTests
{
    [Test]
    public void WeaponClip1_PovAndStvOfOneSession_ArriveInBoth()
    {
        DemoTimeline pov = TimelineCache.For(Corpus.Demo("tf2-2011-build4604-pov-koth_viaduct"));
        DemoTimeline stv = TimelineCache.For(Corpus.Demo("tf2-2011-build4604-stv-koth_viaduct"));

        Clipped(pov).ShouldBeGreaterThan(0, "the recorder's own clip is in the local table, sent to the recorder");
        Clipped(stv).ShouldBeGreaterThan(0, "SourceTV is handed the owner-only local table as well");

        // The control: SourceTV's own player holds no weapon, so it must carry no clip — a reader that
        // filled every player would pass the two lines above.
        stv.PlayersAt((stv.FirstTick + stv.LastTick) / 2)
            .Where(player => player.Team == SceneTeams.Spectator)
            .ShouldAllBe(player => player.WeaponClip1 == null);
    }

    /// <summary>Player samples carrying a clip, across ticks spread over the demo.</summary>
    private static int Clipped(DemoTimeline timeline)
    {
        int step = System.Math.Max(1, (timeline.LastTick - timeline.FirstTick) / 50);

        return Enumerable.Range(0, 51)
            .Select(sample => timeline.FirstTick + (sample * step))
            .Sum(tick => timeline.PlayersAt(tick).Count(player => player.WeaponClip1 is not null));
    }
}
