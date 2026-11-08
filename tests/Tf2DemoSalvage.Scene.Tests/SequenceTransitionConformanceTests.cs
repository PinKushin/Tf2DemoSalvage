using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// When an outgoing sequence's fade is measured FROM: the last time it was current, not the frame that saw the change (B712).
/// </summary>
/// <remarks>
/// **`CheckForSequenceChange` pushes the tail entry as it stands** (`sequence_Transitioner.cpp:36-65`): its
/// `m_flLayerAnimtime` and `m_flCycle` are what `UpdateCurrent` last wrote while it WAS the current sequence
/// (`sequence_Transitioner.cpp:94-97`, `flCurTime` = `gpGlobals->curtime`, `c_baseanimating.cpp:1843`). Nothing restamps
/// it at the change. `GetFadeout( flCurTime )` is then `1 - (flCurTime - m_flLayerAnimtime) / m_flLayerFadeOuttime`,
/// splined (`animationlayer.h:84-108`), and an entry at or below zero is removed (`sequence_Transitioner.cpp:103-113`);
/// the fading cycle runs on by `curtime - m_flLayerAnimtime` (`c_baseanimating.cpp:1852-1853`).
///
/// **So a change seen across a gap longer than the window fades nothing.** A seek's landing is such a gap: the last
/// frame posed before it was a skip frame drawn at a batch's tick, and which batch that was depends on the frame's
/// wall-clock length (B708). Stamping the fade at the landing instead held a full-weight copy of whatever that frame
/// drew, so two launches of one `--shot` drew one scout mid-jump and the other running (B712).
/// </remarks>
public sealed class SequenceTransitionConformanceTests
{
    /// <remarks>Fixture window 0.2 s (`AnimatedStudioBytes.FadeSeconds`): last current at 0, seen changed at 0.5, `s = 1 - 0.5/0.2 &lt; 0`.</remarks>
    [Test]
    public void Instances_AChangeSeenAfterAGapLongerThanTheWindow_FadesNothing()
    {
        EntityModelSet models = new() { Geometry = _ => SequenceTransitionTests.Frames() };
        List<SceneProp> drawn = [SequenceTransitionTests.Playing(sequence: 0)];

        models.Add(drawn, _ => SequenceTransitionTests.Frames());
        models.Instances(drawn, [], seconds: 0d);

        drawn[0] = SequenceTransitionTests.Playing(sequence: 1);
        models.Instances(drawn, [], seconds: 0.5d);

        models.LayersOf(4).ShouldNotBeNull().ShouldBeEmpty(
            "the outgoing sequence was last current 0.5 s ago, past its 0.2 s window, so the engine removes it at once");
    }

    /// <remarks>
    /// Current at 0 and again at 0.1, seen changed at 0.15. `s = 1 - (0.15 - 0.1) / 0.2 = 0.75`, splined
    /// `3s² - 2s³ = 0.84375`. The cycle runs on from 0.1 (one cycle a second) by 0.05 to 0.15: frame 4.5 of 30.
    /// Stamped at the change instead, the weight is 1 and the cycle 0.1, frame 3.
    /// </remarks>
    [Test]
    public void Instances_AChangeInsideTheWindow_FadesFromWhenTheOldSequenceWasLastCurrent()
    {
        EntityModelSet models = new() { Geometry = _ => SequenceTransitionTests.Frames() };
        List<SceneProp> drawn = [SequenceTransitionTests.Playing(sequence: 0)];

        models.Add(drawn, _ => SequenceTransitionTests.Frames());
        models.Instances(drawn, [], seconds: 0d);
        models.Instances(drawn, [], seconds: 0.1d);

        drawn[0] = SequenceTransitionTests.Playing(sequence: 1);
        models.Instances(drawn, [], seconds: 0.15d);

        PoseLayer fading = models.LayersOf(4).ShouldNotBeNull().ShouldHaveSingleItem();

        fading.Sequence.ShouldBe(0);
        fading.Weight.ShouldBe(0.84375f, 1e-5f);
        fading.Frame.ShouldBe(4);
        fading.FrameFraction.ShouldBe(0.5f, 1e-3f);
    }
}
