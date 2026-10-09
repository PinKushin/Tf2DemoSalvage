using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>`m_bFlipViewModels` reaches the first-person scene from a real demo (B515).</summary>
/// <remarks>
/// **The synthetic tests cannot fail if the property's name or table is wrong** — `Integer(...)` would return null,
/// every owner would read right-handed, and every other test would still pass. So this asserts on the scene a
/// SourceTV viewer builds, and it asserts BOTH answers: gummo plays left-handed in `cp_process_f12` (TF2 draws his
/// viewmodel on the left, `docs/findings/76-golden/b515-45000-viewmodel.png`), and a reader stuck on either value
/// fails one half.
/// </remarks>
public sealed class ViewmodelFlipCorpusTests
{
    [Test]
    public void Build_OnF12_FlipsTheViewmodelOfALeftHandedPlayerAndNotTheOthers()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("cp_process_f12"));
        TimelineViewmodels source = new(timeline);

        HashSet<bool> owners = [];
        HashSet<bool> drawn = [];

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 2000)
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                if (source.MainHandAt(tick, player.EntityIndex) is not { OwnerFlipsViewModels: { } flips })
                {
                    continue;
                }

                owners.Add(flips);

                // Through the scene, as production builds it: the arms prop carries the same answer.
                ViewmodelSceneResult scene = new ViewmodelScene().Build(
                    source, tick, player.EntityIndex, new ViewmodelPlacement(0f, 0f, 0f, 0f, 0f, 0f), null, null);

                drawn.UnionWith(scene.Props.Select(prop => prop.FlipViewModel));
            }
        }

        owners.ShouldBe([true, false], ignoreOrder: true, "f12 has a left-handed player (gummo) and right-handed ones");
        drawn.ShouldBe([true, false], ignoreOrder: true, "the scene must draw both hands, as the owners' flags say");
    }
}
