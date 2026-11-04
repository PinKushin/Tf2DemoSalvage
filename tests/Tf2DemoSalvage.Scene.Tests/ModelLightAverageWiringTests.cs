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

    private static PointLighting Lit(float cube, BspWorldLight light)
    {
        LightingState state = new(Flat(cube));

        state.Put(light);

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
        SunLight sun = new(2f, 2f, 2f, 0f, 0f, -1f);

        models.Instances([prop], drawn, (_, _, _) => Lit(1f, Lamp), (_, _, _) => sun, 10d);
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
