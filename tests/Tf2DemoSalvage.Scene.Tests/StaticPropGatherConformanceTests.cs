using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A static prop is filed with the leaf system once and reached only through the in-view gather (finding 83 item 1).
/// </summary>
/// <remarks>
/// **What the engine does, from published source.** <c>CClientLeafSystem::AddRenderable</c> takes a static prop with
/// <c>RENDER_FLAGS_STATIC_PROP</c> at level load (<c>clientleafsystem.cpp:660-666</c>); it is never on the client entity
/// list, so <c>SimulateEntities</c> (<c>cdll_client_int.cpp:2010-2015</c>) never visits one, and it is drawn only when
/// <c>BuildRenderablesList</c> collates its leaf (<c>clientleafsystem.cpp:1813</c>), through <c>DrawStaticProps</c>
/// (<c>viewrender.cpp:4041</c>). So with no entities and the camera facing away, no per-prop pass visits a static
/// prop at all; facing them, exactly the ones in view are visited. Every count is carried from the loop that did the
/// work (B243): <see cref="EntityModelSet.Simulated"/> from the simulate loop, <see cref="EntityModelSet.StaticPropsFiled"/>
/// from the <c>AddRenderable</c> call.
/// </remarks>
public sealed class StaticPropGatherConformanceTests
{
    [Test]
    public void Instances_NoEntitiesFacingAway_VisitsNoStaticProp()
    {
        EntityModelSet models = Level(Static(0, x: -5000f), Static(1, x: -6000f), Static(2, y: -5000f));
        List<ModelInstance> instances = [];

        models.Instances([], instances, frustum: LookingAlongX());

        instances.ShouldBeEmpty();
        models.Simulated.ShouldBe(0);
        models.Culled.ShouldBe(3, "the three are filed and turned away by the gather, not missing");
    }

    /// <remarks>The control: one entity behind the camera IS visited by the entity walk, so the counter can count.</remarks>
    [Test]
    public void Instances_OneEntityFacingAway_VisitsOnlyTheEntity()
    {
        EntityModelSet models = Level(Static(0, x: -5000f), Static(1, x: -6000f));
        List<ModelInstance> instances = [];
        SceneProp[] entity = [new(5, "models/props/crate.mdl", SceneModelKind.Studio, new ScenePose { X = -5000f }, null)];

        models.Add(entity, Boxed);
        models.Instances(entity, instances, frustum: LookingAlongX());

        models.Simulated.ShouldBe(1);
    }

    [Test]
    public void Instances_FacingTwoOfThree_GathersAndVisitsExactlyThoseTwo()
    {
        EntityModelSet models = Level(Static(0, x: 500f), Static(1, x: -5000f), Static(2, x: 800f));
        List<ModelInstance> instances = [];

        models.Instances([], instances, frustum: LookingAlongX());

        instances.Select(instance => instance.EntityIndex).Order()
            .ShouldBe([PropModels.FirstStaticPropEntityIndex, PropModels.FirstStaticPropEntityIndex + 2]);
        models.Simulated.ShouldBe(2);
        models.Culled.ShouldBe(1);
    }

    [Test]
    public void Instances_ManyFrames_FilesEachStaticPropOnce()
    {
        EntityModelSet models = Level(Static(0, x: 500f), Static(1, x: -5000f));
        List<ModelInstance> instances = [];

        for (int frame = 0; frame < 3; frame++)
        {
            models.Instances([], instances, frustum: LookingAlongX(), seconds: frame * 0.015d);
        }

        models.StaticPropsFiled.ShouldBe(2);
        instances.ShouldHaveSingleItem().EntityIndex.ShouldBe(PropModels.FirstStaticPropEntityIndex);
    }

    /// <remarks>A skip's frame simulates entities only (B504), so it gathers no static prop even in view.</remarks>
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void Instances_GathersStaticProps_DrawsTheInViewOneOnlyWhenSet(bool gathers, int drawn)
    {
        EntityModelSet models = Level(Static(0, x: 500f));
        models.GathersStaticProps = gathers;
        List<ModelInstance> instances = [];

        models.Instances([], instances, frustum: LookingAlongX());

        instances.Count.ShouldBe(drawn);
        models.Simulated.ShouldBe(drawn);
    }

    /// <remarks>A new map is a new leaf system (`LevelInitPreEntity`), so the static props are filed into it again.</remarks>
    [Test]
    public void Instances_AfterANewTree_FilesTheStaticPropsAgain()
    {
        EntityModelSet models = Level(Static(0, x: 500f));
        List<ModelInstance> instances = [];

        models.Instances([], instances, frustum: LookingAlongX());
        models.Tree = null;
        models.Instances([], instances, frustum: LookingAlongX());

        models.StaticPropsFiled.ShouldBe(2);
        instances.ShouldHaveSingleItem();
    }

    private static EntityModelSet Level(params SceneProp[] statics)
    {
        EntityModelSet models = new() { StaticProps = statics };

        // The production order: the moment packs (and with it, once, the level's static props) before it poses.
        models.Add([], Boxed);

        return models;
    }

    private static SceneProp Static(int index, float x = 0f, float y = 0f) =>
        PropModels.StaticModel(new BspStaticProp("models/props/crate.mdl", x, y, 0f, 0f, 0f, 0f, 1f), index);

    private static PropModels.ModelFrames Boxed(string path) =>
        ModelFramesFixture.OneTriangle(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) };

    private static ViewFrustum LookingAlongX() =>
        ViewFrustum.Perspective(
            origin: (0f, 0f, 0f),
            forward: (1f, 0f, 0f),
            right: (0f, -1f, 0f),
            up: (0f, 0f, 1f),
            nearZ: 1f,
            farZ: 10000f,
            fovX: 90f,
            fovY: 60f);
}
