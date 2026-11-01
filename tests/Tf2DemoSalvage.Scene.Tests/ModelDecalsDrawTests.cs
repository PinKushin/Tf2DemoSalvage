using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What the device asks when it draws a model's decals (B642): which key holds them, which light they take, and which
/// translucent draws are followed by them. `Device3D.DrawDecalsOf` makes these three calls and nothing else.
/// </summary>
public sealed class ModelDecalsDrawTests
{
    /// <summary>One triangle facing +X at the origin, which a shot along −X decals.</summary>
    internal static readonly WorldVertex[] Face =
    [
        new(0f, 2f, 0f, 0f, 0f, 0f, 0f, 0f, NormalX: 1f, NormalZ: 0f),
        new(0f, 0f, 2f, 0f, 0f, 0f, 0f, 0f, NormalX: 1f, NormalZ: 0f),
        new(0f, -2f, -2f, 0f, 0f, 0f, 0f, 0f, NormalX: 1f, NormalZ: 0f),
    ];

    /// <summary>A pool holding one decal on the static prop at <paramref name="lumpIndex"/>.</summary>
    internal static ModelDecals HoldingADecal(int lumpIndex)
    {
        ModelDecals pool = new();

        pool.AddClipped(DecalReplay.StaticPropKey(lumpIndex), Face, Vector3.Zero, new Vector3(-1.1f, 0f, 0f), 4f, 0)
            .ShouldBeTrue("the control: the decal took the triangle");

        return pool;
    }

    private static readonly AmbientCube ModelCube = new((1f, 0f, 0f), (1f, 0f, 0f), (1f, 0f, 0f), (1f, 0f, 0f), (1f, 0f, 0f), (1f, 0f, 0f));
    private static readonly AmbientCube PropCube = new((0f, 1f, 0f), (0f, 1f, 0f), (0f, 1f, 0f), (0f, 1f, 0f), (0f, 1f, 0f), (0f, 1f, 0f));
    private static readonly SunLight ModelSun = new(1f, 2f, 3f, 0f, 0f, -1f);
    private static readonly SunLight PropSun = new(4f, 5f, 6f, 0f, 0f, -1f);
    private static readonly IReadOnlyList<LocalLight> ModelLamps = [new(1f, 0f, 0f, 9f, 9f, 9f, 0f, 0f, 1f, 0f)];
    private static readonly IReadOnlyList<LocalLight> PropLamps = [new(2f, 0f, 0f, 7f, 7f, 7f, 0f, 0f, 1f, 0f)];

    /// <remarks>
    /// **An entity's decals draw under the model's own state** (`m_bStaticLighting` clear, `studiorender.dll`
    /// `0x18000a880` sets nothing): its cube, sun and lamps, each in its own slot. Every value differs, so a swapped slot
    /// or a dropped one fails.
    /// </remarks>
    [Test]
    public void LightFor_AnEntityWithNoDecalLight_IsItsOwnCubeSunAndLamps()
    {
        ModelInstance entity = new("models/player/heavy.mdl", [], ModelCube, ModelSun, Locals: ModelLamps, EntityIndex: 4);

        (AmbientCube? light, SunLight? sun, IReadOnlyList<LocalLight>? locals, bool decalLit) = ModelDecals.LightFor(entity, new ModelDecals());

        light.ShouldBe(ModelCube);
        sun.ShouldBe(ModelSun);
        locals.ShouldBeSameAs(ModelLamps);
        decalLit.ShouldBeFalse();
    }

    /// <remarks>
    /// **A decal the replay placed after the pose takes the pool's late light** (B642 review): the instance was posed with
    /// no holder and carries no <see cref="ModelInstance.DecalLight"/>, and a paused viewer does not pose again.
    /// </remarks>
    [Test]
    public void LightFor_AStaticPropPosedBeforeItsDecalWithALateLight_IsTheLateLight()
    {
        ModelInstance prop = new(
            "models/props/crate.mdl", [], LevelLighting.NoStaticState, null, Locals: ModelLamps,
            EntityIndex: PropModels.FirstStaticPropEntityIndex + 3);
        ModelDecals pool = HoldingADecal(3);

        pool.SetLateLights([new(DecalReplay.StaticPropKey(3), new DecalLighting(PropCube, PropSun, PropLamps))]);

        (AmbientCube? light, SunLight? sun, IReadOnlyList<LocalLight>? locals, bool decalLit) = ModelDecals.LightFor(prop, pool);

        light.ShouldBe(PropCube);
        sun.ShouldBe(PropSun);
        locals.ShouldBeSameAs(PropLamps);
        decalLit.ShouldBeTrue();
    }

    /// <remarks>
    /// **A colour-mesh prop's decals draw under its decal light**, the full state `engine.dll` hands them
    /// (`0x1800f1bd0`, `0x1800eb5e0`), never its own draw's zero cube and flags-6 lamps.
    /// </remarks>
    [Test]
    public void LightFor_AStaticPropWithADecalLight_IsTheDecalLight()
    {
        ModelInstance prop = new(
            "models/props/crate.mdl", [], LevelLighting.NoStaticState, null, Locals: ModelLamps,
            EntityIndex: PropModels.FirstStaticPropEntityIndex, DecalLight: new DecalLighting(PropCube, PropSun, PropLamps));

        (AmbientCube? light, SunLight? sun, IReadOnlyList<LocalLight>? locals, bool decalLit) = ModelDecals.LightFor(prop, null);

        light.ShouldBe(PropCube);
        sun.ShouldBe(PropSun);
        locals.ShouldBeSameAs(PropLamps);
        decalLit.ShouldBeTrue();
    }

    /// <remarks>
    /// **The pose's light wins over a late one** (B642 review 3): a late light stays in the pool after a later pose sets the
    /// instance's, and may carry a dead dlight's lamp; the pose's is current.
    /// </remarks>
    [Test]
    public void LightFor_APosedLightAndALateLightBoth_IsThePosedLight()
    {
        ModelInstance prop = new(
            "models/props/crate.mdl", [], LevelLighting.NoStaticState, null, Locals: ModelLamps,
            EntityIndex: PropModels.FirstStaticPropEntityIndex + 3, DecalLight: new DecalLighting(PropCube, PropSun, PropLamps));
        ModelDecals pool = HoldingADecal(3);

        pool.SetLateLights([new(DecalReplay.StaticPropKey(3), new DecalLighting(ModelCube, ModelSun, ModelLamps))]);

        (AmbientCube? light, SunLight? sun, IReadOnlyList<LocalLight>? locals, _) = ModelDecals.LightFor(prop, pool);

        light.ShouldBe(PropCube);
        sun.ShouldBe(PropSun);
        locals.ShouldBeSameAs(PropLamps);
    }

    [TestCase(4, false, DecalLightSource.Entity)]
    [TestCase(PropModels.FirstStaticPropEntityIndex, true, DecalLightSource.DecalLight)]
    public void SourceOf_ByKindAndAnswer_NamesTheLight(int entity, bool decalLit, DecalLightSource expected) =>
        ModelDecals.SourceOf(new ModelInstance("m.mdl", [], null, null, EntityIndex: entity), decalLit).ShouldBe(expected);

    /// <remarks>
    /// **Only a colour-mesh prop's own light is the fallback** (B642 review 3): a bump-mapped prop carries no colour mesh,
    /// and its own light is its full state, which is right.
    /// </remarks>
    [TestCase(true, DecalLightSource.Fallback)]
    [TestCase(false, DecalLightSource.OwnFullState)]
    public void SourceOf_AStaticPropWithoutItsDecalLight_IsTheFallbackOnlyWithAColourMesh(bool colourMesh, DecalLightSource expected) =>
        ModelDecals.SourceOf(
                new ModelInstance(
                    "m.mdl", [], null, null, EntityIndex: PropModels.FirstStaticPropEntityIndex, BakedColours: colourMesh ? [1f, 1f, 1f] : null),
                decalLit: false)
            .ShouldBe(expected);

    [TestCase(PropModels.FirstStaticPropEntityIndex, false, true)]
    [TestCase(PropModels.FirstStaticPropEntityIndex - 1, false, false)]
    [TestCase(PropModels.FirstStaticPropEntityIndex + 3, false, true)]
    [TestCase(PropModels.FirstStaticPropEntityIndex + 3, true, false)]
    [TestCase(4, false, false)]
    public void DrawsAfterTranslucent_ByKindAndHalf_IsTheStaticPropsOnePassDraw(int entity, bool twoPass, bool draws) =>
        ModelDecals.DrawsAfterTranslucent(new ModelInstance("m.mdl", [], null, null, EntityIndex: entity), twoPass).ShouldBe(draws);

    [Test]
    public void Holds_AStaticPropShotOnceAndOneNeverShot_AnswersEach()
    {
        ModelDecals pool = HoldingADecal(3);

        pool.Holds(DecalReplay.KeyOf(PropModels.FirstStaticPropEntityIndex + 3)).ShouldBeTrue();
        pool.Holds(DecalReplay.KeyOf(PropModels.FirstStaticPropEntityIndex + 4)).ShouldBeFalse();
    }

    /// <remarks>
    /// **A model whose only decal was retired holds none**: `Retire` leaves its list in place, empty (the 1.5 · limit rule
    /// retires the oldest decal of any model).
    /// </remarks>
    [Test]
    public void Holds_AStaticPropWhoseOnlyDecalWasRetired_IsFalse()
    {
        ModelDecals pool = HoldingADecal(3);
        int key = DecalReplay.StaticPropKey(3);

        for (int shot = 0; shot < ModelDecals.MaximumPerModel * 3 / 2; shot++)
        {
            pool.AddClipped(
                DecalReplay.StaticPropKey(10 + (shot / ModelDecals.MaximumPerModel)), Face, Vector3.Zero, new Vector3(-1.1f, 0f, 0f), 4f, 0)
                .ShouldBeTrue();
        }

        pool.Models.ShouldContain(key, "the control: the retired model's list is still in the pool");
        pool.Holds(key).ShouldBeFalse();
    }

    /// <remarks>
    /// **The decal light rides as a reference** (B642 review): the struct is copied on every collation add and sort, and
    /// the first form, a nullable `(PointLighting, SunLight?)` inline, made it 144 bytes larger (432 at `56da2627`, 576 at
    /// `d8cd9101`, 440 with the reference, each measured with <c>Unsafe.SizeOf</c>). Asserted on the field's kind rather
    /// than a byte count, which follows the runtime's layout.
    /// </remarks>
    [Test]
    public void ModelInstance_DecalLight_IsAReferenceNotAnInlineValue()
    {
        typeof(ModelInstance).GetProperty(nameof(ModelInstance.DecalLight))!.PropertyType.IsValueType.ShouldBeFalse();

        // And no other field grew with it: at most one pointer over the size before the field (B642 review 3).
        System.Runtime.CompilerServices.Unsafe.SizeOf<ModelInstance>().ShouldBeLessThanOrEqualTo(SizeBeforeDecalLight + System.IntPtr.Size);
    }

    /// <summary><c>Unsafe.SizeOf&lt;ModelInstance&gt;()</c> at `56da2627`, before B642 added the field, on this runtime.</summary>
    private const int SizeBeforeDecalLight = 432;
}
