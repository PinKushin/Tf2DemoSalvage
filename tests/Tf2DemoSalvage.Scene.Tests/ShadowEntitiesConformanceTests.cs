using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The drawn props as the client shadow manager reads them: <c>C_BaseEntity::ShadowCastType</c>
/// (`c_baseentity.cpp:1502-1509`) and <c>C_TFPlayer</c>'s overrides (`c_tf_player.cpp:9255-9303`) (B702, finding 107).
/// </summary>
/// <remarks>Every prop's render bounds are (−10, −10, 0)–(10, 10, 72) here, so a change is the gather's.</remarks>
public sealed class ShadowEntitiesConformanceTests
{
    private static readonly (Vector3 Min, Vector3 Max) Bounds = (new Vector3(-10f, -10f, 0f), new Vector3(10f, 10f, 72f));

    private static SceneProp Prop(int index, SceneModelKind kind = SceneModelKind.Studio, int? attachedTo = null, int? firstTick = null) =>
        new(index, "models/x.mdl", kind, new ScenePose { X = 1f, Y = 2f, Z = 3f, Yaw = 90f }, AttachedTo: attachedTo, FirstTick: firstTick);

    private static List<ShadowEntity> Gather(IReadOnlyList<SceneProp> drawn, params int[] players)
    {
        List<ShadowEntity> into = [];
        new ShadowEntities().Gather(drawn, players.Contains, _ => Bounds, into);
        return into;
    }

    /// <remarks>A player is dynamic and its bounds grow 36 in x and y only; anything else studio is render-to-texture.</remarks>
    [Test]
    public void Gather_APlayerAndAProp_AreDynamicFattenedAndRenderToTexture()
    {
        List<ShadowEntity> entities = Gather([Prop(1), Prop(40)], 1);

        entities[0].ShouldBe(new ShadowEntity(
            1, null, ShadowCast.Dynamic, new Vector3(1f, 2f, 3f), (0f, 90f, 0f), new Vector3(-46f, -46f, 0f), new Vector3(46f, 46f, 72f)));
        entities[1].Cast.ShouldBe(ShadowCast.RenderToTexture);
        (entities[1].Mins, entities[1].Maxs).ShouldBe(Bounds);
    }

    /// <remarks>A brush or sprite model casts none (<c>mod_studio</c> only), nor — a filed gap — a corpse.</remarks>
    [Test]
    public void Gather_ABrushASpriteAndACorpse_CastNothing() =>
        Gather([Prop(5, SceneModelKind.Brush), Prop(6, SceneModelKind.Sprite), Prop(7, firstTick: 100)])
            .ShouldAllBe(entity => entity.Cast == ShadowCast.None);

    /// <remarks>A hat keeps its wearer as its shadow parent; one whose wearer is not drawn has no origin, so it is left out.</remarks>
    [Test]
    public void Gather_AHatOnADrawnAndOnAnUndrawnWearer_KeepsTheFirstAndDropsTheSecond()
    {
        List<ShadowEntity> entities = Gather([Prop(1), Prop(40, attachedTo: 1), Prop(41, attachedTo: 2)], 1);

        entities.Select(entity => entity.EntityIndex).ShouldBe([1, 40]);
        entities[1].ShadowParent.ShouldBe(1);
    }
}
