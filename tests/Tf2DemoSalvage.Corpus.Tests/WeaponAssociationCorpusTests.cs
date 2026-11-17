using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>A real Botkiller wearable names its weapon by handle, and that weapon is not drawn while holstered (B770).</summary>
/// <remarks>
/// **Only real bytes can say this**: that the live server sends `m_hWeaponAssociatedWith` on a world wearable at all, and
/// that a holstered weapon reaches no prop list, which is why the timeline dereferences against every entity. At tick
/// 20000 the demoman (4) holds his grenade launcher (358); sticky launcher 360 (serial 783) is holstered, and its
/// Botkiller 364 names it. `WeaponWearableVisibility` then drops 364 by the active-weapon test, which the scene tests pin.
/// </remarks>
public sealed class WeaponAssociationCorpusTests
{
    private const string Match = "demostf-cp_process_f12-2026-08-08-2207";

    [Test]
    public void PropsAt_ABotkillerWhoseLauncherIsHolstered_NamesTheLauncherThePropsLack()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(Match));
        List<SceneProp> props = [];
        timeline.PropsAt(20000, props);

        ScenePlayer demoman = timeline.PlayersAt(20000).Single(player => player.EntityIndex == 4);

        (props.Single(prop => prop.EntityIndex == 364).WeaponAssociatedWith, demoman.ActiveWeapon, props.Exists(prop => prop.EntityIndex == 360))
            .ShouldBe(((360, 783), 358, false));
    }
}
