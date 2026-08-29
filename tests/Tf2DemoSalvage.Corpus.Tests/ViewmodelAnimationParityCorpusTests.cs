using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// <c>m_nAnimationParity</c> reaches the scene from a real demo, and changes there.
/// </summary>
/// <remarks>
/// **The unit tests cannot fail if the field never arrives, and that is the whole risk here.**
/// <c>ViewmodelAnimationParityConformanceTests</c> proves <see cref="ViewmodelAnimation.RestartAt"/>
/// computes Valve's rule; it says nothing about whether production ever calls it, or with what. If
/// the property name or its table were wrong, <c>Integer(...)</c> returns null, parity is zero for
/// every tick, no animation ever restarts — and every other test in the repository still passes.
///
/// That failure has shipped here three times in one session, each with a green suite: a dump
/// annotation matching <c>int</c> when the field arrives as a <c>byte</c>, a kill feed resolving
/// numbers through a renderer that returns strings, and <c>m_flPlaybackRate</c> decoded, retained,
/// unit-tested and read by nothing.
///
/// **So this asserts on the decoded scene rather than on the decoder**, and it asserts CHANGE rather
/// than presence: a counter stuck at any single value is indistinguishable from one that was never
/// read, and only a changing one proves the field is live.
///
/// `cp_process_f12` because it is the owner's parity reference demo — six players firing weapons for
/// several minutes, so a viewmodel animation restarting is not a rare event in it.
/// </remarks>
public sealed class ViewmodelAnimationParityCorpusTests
{
    [Test]
    public void ViewmodelAt_AcrossAMatch_ReportsAParityThatChanges()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("cp_process_f12"));

        HashSet<int> parities = [];
        HashSet<int> starts = [];

        int sampled = 0;

        // Every player present, because which one the recording followed is not this test's
        // business — sampling one is how a test ends up measuring a spectator who never fires.
        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 8)
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                if (timeline.ViewmodelAt(tick, player.EntityIndex) is not { } viewmodel)
                {
                    continue;
                }

                sampled++;
                parities.Add(viewmodel.AnimationParity);
                starts.Add(viewmodel.AnimationStartTick);
            }
        }

        // The control: without viewmodels at all the assertions below are vacuously interesting.
        sampled.ShouldBeGreaterThan(0, "the demo should carry viewmodels to sample");

        parities.Count.ShouldBeGreaterThan(
            1,
            $"m_nAnimationParity never changed across {sampled} samples — it is either not being " +
            $"decoded (wrong property name or table, which reads as a constant zero) or the demo " +
            $"carries no weapon animations at all. Values seen: " +
            $"{string.Join(", ", parities.OrderBy(each => each))}");

        starts.Count.ShouldBeGreaterThan(
            1,
            "the animation start tick never moved, so no viewmodel animation ever restarted; " +
            "the parity is arriving but ViewmodelAnimation.RestartAt is not being reached");
    }
}
