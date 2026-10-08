using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>RunFlexRules</c> and the vertex morph, on hand-built rules and vertex animations with known answers (D38, B513).
/// </summary>
public sealed class StudioFlexRulesTests
{
    private const int Const = 1, Fetch1 = 2, Fetch2 = 3, Add = 4, Sub = 5, Mul = 6, Div = 7, Neg = 8, Max = 13, Min = 14;
    private const int TwoWay0 = 15, TwoWay1 = 16, NWay = 17, Combo = 18, Dominate = 19, LowerLid = 20, UpperLid = 21;

    private static readonly StudioFlexController[] Four =
    [
        new("default", "a", 0f, 1f),
        new("default", "b", -1f, 1f),
        new("eyes", "updown", -45f, 45f),
        new("default", "c", 0f, 1f),
    ];

    [TestCase(Add, 3f, 2f, 5f)]
    [TestCase(Sub, 3f, 2f, 1f)]
    [TestCase(Mul, 3f, 2f, 6f)]
    [TestCase(Div, 3f, 2f, 1.5f)]
    [TestCase(Div, 3f, 0.0001f, 0f)]
    [TestCase(Max, 3f, 2f, 3f)]
    [TestCase(Min, 3f, 2f, 2f)]
    public void Run_ABinaryOp_CombinesTheTopTwo(int op, float left, float right, float expected)
    {
        Weights(Rule(0, C(left), C(right), Op(op))).ShouldBe([expected, 0f]);
    }

    [Test]
    public void Run_Neg_NegatesTheTop() => Weights(Rule(0, C(2f), Op(Neg))).ShouldBe([-2f, 0f]);

    [Test]
    public void Run_Fetch1_ReadsTheControllerInItsOwnRange() =>
        Weights(Rule(0, Op(Fetch1, 1)), src: [0f, -0.5f, 0f, 0f]).ShouldBe([-0.5f, 0f]);

    [Test]
    public void Run_Fetch2_ReadsAnEarlierRulesResult() =>
        Weights(Rule(0, C(0.25f)), Rule(1, Op(Fetch2, 0), C(2f), Op(Mul))).ShouldBe([0.25f, 0.5f]);

    [TestCase(-0.5f, 0.5f, 0f)]
    [TestCase(0.5f, 0f, 0.5f)]
    [TestCase(-2f, 1f, 0f)]
    public void Run_TwoWay_SplitsAControllerAtZero(float value, float left, float right) =>
        Weights(Rule(0, Op(TwoWay0, 1)), Rule(1, Op(TwoWay1, 1)), src: [0f, value, 0f, 0f]).ShouldBe([left, right]);

    [Test]
    public void Run_Combo_MultipliesTheLastN() =>
        Weights(Rule(0, C(9f), C(0.5f), C(0.5f), C(0.5f), Op(Combo, 3))).ShouldBe([9f, 0f]);

    [Test]
    public void Run_ComboOfThree_LeavesTheProductInSlotZero() =>
        Weights(Rule(0, C(0.5f), C(0.5f), C(0.5f), Op(Combo, 3))).ShouldBe([0.125f, 0f]);

    [Test]
    public void Run_Dominate_SuppressesTheValueBelowByTheProduct()
    {
        // stack: 0.8 | 0.5 0.5 ; dominate 2 → 0.8 * (1 - 0.25) = 0.6.
        Weights(Rule(0, C(0.8f), C(0.5f), C(0.5f), Op(Dominate, 2))).ShouldBe([0.6f, 0f]);
    }

    [TestCase(0.1f, 0f)]
    [TestCase(0.3f, 0.5f)]
    [TestCase(0.5f, 1f)]
    [TestCase(0.75f, 0.25f)]
    [TestCase(0.9f, 0f)]
    public void Run_NWay_RampsUpHoldsAndRampsDownThenScalesByTheValueController(float value, float expected)
    {
        // Filter 0.2/0.4/0.6/0.8 on controller 3's value, times controller 0 (one here; halved below).
        float[] src = [1f, 0f, 0f, value];

        Weights(Rule(0, C(0.2f), C(0.4f), C(0.6f), C(0.8f), C(3f), Op(NWay, 0)), src: src)[0]
            .ShouldBe(expected, 1e-6f);
    }

    [Test]
    public void Run_NWay_ScalesByTheMultiplierController() =>
        Weights(Rule(0, C(0.2f), C(0.4f), C(0.6f), C(0.8f), C(3f), Op(NWay, 0)), src: [0.5f, 0f, 0f, 0.5f])[0]
            .ShouldBe(0.5f);

    [TestCase(LowerLid, 0f, 0.5f)]
    [TestCase(LowerLid, 22.5f, 0.25f)]
    [TestCase(UpperLid, 0f, 0.5f)]
    [TestCase(UpperLid, -22.5f, 0.25f)]
    public void Run_DmeEyelid_IsCloseLidAgainstCloseLidVAndThePitch(int op, float updown, float expected)
    {
        // CloseLid (controller 3) 1, CloseLidV (controller 1, range -1..1) 0 → 0.5 normalised; blink slot 0 unread.
        // Lower: (1 - 0.5) * 1 = 0.5, times (1 - 0.5) when the eye looks up half way.
        // Upper: 0.5 * 1, times (1 - 0.5) when the eye looks down half way.
        float[] src = [0f, 0f, updown, 1f];

        Weights(Rule(0, C(2f), C(0f), C(3f), Op(op, 1)), src: src)[0].ShouldBe(expected, 1e-6f);
    }

    [Test]
    public void Run_AnOpUnderflowingTheStack_IsANoOpAndTheRuleCarriesOn() =>
        Weights(Rule(0, Op(Add), C(0.75f))).ShouldBe([0.75f, 0f]);

    [Test]
    public void Run_AFetchOfAControllerThatDoesNotExist_IsANoOp() =>
        Weights(Rule(0, C(0.25f), Op(Fetch1, 9))).ShouldBe([0.25f, 0f]);

    [Test]
    public void Run_TheDescriptors_AreZeroedFirst()
    {
        float[] dest = [7f, 7f];
        StudioFlexRules.Run(Data([]), new float[4], dest);
        dest.ShouldBe([0f, 0f]);
    }

    [TestCase(-0.1f, 0f)]
    [TestCase(0f, 0f)]
    [TestCase(0.5f, 0.5f)]
    [TestCase(5f, 1f)]
    [TestCase(10.5f, 0.5f)]
    [TestCase(11f, 0f)]
    public void Ramp_AWeight_RisesHoldsAndFallsBetweenTheTargets(float weight, float expected) =>
        StudioFlexRules.Ramp(Flex(0, 0), weight).ShouldBe(expected, 1e-6f);

    [Test]
    public void Accumulate_AStereoFlex_MixesItsTwoDescriptorsBySide()
    {
        // Left descriptor 0 at 1, right descriptor 1 at 0. Side 0 takes the left, 255 the right, 51 a fifth of the way.
        StudioFlexData data = Data([], Flex(0, 1, Anim(0, side: 0), Anim(1, side: 255), Anim(2, side: 51)));
        float[] positions = new float[9];
        float[] normals = new float[9];
        float[] weights = [1f, 0f];

        StudioFlexRules.Accumulate(data, weights, weights, positions, normals).ShouldBe(3);

        positions[0].ShouldBe(2f);
        positions[3].ShouldBe(0f);
        positions[6].ShouldBe(1.6f, 1e-6f);
        normals[2].ShouldBe(-1f);
        normals[5].ShouldBe(0f);
    }

    [Test]
    public void Accumulate_ASlowVertex_TakesTheDelayedWeightBySpeed()
    {
        StudioFlexData data = Data([], Flex(0, 0, Anim(0, side: 0, speed: 0), Anim(1, side: 0, speed: 255)));
        float[] positions = new float[6];

        StudioFlexRules.Accumulate(data, [1f], [0.5f], positions, new float[6]);

        positions[0].ShouldBe(1f);
        positions[3].ShouldBe(2f);
    }

    [Test]
    public void Accumulate_AMonoFlex_IgnoresSide()
    {
        StudioFlexData data = Data([], Flex(0, 0, Anim(0, side: 200)));
        float[] positions = new float[3];

        StudioFlexRules.Accumulate(data, [0.5f], [0.5f], positions, new float[3]);

        positions[0].ShouldBe(1f);
    }

    [Test]
    public void Accumulate_AZeroWeight_TouchesNothing() =>
        StudioFlexRules.Accumulate(Data([], Flex(0, 0, Anim(0, 0))), [0f], [0f], new float[3], new float[3]).ShouldBe(0);

    [TestCase(0.0009f, 0)]
    [TestCase(-0.0009f, 0)]
    [TestCase(0.0011f, 1)]
    public void Accumulate_AWeightInsideTheDeadBand_IsSkipped(float weight, int expected) =>
        // R_StudioFlexVerts, studiorender 0x18001eb90: all four weights inside (-0.001, 0.001), compared as doubles.
        StudioFlexRules.Accumulate(Data([], Flex(0, 0, Anim(0, 0))), [weight], [weight], new float[3], new float[3])
            .ShouldBe(expected);

    [Test]
    public void Accumulate_SpeedAndSide_MixInTheEnginesOrder()
    {
        // w = ((1 - s) w2 + s w1)(1 - side) + ((1 - s) w4 + s w3) side, s = speed / 255, side = side / 255:
        // speed 51 (0.2), side 102 (0.4); w1 = 1, w2 = 0.5, w3 = 0, w4 = 0.25.
        // ((0.8 × 0.5) + (0.2 × 1)) × 0.6 + ((0.8 × 0.25) + 0) × 0.4 = 0.36 + 0.08 = 0.44; × delta 2.
        StudioFlexData data = Data([], Flex(0, 1, Anim(0, side: 102, speed: 51)));
        float[] positions = new float[3];

        StudioFlexRules.Accumulate(data, [1f, 0f], [0.5f, 0.25f], positions, new float[3]);

        positions[0].ShouldBe(0.88f, 1e-5f);
    }

    [Test]
    public void Resting_EachController_IsItsMinimum() =>
        StudioFlexRules.Resting(Data([])).ShouldBe([0f, -1f, -45f, 0f]);

    private static float[] Weights(params StudioFlexRule[] rules) => Weights(rules, src: new float[4]);

    private static float[] Weights(StudioFlexRule rule, float[] src) => Weights([rule], src);

    private static float[] Weights(StudioFlexRule first, StudioFlexRule second, float[] src) => Weights([first, second], src);

    private static float[] Weights(StudioFlexRule[] rules, float[] src)
    {
        float[] dest = new float[2];
        StudioFlexRules.Run(Data(rules), src, dest);
        return dest;
    }

    private static StudioFlexData Data(IReadOnlyList<StudioFlexRule> rules, params StudioMeshFlex[] flexes) =>
        new(["left", "right"], Four, rules, flexes);

    private static StudioFlexRule Rule(int flex, params StudioFlexOp[] ops) => new(flex, ops);

    private static StudioFlexOp C(float value) => new(Const, BitConverter.SingleToInt32Bits(value), value);

    private static StudioFlexOp Op(int op, int index = 0) => new(op, index, BitConverter.Int32BitsToSingle(index));

    private static StudioMeshFlex Flex(int desc, int pair, params StudioVertAnim[] anims) =>
        new(desc, pair, 0f, 1f, 10f, 11f, anims.ToArray());

    private static StudioVertAnim Anim(int vertex, byte side, byte speed = 255) =>
        new(vertex, speed, side, (2f, 0f, 0f), (0f, 0f, -1f));
}
