using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The average on the path a draw takes, read from the <see cref="ModelInstance"/> the renderer gets (B676).</summary>
public sealed class ModelLightAverageWiringTests
{
    private const string Path = "models/player/demo.mdl";
    private const int Walker = 5;

    /// <summary>`exp( -5 * 0.2 )`.</summary>
    private static readonly float F = MathF.Exp(-1f);

    private static readonly BspWorldLight Lamp = new((10f, 0f, 0f), (40f, 40f, 40f), default, WorldLightKind.Point, QuadraticAttenuation: 1f, LumpIndex: 3);

    private static AmbientCube Flat(float value) => new(
        (value, value, value), (value, value, value), (value, value, value),
        (value, value, value), (value, value, value), (value, value, value));

    private static PointLighting Lit(float cube, params BspWorldLight[] lights)
    {
        LightingState state = new(Flat(cube));

        foreach (BspWorldLight light in lights)
        {
            state.Put(light);
        }

        return state.Lighting();
    }

    private static SceneProp Prop(int entity = Walker, int? attachedTo = null) =>
        EntityModelSetPrePoseTests.Prop(Path, entity, attachedTo: attachedTo);

    private static PropModels.ModelFrames Loaded(string path) => EntityModelSetPrePoseTests.Skinned("root");

    private static EntityModelSet Models(params SceneProp[] props)
    {
        EntityModelSet models = new();

        models.Add(props, Loaded);

        return models;
    }

    /// <summary>The cube, the local light and the sun, all three as the draw carries them, a fifth of a second on.</summary>
    [Test]
    public void Instances_AFifthOfASecondLater_CarriesTheAveragedCubeLocalsAndSun()
    {
        SceneProp prop = Prop();
        EntityModelSet models = Models(prop);
        List<ModelInstance> drawn = [];
        // The sun is one of the state's slots (B170), drawn as the sun and not as a local light.
        BspWorldLight sun = new(default, (2f, 2f, 2f), (0f, 0f, -1f), WorldLightKind.SkyLight);

        models.Instances([prop], drawn, (_, _, _) => Lit(1f, Lamp, sun), static (_, _, _) => null, 10d);
        models.Instances([prop], drawn, (_, _, _) => Lit(0.5f, Lamp with { LumpIndex = 4 }), static (_, _, _) => null, 10.2d);

        ModelInstance instance = drawn.Single();

        instance.Light.ShouldNotBeNull().PositiveX.Red.ShouldBe(0.5f + (0.5f * F), 0.0001f);
        instance.Sun.ShouldNotBeNull().Red.ShouldBe(2f * F, 0.001f, "the sun has left, and fades");

        // The new light in from nothing, the one that left at f.
        IReadOnlyList<LocalLight> locals = instance.Locals.ShouldNotBeNull();

        locals.Count.ShouldBe(2);
        locals[0].Red.ShouldBe(40f * (1f - F), 0.001f);
        locals[1].Red.ShouldBe(40f * F, 0.001f);
    }

    /// <summary>With `r_lightaverage 0` the draw is the state as it stands, and the sun is its slot (B170), whatever a separate sampler says.</summary>
    [Test]
    public void Instances_AverageOffWithTheSunInASlot_DrawsThatSunAsTheSun()
    {
        SceneProp prop = Prop();
        EntityModelSet models = Models(prop);
        List<ModelInstance> drawn = [];
        BspWorldLight sun = new(default, (2f, 2f, 2f), (0f, 0f, -1f), WorldLightKind.SkyLight);

        models.LightAverage.Enabled = false;
        models.Instances([prop], drawn, (_, _, _) => Lit(1f, Lamp, sun), static (_, _, _) => null, 10d);

        ModelInstance instance = drawn.Single();

        instance.Sun.ShouldNotBeNull().Red.ShouldBe(2f);
        instance.Locals.ShouldNotBeNull().ShouldHaveSingleItem().Red.ShouldBe(40f, "the lamp alone is a local light");
    }

    /// <summary>`C_TFPlayer::DrawModel` returns before any draw (`c_tf_player.cpp:6938`), so `FUN_1800f2dd0` never runs and no history is stamped.</summary>
    [Test]
    public void Instances_AModelTheCloakRefuses_StoresNoHistory()
    {
        SceneProp prop = Prop();
        EntityModelSet models = Models(prop);
        List<ModelInstance> drawn = [];

        models.Cloak = static _ => new CloakBind { Undrawn = true };
        models.Instances([prop], drawn, (_, _, _) => Lit(1f, Lamp), static (_, _, _) => null, 10d);

        drawn.ShouldBeEmpty();
        (models.LightAverage.Instances, models.LightAverage.Draws).ShouldBe((0, 0L));

        // The control: the same model, drawn.
        models.Cloak = null;
        models.Instances([prop], drawn, (_, _, _) => Lit(1f, Lamp), static (_, _, _) => null, 10.2d);

        drawn.Count.ShouldBe(1);
        (models.LightAverage.Instances, models.LightAverage.Draws).ShouldBe((1, 1L));
    }

    /// <summary>Valve's `if ( baseDrawn )` (`c_baseanimating.cpp:3243`): an item whose wearer is not drawn is not drawn, so it stamps nothing.</summary>
    [Test]
    public void Instances_AnItemWhoseWearerIsNotDrawn_StoresNoHistory()
    {
        SceneProp item = Prop(entity: 6, attachedTo: 99);
        EntityModelSet models = Models(item);
        List<ModelInstance> drawn = [];

        models.Instances([item], drawn, (_, _, _) => Lit(1f, Lamp), static (_, _, _) => null, 10d);

        drawn.ShouldBeEmpty();
        models.LightAverage.Draws.ShouldBe(0L);
    }

    /// <summary>A stuck arrow is a `C_LocalTempEntity`, `MODEL_INSTANCE_INVALID` (`c_te_legacytempents.cpp:178`), so `param_2 != 0xffff` fails and it draws its state. Logged live.</summary>
    [Test]
    public void Instances_AStuckArrow_DrawsItsStateAndStoresNoHistory()
    {
        SceneProp arrow = Prop(BoltImpacts.FirstArrowEntityIndex);
        EntityModelSet models = Models(arrow);
        List<ModelInstance> drawn = [];

        models.Instances([arrow], drawn, (_, _, _) => Lit(1f, Lamp), static (_, _, _) => null, 10d);
        models.Instances([arrow], drawn, (_, _, _) => Lit(0.5f, Lamp), static (_, _, _) => null, 10.2d);

        drawn.Single().Light.ShouldBe(Flat(0.5f));
        models.LightAverage.Draws.ShouldBe(0L);
    }

    /// <summary>An econ attachment is drawn with `instance = MODEL_INSTANCE_INVALID` (`econ_entity.cpp:129`), so it draws the state its parent's history blends.</summary>
    [Test]
    public void Instances_AnEconAttachment_DrawsTheStateItsParentAverages()
    {
        SceneProp prop = Prop();
        EntityModelSet models = Models(prop);
        List<ModelInstance> drawn = [];

        models.Attachments = static _ => ["models/attached.mdl"];
        models.Instances([prop], drawn, (_, _, _) => Lit(1f, Lamp), static (_, _, _) => null, 10d);
        models.Instances([prop], drawn, (_, _, _) => Lit(0.5f, Lamp), static (_, _, _) => null, 10.2d);

        drawn.Single(static instance => instance.ModelPath == Path).Light.ShouldNotBeNull().PositiveX.Red.ShouldBe(0.5f + (0.5f * F), 0.0001f);
        drawn.Single(static instance => instance.ModelPath == "models/attached.mdl").Light.ShouldBe(Flat(0.5f));
    }

    /// <summary>The undrawn pass is posed for what may be a monitor's draw and the hidden pass for a decal's: neither stamps a history, and the undrawn one's model carries its raw state.</summary>
    [Test]
    public void Instances_TheUndrawnAndHiddenPasses_StoreNoHistory()
    {
        SceneProp prop = Prop();
        EntityModelSet models = Models(prop);
        List<ModelInstance> drawn = [];

        models.Instances([prop], drawn, (_, _, _) => Lit(1f, Lamp), static (_, _, _) => null, 10d, pass: EntityModelSet.UndrawnPass);

        drawn.Single().Light.ShouldBe(Flat(1f), "the instrument: the undrawn pass reached the draw's code");

        models.Instances([prop], drawn, (_, _, _) => Lit(1f, Lamp), static (_, _, _) => null, 10d, pass: EntityModelSet.HiddenPass);

        models.LightAverage.Draws.ShouldBe(0L);
    }

    /// <summary>A static prop has a handle, `param_4`, so `FUN_1800f2dd0` draws its full state with no average, however it changes.</summary>
    [Test]
    public void Instances_AStaticProp_DrawsItsStateAndStoresNoHistory()
    {
        SceneProp prop = Prop(PropModels.FirstStaticPropEntityIndex);
        EntityModelSet models = Models(prop);
        List<ModelInstance> drawn = [];
        float now = 1f;

        // No lamp in it: the gate is all that keeps a state this plain from the average.
        models.StaticPropFullLighting = (_, _, _, _) => (new LightingState(Flat(now)).Lighting(), null);
        models.Instances([prop], drawn, (_, _, _) => Lit(9f, Lamp), static (_, _, _) => null, 10d);
        drawn.Single().Light.ShouldBe(Flat(1f), "the instrument: the handle's state reached the draw");

        now = 0.5f;
        models.Instances([prop], drawn, (_, _, _) => Lit(9f, Lamp), static (_, _, _) => null, 10.2d);

        drawn.Single().Light.ShouldBe(Flat(0.5f));
        models.LightAverage.Draws.ShouldBe(0L);
    }
}
