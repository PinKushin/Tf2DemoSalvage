using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `r_ambientboost` as `engine.dll` `FUN_1800f2dd0` computes it (build 11191711, B672), on cubes and lights whose answers are arithmetic.
/// </summary>
/// <remarks>
/// Every case sets one lamp 10 units away with a quadratic falloff, so its weight is `1 / 100`, and a grey cube, so a face's luminance is
/// its value (`0.59 + 0.3 + 0.11` is 1). A lamp of 300 then weighs 3, `r_ambientfraction` takes 0.3 of that, and over a cube of 0.1 the
/// scale is 3.
/// </remarks>
public sealed class AmbientBoostConformanceTests
{
    private const float Tolerance = 0.0001f;

    private static AmbientCube Flat(float value) => new(
        (value, value, value), (value, value, value), (value, value, value),
        (value, value, value), (value, value, value), (value, value, value));

    private static LocalLight Lamp(float intensity) => new(10f, 0f, 0f, intensity, intensity, intensity, 0f, 0f, 1f, 0f);

    private static AmbientCube Boosted(AmbientCube cube, IReadOnlyList<LocalLight> locals, SunLight? sun = null, int flags = StudioModelFlags.AmbientBoost, AmbientBoost? boost = null) =>
        (boost ?? new AmbientBoost()).Apply(flags, cube, sun, locals, 0f, 0f, 0f);

    /// <summary>`min( 0.1 · 3 / 0.1, 5 )` is 3, so a face of 0.1 draws 0.3.</summary>
    [Test]
    public void Apply_ACubeSwampedByALamp_ScalesByTheFractionOfDirectOverTheBrightestFace() =>
        Boosted(Flat(0.1f), [Lamp(300f)]).PositiveX.Red.ShouldBe(0.3f, Tolerance);

    /// <summary>The control: the same cube and lamp on a model without `STUDIOHDR_FLAGS_AMBIENT_BOOST`.</summary>
    [Test]
    public void Apply_AModelWithoutTheFlag_DrawsItsCube() =>
        Boosted(Flat(0.1f), [Lamp(300f)], flags: StudioModelFlags.StaticProp).ShouldBe(Flat(0.1f));

    /// <summary>
    /// The weights are per channel: a face of (0.1, 0.2, 0) weighs 0.148, a lamp of (100, 300, 200) at 100 weighs 2.29, so the scale is
    /// `0.229 / 0.148`. A grey case cannot tell the three weights apart.
    /// </summary>
    [Test]
    public void Apply_ColouredFacesAndLamp_WeighEachChannelByItsOwnWeight()
    {
        AmbientCube cube = new((0.1f, 0.2f, 0f), (0.1f, 0.2f, 0f), (0.1f, 0.2f, 0f), (0.1f, 0.2f, 0f), (0.1f, 0.2f, 0f), (0.1f, 0.2f, 0f));
        LocalLight lamp = new(10f, 0f, 0f, 100f, 300f, 200f, 0f, 0f, 1f, 0f);

        Boosted(cube, [lamp]).PositiveX.Red.ShouldBe(0.1f * (0.229f / 0.148f), Tolerance);
    }

    /// <summary>`max` starts from the first face clamped at 0, so a cube of all -0.1 divides by 0 and is held at 5, not scaled by -3.</summary>
    [Test]
    public void Apply_EveryFaceNegative_DividesByZeroAndHoldsAtFive() =>
        Boosted(Flat(-0.1f), [Lamp(300f)]).PositiveX.Red.ShouldBe(-0.5f, Tolerance);

    /// <summary>`r_ambientboost 0`.</summary>
    [Test]
    public void Apply_WithTheCvarOff_DrawsItsCube() =>
        Boosted(Flat(0.1f), [Lamp(300f)], boost: new AmbientBoost { Settings = AmbientBoostSettings.Default with { Enabled = false } })
            .ShouldBe(Flat(0.1f));

    /// <summary>A light count of 0 fails the first test, whatever the cube.</summary>
    [Test]
    public void Apply_WithNoLight_DrawsItsCube() => Boosted(Flat(0.1f), []).ShouldBe(Flat(0.1f));

    /// <summary>An average at `r_ambientmin`, 0.3, is bright enough: the test is `min &lt;= avg`.</summary>
    [Test]
    public void Apply_AnAverageAtTheMinimum_DrawsItsCube() =>
        Boosted(Flat(0.3f), [Lamp(30000f)]).ShouldBe(Flat(0.3f));

    /// <summary>`r_ambientfraction · direct` equal to the average does not fire: the test is `direct &lt;= avg`.</summary>
    [Test]
    public void Apply_TheFractionOfDirectEqualToTheAverage_DrawsItsCube() =>
        Boosted(Flat(0.1f), [Lamp(100f)]).ShouldBe(Flat(0.1f));

    /// <summary>A lamp of 1000 asks for 10, and the constant at `0x180374cc8` holds it to 5.</summary>
    [Test]
    public void Apply_AScaleAboveFive_IsHeldAtFive() =>
        Boosted(Flat(0.1f), [Lamp(1000f)]).NegativeZ.Blue.ShouldBe(0.5f, Tolerance);

    /// <summary>The divisor is the brightest face, not the average: one face of 0.2 among 0.05s halves the scale it would have had.</summary>
    [Test]
    public void Apply_UnevenFaces_DivideByTheBrightest()
    {
        AmbientCube cube = Flat(0.05f) with { NegativeY = (0.2f, 0.2f, 0.2f) };

        // avg 0.075, direct 0.3, so scale 0.3 / 0.2 = 1.5.
        Boosted(cube, [Lamp(300f)]).PositiveX.Green.ShouldBe(0.075f, Tolerance);
    }

    /// <summary>`dworldlight_t.intensity` is read through the slot's pointer, before its style: a lamp whose style is dark still weighs.</summary>
    [Test]
    public void Apply_ALampWhoseStyleIsDark_WeighsItsUnstyledIntensity() =>
        Boosted(Flat(0.1f), [Lamp(0f) with { Unstyled = (300f, 300f, 300f) }]).PositiveX.Red.ShouldBe(0.3f, Tolerance);

    /// <summary>An attenuation at or under `1e-5` is taken as 1, not inverted: a lamp at its own point with no constant term.</summary>
    [Test]
    public void Apply_AnAttenuationOfZero_WeighsTheLightAtOne() =>
        Boosted(Flat(0.1f), [new LocalLight(0f, 0f, 0f, 3f, 3f, 3f, 0f, 0f, 1f, 0f)]).PositiveX.Red.ShouldBe(0.3f, Tolerance);

    /// <summary>The sun is a slot like any other, weighed by the sky light's own falloff; a sky with all three 0 weighs it at 1.</summary>
    [Test]
    public void Apply_TheSunAlone_IsWeighedByTheSkyLightsFalloff()
    {
        SunLight sun = new(3f, 3f, 3f, 0f, 0f, -1f);

        Boosted(Flat(0.1f), [], sun).PositiveX.Red.ShouldBe(0.3f, Tolerance);

        AmbientBoost halved = new() { Sky = new BspWorldLight(default, default, default, WorldLightKind.SkyLight, ConstantAttenuation: 2f) };

        Boosted(Flat(0.1f), [], sun, boost: halved).PositiveX.Red.ShouldBe(0.15f, Tolerance);
    }

    /// <summary>The defaults are `engine.dll`'s registrations: "1", "0.3", "0.1".</summary>
    [Test]
    public void Settings_ByDefault_AreTheEnginesRegistrations() =>
        new ViewerSettings().AmbientBoost.ShouldBe(new AmbientBoostSettings(true, 0.3f, 0.1f));

    /// <summary>A config's three lines reach the settings.</summary>
    [Test]
    public void Settings_FromAConfig_TakeEachValue() =>
        ViewerSettings.Parse("r_ambientboost 0\nr_ambientmin 0\nr_ambientfraction 0.5")
            .AmbientBoost.ShouldBe(new AmbientBoostSettings(false, 0f, 0.5f));

    /// <summary>The moment's settings reach the boost a pose uses.</summary>
    [Test]
    public void Pose_AMomentCarryingTheSettings_HandsThemToTheBoost()
    {
        SceneProp prop = EntityModelSetPrePoseTests.Prop("models/player/demo.mdl", 5);
        EntityModelSet models = new();

        models.Add([prop], static _ => EntityModelSetPrePoseTests.Skinned("root"));

        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance);
        AmbientBoostSettings set = new(false, 0.7f, 0.2f);
        MomentInfo info = new(0d, 0, false, null, null, 0.015f, 54f, AmbientBoost: set);

        scene.Build([], [prop], info);
        scene.Pose(info);

        models.AmbientBoost.Settings.ShouldBe(set);
    }

    /// <summary>On the draw's path: a flagged model's instance carries the boosted cube, an unflagged one's the state (the control).</summary>
    [Test]
    public void Instances_AFlaggedModelSwampedByALamp_CarriesTheBoostedCube()
    {
        BspWorldLight lamp = new((10f, 0f, 0f), (300f, 300f, 300f), default, WorldLightKind.Point, QuadraticAttenuation: 1f, LumpIndex: 3);

        AmbientCube Drawn(int flags)
        {
            SceneProp prop = EntityModelSetPrePoseTests.Prop("models/flagged.mdl", 5);
            EntityModelSet models = new();
            List<ModelInstance> drawn = [];

            models.Add([prop], _ => EntityModelSetPrePoseTests.Skinned("root") with { StudioFlags = flags });

            LightingState state = new(Flat(0.1f));
            state.Put(lamp);

            models.Instances([prop], drawn, (_, _, _) => state.Lighting(), static (_, _, _) => null, 10d);

            return drawn.Single().Light.ShouldNotBeNull();
        }

        Drawn(0).PositiveX.Red.ShouldBe(0.1f, Tolerance);
        Drawn(StudioModelFlags.AmbientBoost).PositiveX.Red.ShouldBeGreaterThan(0.1f + Tolerance);
    }

    /// <summary>An econ attachment reaches the same function with its own model, so its own flag decides, not its item's.</summary>
    [Test]
    public void Instances_AFlaggedAttachmentOnAnUnflaggedItem_IsBoostedAlone()
    {
        const string Item = "models/item.mdl";
        const string Attached = "models/attached.mdl";
        BspWorldLight lamp = new((10f, 0f, 0f), (300f, 300f, 300f), default, WorldLightKind.Point, QuadraticAttenuation: 1f, LumpIndex: 3);
        SceneProp prop = EntityModelSetPrePoseTests.Prop(Item, 5);
        EntityModelSet models = new() { Attachments = static _ => [Attached] };
        List<ModelInstance> drawn = [];

        // The attachment's frames packed as any model's are; only the item is drawn.
        models.Add([prop, EntityModelSetPrePoseTests.Prop(Attached, 6)], static path => EntityModelSetPrePoseTests.Skinned("root") with
        {
            StudioFlags = path == Attached ? StudioModelFlags.AmbientBoost : 0,
        });

        LightingState state = new(Flat(0.1f));
        state.Put(lamp);

        models.Instances([prop], drawn, (_, _, _) => state.Lighting(), static (_, _, _) => null, 10d);

        drawn.Single(static instance => instance.ModelPath == Item).Light.ShouldNotBeNull().PositiveX.Red.ShouldBe(0.1f, Tolerance);
        drawn.Single(static instance => instance.ModelPath == Attached).Light.ShouldNotBeNull().PositiveX.Red.ShouldBe(0.3f, Tolerance);
    }
}
