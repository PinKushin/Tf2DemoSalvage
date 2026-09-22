using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// vphysics' material manager and surface lookups — <c>FUN_180019370</c>, <c>FUN_1800192e0</c>, <c>FUN_1800192b0</c>,
/// <c>FUN_180024080</c>, <c>FUN_1800181d0</c> and <c>FUN_180019220</c> — against what the shipped binary answered (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected value was printed by the `vphysics-materials` probe's `list` mode**, which wrote the same surfaces, record,
/// objects and world table into fabricated structs with the image's own manager and material tables and called the routines in
/// process. The probe's sweep compares the same routines over 200,000 drawn cases.
/// </remarks>
public sealed class VphysicsSurfacePropsConformanceTests
{
    private const long SixtyFour = 0x3fe47ae151eb8520;

    /// <remarks>
    /// **Both are the product of the two surfaces' values, clamped to `[0, 1]`**, and a negative or NaN product is zero — friction by
    /// `COMISD`/`JC`, elasticity by `MAXSD` answering its second operand.
    /// </remarks>
    [TestCase(0.3f, 0.7f, 0.3f, 0.7f, 0x3fcae147b851eb80, 0x3fcae147b851eb80)]
    [TestCase(1.5f, 0.9f, 2f, 0.9f, 0x3ff0000000000000, 0x3ff0000000000000)]
    [TestCase(-0.5f, 0.5f, -1f, 0.5f, 0L, 0L)]
    [TestCase(float.NaN, 0.5f, float.NaN, 0.5f, 0L, 0L)]
    public void FrictionAndElasticity_TwoSurfaces_AreTheBinarysClampedProducts(
        float firstFriction, float secondFriction, float firstElasticity, float secondElasticity, long friction, long elasticity)
    {
        VphysicsSurfaceProps props = Props([firstFriction, secondFriction], [firstElasticity, secondElasticity]);
        IvpContactRecord record = Record(props, (false, false), (false, false), (0f, 0f, 1f));

        Bits(props.FrictionFactor(record)).ShouldBe(friction);
        Bits(props.Elasticity(record)).ShouldBe(elasticity);
    }

    /// <remarks>
    /// **The override needs a core's `+0x58` and a flagged physics object**, the first object's before the second's, and zeroes the
    /// friction only when the normal is long enough and lies more than `sin 15°` along that core's x. Both cores are unturned.
    /// </remarks>
    [TestCase(true, false, true, false, 1f, 0f, 0f, 0L)]
    [TestCase(true, false, true, false, 0f, 1f, 0f, SixtyFour)]
    [TestCase(true, false, true, false, 0.005f, 0f, 0f, SixtyFour)]
    [TestCase(false, true, false, true, 0.3f, 0.953939f, 0f, 0L)]
    [TestCase(true, true, false, false, 1f, 0f, 0f, SixtyFour)]
    public void FrictionFactor_TheOverridesConditions_AnswerTheBinarysFriction(
        bool firstController, bool secondController, bool firstFlag, bool secondFlag, float x, float y, float z, long friction)
    {
        VphysicsSurfaceProps props = Props([0.8f, 0.8f], [0.8f, 0.8f]);
        IvpContactRecord record = Record(props, (firstController, secondController), (firstFlag, secondFlag), (x, y, z));

        Bits(props.FrictionFactor(record)).ShouldBe(friction);
        Bits(props.Elasticity(record)).ShouldBe(SixtyFour);
    }

    /// <remarks>
    /// **With both objects flagged, the first object's core decides**: the normal lies along its unturned x, so the friction is zeroed
    /// even though the second core, turned a quarter about z, sees the same normal across its own x.
    /// </remarks>
    [Test]
    public void FrictionFactor_BothObjectsFlagged_AsksTheFirstObjectsCore()
    {
        VphysicsSurfaceProps props = Props([0.8f, 0.8f], [0.8f, 0.8f]);
        IvpContactRecord record = Record(props, (true, true), (true, true), (1f, 0f, 0f));

        record.SecondObject!.Core!.CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0.70710677f, 0.70710677f), (0d, 0d, 0d));

        Bits(props.FrictionFactor(record)).ShouldBe(0L);
    }

    /// <remarks>
    /// Three surfaces with `default` second, the shadow surface the third. **An index past `0x7f` is surface zero unless it is
    /// `0xf000`**, and a negative or past-the-end index is none.
    /// </remarks>
    [TestCase(-1, -1)]
    [TestCase(0, 0)]
    [TestCase(2, 2)]
    [TestCase(3, -1)]
    [TestCase(0x80, 0)]
    [TestCase(0xf000, 2)]
    public void GetIVPMaterial_AnIndexTheBinaryWasAsked_AnswersItsSurface(int index, int surface)
    {
        VphysicsSurfaceProps props = Lookups();

        Slot(props, props.GetIVPMaterial(index)).ShouldBe(surface);
    }

    /// <remarks>
    /// Slot 5, <c>FUN_1800184a0</c>, read from the disassembly: the same remap past `0x7f`, but **a negative or past-the-end index is
    /// surface zero rather than none** — which is what a world texture with no `$surfaceprop` resolves to (`CMod_LoadTextures`
    /// stores `GetSurfaceIndex`'s −1), and what `ParseImpactData` reads the game material of (B415).
    /// </remarks>
    [TestCase(-1, 0)]
    [TestCase(0, 0)]
    [TestCase(2, 2)]
    [TestCase(3, 0)]
    [TestCase(0x80, 0)]
    [TestCase(0xf000, 2)]
    public void GetSurfaceData_AnIndexTheBinaryWasAsked_AnswersItsSurface(int index, int surface)
    {
        VphysicsSurfaceProps props = Lookups();

        Slot(props, props.GetSurfaceData(index)).ShouldBe(surface);
    }

    /// <remarks>
    /// **A triangle's index goes through the world table first**: the map `[2, 0xf000, −1, 7]` sends 0 to surface 2, 1 to the
    /// shadow, 2 to `0xffff` — surface zero — and 3 to a surface that does not exist, which falls back to `default`, as the
    /// identity slots 4 and 5 do.
    /// </remarks>
    [TestCase(0, 2)]
    [TestCase(1, 2)]
    [TestCase(2, 0)]
    [TestCase(3, 1)]
    [TestCase(4, 1)]
    [TestCase(5, 1)]
    [TestCase(0x80, 0)]
    [TestCase(0xf000, 2)]
    public void MaterialAt_ATriangleIndexThroughTheWorldTable_AnswersTheBinarysSurface(int index, int surface)
    {
        VphysicsSurfaceProps props = Lookups();

        props.SetWorldMaterialIndexTable([2, 0xf000, -1, 7]);

        Slot(props, props.MaterialAt(new IvpCollisionObject(), index)).ShouldBe(surface);
    }

    private static VphysicsSurfaceProps Lookups() =>
        new VphysicsSurfaceProps(
        [
            new VphysicsSurface("surface0", new SurfacePhysicsParams(0.1f, 0.1f, 0f, 0f, 0f), hasSecondFriction: false),
            new VphysicsSurface("default", new SurfacePhysicsParams(0.2f, 0.2f, 0f, 0f, 0f), hasSecondFriction: false),
            new VphysicsSurface("surface2", new SurfacePhysicsParams(0.3f, 0.3f, 0f, 0f, 0f), hasSecondFriction: false),
        ])
        {
            ShadowSurface = 2,
        };

    private static VphysicsSurfaceProps Props(float[] friction, float[] elasticity) =>
        new(
        [
            new VphysicsSurface("surface0", new SurfacePhysicsParams(friction[0], elasticity[0], 0f, 0f, 0f), hasSecondFriction: false),
            new VphysicsSurface("surface1", new SurfacePhysicsParams(friction[1], elasticity[1], 0f, 0f, 0f), hasSecondFriction: false),
        ]);

    private static IvpContactRecord Record(
        VphysicsSurfaceProps props, (bool First, bool Second) controllers, (bool First, bool Second) flags, (float X, float Y, float Z) normal) =>
        new()
        {
            Normal = normal,
            FirstObject = new IvpCollisionObject { Core = new IvpRigidBody { HasOffset58 = controllers.First }, PhysicsFlag48Bit6 = flags.First },
            SecondObject = new IvpCollisionObject { Core = new IvpRigidBody { HasOffset58 = controllers.Second }, PhysicsFlag48Bit6 = flags.Second },
            FirstMaterial = props.Surfaces[0],
            SecondMaterial = props.Surfaces[1],
        };

    private static int Slot(VphysicsSurfaceProps props, IIvpMaterial? material) =>
        material is VphysicsSurface surface ? System.Linq.Enumerable.ToList(props.Surfaces).IndexOf(surface) : -1;

    private static long Bits(double value) => System.BitConverter.DoubleToInt64Bits(value);
}
