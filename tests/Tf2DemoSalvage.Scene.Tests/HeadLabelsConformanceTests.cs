using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CVoiceStatus::DrawHeadLabels` (voice_status.cpp:205): a white quad over each speaking player's head (B549).</summary>
/// <remarks>
/// Player 3 is RED, standing at (100, 0, 0) — `WorldSpaceCenter` is the hull's middle, 41 up — so its label is centred at
/// z = 41 + 35 = 76, eight either side. Player 5 is BLU. The view looks down +X: right is −Y.
/// </remarks>
public sealed class HeadLabelsConformanceTests
{
    private static readonly Vector3 Right = new(0f, -1f, 0f);

    [Test]
    public void Build_ASpeakingPlayer_IsAQuad35AboveTheHullCentreAndEightEitherSide()
    {
        IReadOnlyList<ParticleBatch> batches = Build(Speaking(3), Right);

        DetailSpriteVertex[] corners = [.. batches.ShouldHaveSingleItem().Corners];
        corners.Length.ShouldBe(6, "one quad, as two triangles");
        corners.Select(c => (c.X, c.Y, c.Z)).Distinct().OrderBy(c => c.Z).ThenBy(c => c.Y).ShouldBe(
        [
            (100f, -8f, 68f), (100f, 8f, 68f), (100f, -8f, 84f), (100f, 8f, 84f),
        ]);
    }

    /// <summary>`GetHeadLabelMaterial` (c_tf_player.cpp:9532): RED's material on RED, BLU's on anything else.</summary>
    [TestCase(3, HeadLabels.RedMaterial)]
    [TestCase(5, HeadLabels.BlueMaterial)]
    public void Build_ASpeaker_IsDrawnWithTheirTeamsMaterial(int speaker, string material)
    {
        IReadOnlyList<ParticleBatch> batches = Build(Speaking(speaker), Right);

        (batches.ShouldHaveSingleItem().Material.Sheet?.MappingWidth ?? -1).ShouldBe(Materials[material].Width, "each team's test material is its own size");
    }

    /// <summary>Not drawn for a player not speaking, one `IsPlayerDead`, or one not in the entity list (dormant).</summary>
    [Test]
    public void Build_SilentDeadOrAbsent_DrawsNothing()
    {
        Build(Speaking(), Right).ShouldBeEmpty();
        Build(Speaking(3), Right, deadFlag: true).ShouldBeEmpty();
        Build(Speaking(9), Right).ShouldBeEmpty();
    }

    /// <summary>`fabs( vRight.z ) &gt; 0.95` (:245): "don't draw it edge-on"; under it, the right is flattened and renormalised.</summary>
    [Test]
    public void Build_AViewRightNearlyVertical_DrawsNothing_AndATiltedOneIsFlattened()
    {
        Build(Speaking(3), Vector3.Normalize(new Vector3(0f, -0.2f, 0.98f))).ShouldBeEmpty();

        DetailSpriteVertex[] corners = [.. Build(Speaking(3), Vector3.Normalize(new Vector3(0f, -1f, 0.5f))).Single().Corners];
        corners.Select(c => c.Y).Distinct().Order().ToArray().ShouldBe([-8f, 8f], "the right flattened to −Y and renormalised");
    }

    /// <summary>
    /// `C_TFGameRules::ShouldDrawHeadLabels` (tf_gamerules.cpp:3670): tournament mode off a match server — every demo — never;
    /// otherwise `mp_show_voice_icons`.
    /// </summary>
    [TestCase(false, true, true)]
    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    public void ShouldDraw_TournamentAndShowVoiceIcons_IsTheGamerulesAnswer(bool tournament, bool showVoiceIcons, bool expected) =>
        HeadLabels.ShouldDraw(tournament, showVoiceIcons).ShouldBe(expected);

    /// <summary>A ducked player's hull is 62 tall, so its centre is 31 up (`PlayerHull`).</summary>
    [Test]
    public void Build_ADuckedSpeaker_IsCentredOnTheDuckedHull()
    {
        DetailSpriteVertex[] corners = [.. Build(Speaking(3), Right, flags: PlayerHull.DuckingFlag).Single().Corners];

        corners.Select(c => c.Z).Distinct().Order().ToArray().ShouldBe([58f, 74f]);
    }

    private static readonly Dictionary<string, EngineSprite> Materials = new()
    {
        [HeadLabels.RedMaterial] = Sprite(64),
        [HeadLabels.BlueMaterial] = Sprite(32),
    };

    /// <summary>`effects/speech_voice_*`: UnlitGeneric, `$translucent`, `$vertexcolor`, `$vertexalpha` (tf2_misc_dir.vpk).</summary>
    private static EngineSprite Sprite(int size) => new(
        new ParticleMaterial(new MapTexture(size, size, size, size, TextureImage.None, IsTransparent: true), [], SpriteBlend.Translucent),
        size,
        size,
        SpriteOrientation.Parallel,
        SpriteExtents.Of(size, size, origin: null),
        (1f, 1f, 1f, 1f),
        IgnoresVertexColors: false,
        Shader: "UnlitGeneric",
        Modulation: (1f, 1f, 1f, 1f),
        VertexColour: true,
        VertexAlpha: true);

    private static HashSet<int> Speaking(params int[] speakers) => [.. speakers];

    private static IReadOnlyList<ParticleBatch> Build(IReadOnlySet<int> speaking, Vector3 right, bool deadFlag = false, int flags = 0)
    {
        ScenePlayer[] players =
        [
            new(3, 100f, 0f, 0f, Team: 2, Health: 125, PlayerClass: 1, Flags: flags) { DeadFlag = deadFlag },
            new(5, 300f, 0f, 0f, Team: 3, Health: 125, PlayerClass: 1),
        ];

        return new HeadLabels().Build(speaking, players, right, Materials);
    }
}
