using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A real stopwatch team swap draws every player in his new team's skin on the tick it arrives (B65).
/// </summary>
/// <remarks>
/// **The output-level assertion** (<c>docs/memory/output-level-assertion-or-it-is-not-done.md</c>): the skin
/// <see cref="Disguise.VisibleSkin"/> hands the renderer, from the timeline's players. `GetSkin` switches on
/// `GetTeamNumber()` (c_tf_player.cpp:7795), the entity's own `m_iTeamNum`. On `demostf-pl_upward_f12-1491354` the
/// halves swap at tick 45768: every player's entity says so then, and the resource's `m_iTeam` copy follows six ticks
/// later at 45774 (one `ResourceThink`, player_resource.cpp:100). Reading the resource first drew all eighteen in the
/// old team's colour for those six ticks — the B65 symptom, on every player at once. lcor; skips without it.
/// </remarks>
public sealed class CorpusTeamSkinTests
{
    private const string StopwatchDemo = "demostf-pl_upward_f12-1491354";

    /// <summary>The tick the players' own team numbers swap.</summary>
    private const int SwapTick = 45768;

    /// <summary>Past the resource's catch-up at 45774, so both sources agree.</summary>
    private const int SettledTick = 45800;

    [Test]
    public void VisibleSkin_OnTheTickATeamSwapArrives_IsTheNewTeams()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(StopwatchDemo));

        ScenePlayer[] swapped = [.. timeline.PlayersAt(SwapTick)
            .Where(player => player.Team is SceneTeams.Red or SceneTeams.Blu)
            .OrderBy(player => player.EntityIndex)];

        ScenePlayer[] settled = [.. timeline.PlayersAt(SettledTick)
            .Where(player => player.Team is SceneTeams.Red or SceneTeams.Blu)
            .OrderBy(player => player.EntityIndex)];

        // The control: the swap is real, so the tick before it shows each player on the OTHER team.
        ScenePlayer[] before = [.. timeline.PlayersAt(SwapTick - 1)
            .Where(player => player.Team is SceneTeams.Red or SceneTeams.Blu)
            .OrderBy(player => player.EntityIndex)];

        settled.Length.ShouldBe(18, "a 9v9 Highlander match");
        before.Select(player => player.Team).ShouldBe(settled.Select(player => (int?)SceneTeams.Opposite(player.Team)));

        swapped.Select(player => Disguise.VisibleSkin(player))
            .ShouldBe(settled.Select(player => PlayerSkin.ForTeam(player.Team)));
    }
}
