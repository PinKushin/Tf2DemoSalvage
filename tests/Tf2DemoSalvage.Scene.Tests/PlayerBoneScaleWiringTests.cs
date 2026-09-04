using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The three per-bone scales survive the hop from the timeline to the drawn prop (B312).
/// </summary>
/// <remarks>
/// **`PlayerProps.Add` builds a player's pose FIELD BY FIELD**, so a value with no assignment there
/// is one the renderer never sees however well the timeline decoded it. That is not hypothetical:
/// `docs/memory/a-moves-regressions-are-wiring.md` records three fields shipping lost through this
/// exact method with the suite at 620 of 620 green, and B259 was a fourth — `ClientSideAnimated`
/// had no parameter at all, so every player was animated on the wrong clock.
///
/// **The unit tests of `PlayerBoneScales` cannot see this.** They call the arithmetic directly and
/// pass whatever scale they choose; nothing in them touches the path that decides what scale the
/// renderer is given. Only a test that goes in one end and reads the other can fail when the hop is
/// missing, which is why `docs/memory/output-level-assertion-or-it-is-not-done.md` exists.
///
/// **These would pass at a scale of 1 whatever the wiring did**, since 1 is the default at every
/// hop — so the fixture states values that are not 1 and not each other. Three distinct numbers is
/// what separates "carried" from "carried into the wrong field", which one shared value could not.
/// </remarks>
public sealed class PlayerBoneScaleWiringTests
{
    [Test]
    public void Add_ForAPlayerWithBoneScales_CarriesAllThreeToTheProp()
    {
        List<SceneProp> drawn = [];

        PlayerProps.Add(
            [Scaled()],
            drawn,
            new StubAppearance(),
            (_, _, body) => body);

        drawn.Count.ShouldBe(1, "the player reached the draw list at all");

        drawn[0].Pose.HeadScale.ShouldBe(1.5f, "m_flHeadScale, not the default");
        drawn[0].Pose.TorsoScale.ShouldBe(0.5f, "m_flTorsoScale, and not the head's value");
        drawn[0].Pose.HandScale.ShouldBe(2f, "m_flHandScale, and not either of the others");
    }

    /// <remarks>
    /// **The control: a player who states nothing draws at 1.** `C_TFPlayer` initialises all three
    /// to 1 (`c_tf_player.cpp:577`), so a demo that never sends them is one where TF2 would also
    /// have used 1 — the default is the engine's answer rather than a fallback for missing data,
    /// and a wiring that defaulted to 0 would collapse the model instead.
    /// </remarks>
    [Test]
    public void Add_ForAPlayerWithoutThem_LeavesAllThreeAtOne()
    {
        List<SceneProp> drawn = [];

        PlayerProps.Add(
            [Plain()],
            drawn,
            new StubAppearance(),
            (_, _, body) => body);

        drawn.Count.ShouldBe(1);

        drawn[0].Pose.HeadScale.ShouldBe(1f);
        drawn[0].Pose.TorsoScale.ShouldBe(1f);
        drawn[0].Pose.HandScale.ShouldBe(1f);
    }

    /// <summary>A scout with three DIFFERENT bone scales, so a swap between them is visible.</summary>
    private static ScenePlayer Scaled() =>
        Plain() with { HeadScale = 1.5f, TorsoScale = 0.5f, HandScale = 2f };

    /// <summary>An ordinary scout, stating none of the three.</summary>
    private static ScenePlayer Plain() =>
        new(
            2,
            0f,
            0f,
            0f,
            SceneTeams.Red,
            125,
            1);
}
