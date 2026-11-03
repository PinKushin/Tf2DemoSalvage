using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A static prop is lit from its handle and never touches the light cache (B674).</summary>
/// <remarks>
/// **Read in disassembly, `engine.dll`.** `CStaticPropMgr::PrecacheLighting` (`0x180205b20`) makes a handle (`FUN_1801b8350`) for every
/// renderable prop and hands it to the instance. `FUN_1800eb1a0` passes it to the model draw `0x1800f1bd0` as `param_4`. With `param_4` set
/// that function has no call to `LightcacheGet` (`FUN_1801b9cd0`): it is `FUN_1801ba590(handle, …, 7)` when `*param_7` is 0, and
/// `FUN_1801ba590(handle, …, 6 + FUN_1801bb8a0(handle))` with a colour mesh on DX9. `*param_7` is 0 for a bump-mapped model and for
/// a model not compiled `$staticprop` (`studiohdr.flags &amp; 0x10`), so a prop with neither colours nor a CPU bake takes flags 7 as well.
/// The batched path `FUN_1800eb5e0` chooses the same way. *Not read:* that creating the instance sets `+0x12e` bit 0 whenever a handle
/// is passed, which `FUN_1800eb1a0` tests; the function that does so is reached through a vtable.
/// </remarks>
public sealed class StaticPropHandleConformanceTests
{
    private static readonly AmbientCube HandleCube = new((0.1f, 0.2f, 0.3f), default, default, default, default, default);

    private static readonly AmbientCube CacheCube = new((0.7f, 0.7f, 0.7f), default, default, default, default, default);

    private static readonly SunLight HandleSun = new(1f, 2f, 3f, 0f, 0f, -1f);

    /// <summary>Asks for each kind of static prop's light and for a moving model's beside it, and counts who asked the cache.</summary>
    private sealed class Asked
    {
        public int Light { get; private set; }

        public int Sun { get; private set; }

        public PointLighting LightAt(float x, float y, float z)
        {
            _ = (x, y, z);
            Light++;

            return PointLighting.Bounce(CacheCube);
        }

        public SunLight? SunAt(float x, float y, float z)
        {
            _ = (x, y, z);
            Sun++;

            return null;
        }
    }

    private static PropModels.ModelFrames Frames(int studioFlags) => new(
        [new[] { new PropVertex(0, 0f, 0f, 0f, 0f, MaterialIndex: 3), new PropVertex(1, 0f, 0f, 0f, 0f, MaterialIndex: 3), new PropVertex(2, 0f, 0f, 0f, 0f, MaterialIndex: 3) }],
        new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
        [0],
        [true])
    {
        StudioFlags = studioFlags,
    };

    private static SceneProp StaticProp() =>
        PropModels.StaticModel(new BspStaticProp("models/props/crate.mdl", 0f, 0f, 100f, 0f, 0f, 0f, 1f), 0);

    private static EntityModelSet Models(int studioFlags, bool colours, List<int> handles) => new()
    {
        Geometry = _ => Frames(studioFlags),
        StaticPropColours = colours
            ? new Dictionary<int, float[]> { [PropModels.FirstStaticPropEntityIndex] = [0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f] }
            : new Dictionary<int, float[]>(),
        StaticPlusDynamicLights = static (_, _, _, _) => PointLighting.None,
        StaticPropFullLighting = static (_, _, _, _) => (PointLighting.Bounce(HandleCube), HandleSun),
        StaticPropHandle = (entity, _, _, _) => handles.Add(entity),
    };

    /// <remarks>Whichever state it draws with: colour mesh (flags 6), bump mapped (flags 7), or neither (flags 7).</remarks>
    [TestCase(StudioModelFlags.StaticProp, true, TestName = "Instances_AColourMeshStaticProp_AsksTheCacheNothing")]
    [TestCase(StudioModelFlags.StaticProp | StudioModelFlags.UsesBumpmapping, true, TestName = "Instances_ABumpMappedStaticProp_AsksTheCacheNothing")]
    [TestCase(0, false, TestName = "Instances_AStaticPropWithNoColourMeshAndNoBump_AsksTheCacheNothing")]
    public void Instances_AnyStaticProp_AsksTheCacheNothing(int studioFlags, bool colours)
    {
        List<int> handles = [];
        EntityModelSet models = Models(studioFlags, colours, handles);
        SceneProp prop = StaticProp();
        Asked asked = new();
        List<ModelInstance> drawn = [];

        models.StaticProps = [prop];
        models.Add([]);
        models.Instances([], drawn, asked.LightAt, asked.SunAt);

        drawn.Count.ShouldBe(1, "the control: it was drawn");
        asked.Light.ShouldBe(0);
        asked.Sun.ShouldBe(0);
        handles.ShouldBe([PropModels.FirstStaticPropEntityIndex], "`0x180205b20` builds a handle for every prop, once");
    }

    /// <remarks>The control for the test above: the same set asks the cache for a model that moves, which has no handle (`param_4` null, flags `0xf`).</remarks>
    [Test]
    public void Instances_AMovingModel_AsksTheCacheOnceEach()
    {
        EntityModelSet models = Models(StudioModelFlags.StaticProp, colours: false, []);
        SceneProp prop = new(5, "models/props/crate.mdl", SceneModelKind.Studio, new ScenePose { Z = 100f });
        Asked asked = new();
        List<ModelInstance> drawn = [];

        models.Add([prop]);
        models.Instances([prop], drawn, asked.LightAt, asked.SunAt);

        drawn.Count.ShouldBe(1);
        asked.Light.ShouldBe(1);
        asked.Sun.ShouldBe(1);
        drawn[0].Light.ShouldBe(CacheCube);
    }

    /// <remarks>
    /// **A prop with no colours, not bump mapped and not `$staticprop` takes the handle's flags 7**, `LAB_1800f1f3b` with `*param_7` 0: where this
    /// class took the cache's cell, cube and sun, the first entry in its cell lit it. Its value is the handle's own, carried here exactly.
    /// </remarks>
    [Test]
    public void Instances_AStaticPropWithNoColourMeshAndNoBump_DrawsTheHandlesFullState()
    {
        EntityModelSet models = Models(0, colours: false, []);
        SceneProp prop = StaticProp();
        Asked asked = new();
        List<ModelInstance> drawn = [];

        models.StaticProps = [prop];
        models.Add([]);
        models.Instances([], drawn, asked.LightAt, asked.SunAt);

        ModelInstance instance = drawn.ShouldHaveSingleItem();

        instance.BakedColours.ShouldBeNull("the control: this is the class with no colour mesh");
        instance.Light.ShouldBe(HandleCube);
        instance.Sun.ShouldBe(HandleSun);
    }
}
