using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Scene claims that were corpus-only because they needed several players at once.
/// </summary>
/// <remarks>
/// **Three tests converted from the corpus suite on 2026-08-19, and each is stronger for it.** All
/// three had the same shape: a real recording was searched for a situation, and the assertion was
/// a heuristic over whatever turned up — "more than one distinct skin", "more than ten distinct X
/// coordinates", "both tables appear somewhere". Those are the assertions you write when the input
/// is found rather than chosen.
///
/// A written demo supplies the situation, so the assertion becomes the value. What is lost is the
/// claim that real recordings contain the case — and that claim was a guard against the corpus
/// drifting, not evidence about this code. See <c>docs/CORPUS-AUDIT.md</c>.
/// </remarks>
public sealed class SyntheticSceneTests
{
    [Test]
    public void Build_TwoPlayersWithDifferentSkins_KeepBothIntoTheirPoses()
    {
        // **A regression test, and the bug it guards is worth restating.** This set was {0} for
        // every demo ever parsed, because m_nSkin was filtered out before the pose was built — so
        // every player wore RED whatever team they were on.
        //
        // The corpus version asserted "more than one distinct skin somewhere in a SourceTV
        // recording of cp_foundry", which depends on that recording containing both teams. Here
        // both skins are put in deliberately and asserted by value.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(
            SyntheticPlayer.OriginTable.NonLocal,
            tick: 66,
            (1, Player(x: 0f, y: 0f, team: SceneTeams.Red, skin: 0)),
            (2, Player(x: 128f, y: 0f, team: SceneTeams.Blu, skin: 1))));

        HashSet<int> skins =
        [
            // **PlayerTracks, not Props.** The corpus version read Props and worked, because a real
            // recording is full of non-player entities carrying skins; a demo holding only players
            // has an empty Props list, so the same assertion came back with nothing to check. The
            // two lists are separate and a player is in exactly one of them.
            //
            // Keyframes rather than interpolated samples: a skin is discrete, so an interpolated
            // pose could only report a value some keyframe already held.
            .. timeline.PlayerTracks.SelectMany(track => track.Keyframes)
                .Select(keyframe => keyframe.Pose.Skin),
        ];

        // RED and BLU are families 0 and 1 of the same model, so these are the meaningful values
        // rather than merely two different ones.
        skins.ShouldContain(0);
        skins.ShouldContain(1);
    }

    [Test]
    public void Build_ThreePlayersAtChosenPositions_AreNotCollapsedToOne()
    {
        // **The control against a decoder that returns a constant**, which is what the corpus
        // version existed for. It asserted "more than ten distinct X values, spanning more than a
        // hundred units" — a heuristic sized to how spread out real players happen to be, and one
        // that a decoder returning two alternating constants would pass.
        //
        // Three chosen positions make it exact. A constant fails, an axis swap fails, and a scale
        // error fails, none of which a spread heuristic distinguishes.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(
            SyntheticPlayer.OriginTable.NonLocal,
            tick: 66,
            (1, Player(x: 512f, y: -1024f, team: SceneTeams.Red, skin: 0)),
            (2, Player(x: -256f, y: 2048f, team: SceneTeams.Blu, skin: 1)),

            // A gap in the slots, because entity indices are delta-coded: the encoder writes the
            // distance to the next occupied slot, so 1, 2, 5 exercises a gap that 1, 2, 3 does not.
            (5, Player(x: 0f, y: 0f, team: SceneTeams.Red, skin: 0))));

        IReadOnlyList<ScenePlayer> players = timeline.PlayersAt(66);
        players.Count.ShouldBe(3);

        players.Select(player => (player.X, player.Y))
            .OrderBy(position => position.X)
            .ShouldBe([(-256f, 2048f), (0f, 0f), (512f, -1024f)]);
    }

    [Test]
    public void Build_EitherExclusiveTable_ResolvesAPosition()
    {
        // **Both branches of the origin resolver, asserted directly.**
        //
        // The corpus version asserted that both tables turn up somewhere across the demos, which
        // is a fact about the corpus. It began life as something sharper and false: that a
        // point-of-view demo resolves through the local table and a SourceTV recording through the
        // non-local one. The corpus killed that — the 2013 SourceTV demo is 21 non-local against 2
        // local, and a modern demos.tf SourceTV recording came back 12 local and 0 non-local. Any
        // reader branching on POV-versus-SourceTV is wrong on some era.
        //
        // What survives is that neither branch is dead code, and a written demo states it per
        // table instead of hoping the corpus holds one of each.
        foreach (SyntheticPlayer.OriginTable table in new[]
        {
            SyntheticPlayer.OriginTable.NonLocal,
            SyntheticPlayer.OriginTable.Local,
        })
        {
            DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(
                table,
                tick: 66,
                (1, Player(x: 640f, y: -320f, team: SceneTeams.Blu, skin: 1))));

            ScenePlayer player = timeline.PlayersAt(66).ShouldHaveSingleItem();

            player.X.ShouldBe(640f, 0.5f, table.ToString());
            player.Y.ShouldBe(-320f, 0.5f, table.ToString());
        }
    }

    /// <summary>One player's properties, positioned and identifiable.</summary>
    private static Dictionary<string, PropertyValue> Player(
        float x, float y, int team, int skin) => new()
        {
            ["m_vecOrigin"] = PropertyValue.FromVectorXY(x, y),
            ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
            ["m_iTeamNum"] = PropertyValue.FromInt(team),
            ["m_lifeState"] = PropertyValue.FromInt(0),
            ["m_nSkin"] = PropertyValue.FromInt(skin),
        };
}
