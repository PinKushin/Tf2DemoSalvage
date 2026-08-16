using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// That a real demo's skins survive all the way into the pose the renderer reads.
/// </summary>
/// <remarks>
/// **The companion to `RetainedPropertyTests`, and it covers a different failure.** That test locks
/// the whitelist; this one exercises the whole path — decode, retain, build the pose — against a
/// real demo. Either alone would have missed this defect: the field was absent from the whitelist
/// AND unread at the construction site, and fixing one without the other leaves skins at zero.
///
/// **Measured before it was asserted.** The 2013 SourceTV recording of cp_foundry carries skins 0,
/// 1 and 2 in roughly a 38 / 5 / 2 split, and `z1800.dem` 42 / 15 / 1. Those are exactly the three
/// values `team_control_point.cpp:569` produces — 0 for RED, 1 for BLU, 2 for unowned — so the
/// non-zero ones are real team colouring, not noise.
///
/// **Why a bare "some skin is non-zero" is the right assertion here** rather than a specific
/// entity's specific value: the defect made EVERY skin zero, structurally, for every demo. One
/// non-zero skin anywhere falsifies that, and pinning an entity index instead would make the test
/// about which entity happened to occupy a slot.
/// </remarks>
public sealed class CorpusEntitySkinTests
{
    [Test]
    public void ARealDemoCarriesMoreThanOneSkinIntoItsPoses()
    {
        string path = Corpus.Demo("stv-cp_foundry");

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        HashSet<int> skins =
        [
            // Keyframes rather than interpolated samples: a skin is discrete, so an interpolated
            // pose could only ever report a value some keyframe already held.
            .. timeline.Props
                .SelectMany(track => track.Keyframes)
                .Select(keyframe => keyframe.Pose.Skin),
        ];

        // More than one distinct value is the falsifiable claim. Before the fix this set was {0} for
        // every demo ever parsed, because the property was filtered out before the pose was built.
        skins.Count.ShouldBeGreaterThan(1);

        // And the specific values, since they are meaningful rather than arbitrary: RED and BLU are
        // families 0 and 1 of the same model.
        skins.ShouldContain(0);
        skins.ShouldContain(1);
    }
}
