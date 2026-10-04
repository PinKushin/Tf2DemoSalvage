using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Which IK rules one sequence hands the solver, and at what weight — `Studio_IKSequenceError` feeding `AddDependencies`.
/// </summary>
/// <remarks>
/// **The rule's envelope is start 0.2, peak 0.4, tail 0.6, end 0.8** over a 31-frame animation, so
/// `Studio_IKRuleWeight` (`bone_setup.cpp:2875`) answers 1 on the plateau, <c>SimpleSpline(0.5) = 0.5</c> at cycle 0.3, and
/// 0 before 0.2. The weight handed on is that envelope times the sequence's own influence (`SolveDependencies`,
/// `bone_setup.cpp:4103`), and a rule at more than 0.999 clears its chain, a RELEASE then adding nothing
/// (`AddDependencies`, `bone_setup.cpp:3319`).
///
/// Built from bytes because that is what production reads: `IkRules` and `StudioIkRules.Error` both open the model.
/// </remarks>
public sealed class IkRuleDependencyTests
{
    [Test]
    public void IkFor_AReleaseAtFullStrength_ClearsItsChainAndAddsNothing()
    {
        List<(StudioIkRule, Vector3, Quaternion, float)> errors = [Held(chain: 0), Held(chain: 1)];

        EntityModelSet.IkFor(Model(StudioIkRuleType.Release), 0, 0.5f, 1f, [], errors);

        errors.Count.ShouldBe(1, "the release cleared chain 0 and was not added itself");
        errors[0].Item1.Chain.ShouldBe(1);
    }

    [Test]
    public void IkFor_AReleaseOnItsRamp_IsAddedAtTheSplinedWeight()
    {
        List<(StudioIkRule, Vector3, Quaternion, float)> errors = [Held(chain: 0)];

        EntityModelSet.IkFor(Model(StudioIkRuleType.Release), 0, 0.3f, 1f, [], errors);

        errors.Count.ShouldBe(2, "at half strength nothing is cleared");
        errors[1].Item1.Type.ShouldBe(StudioIkRuleType.Release);
        errors[1].Item4.ShouldBe(0.5f, 1e-4f);
        errors[1].Item3.ShouldBe(Quaternion.Identity, "a release carries no error track");
        errors[1].Item2.ShouldBe(Vector3.Zero);
    }

    [Test]
    public void IkFor_ALayerAtHalfInfluence_AsksForHalfAndClearsNothing()
    {
        List<(StudioIkRule, Vector3, Quaternion, float)> errors = [Held(chain: 0)];

        EntityModelSet.IkFor(Model(StudioIkRuleType.Release), 0, 0.5f, 0.5f, [], errors);

        errors.Count.ShouldBe(2);
        errors[1].Item4.ShouldBe(0.5f, 1e-4f);
    }

    [Test]
    public void IkFor_AnUnlatchAtFullStrength_ClearsNothingAndIsAdded()
    {
        List<(StudioIkRule, Vector3, Quaternion, float)> errors = [Held(chain: 0)];

        EntityModelSet.IkFor(Model(StudioIkRuleType.Unlatch), 0, 0.5f, 1f, [], errors);

        errors.Count.ShouldBe(2, "`if ( ikrule.type != IK_UNLATCH)` guards the clear");
        errors[1].Item4.ShouldBe(1f, 1e-4f);
    }

    [TestCase(0.1f, 1f)]
    [TestCase(0.5f, 0f)]
    public void IkFor_BeforeTheEnvelopeOrAtZeroInfluence_AddsNothing(float cycle, float influence)
    {
        List<(StudioIkRule, Vector3, Quaternion, float)> errors = [];

        EntityModelSet.IkFor(Model(StudioIkRuleType.Unlatch), 0, cycle, influence, [], errors);

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// A SELF rule reads its error track: a constant (3, 4, 5) with no rotation, read at frame (0.5 − 0.2) × 30 = 9.
    /// </summary>
    [Test]
    public void IkFor_ASelfRuleOnThePlateau_CarriesItsErrorAtFullWeight()
    {
        List<(StudioIkRule, Vector3, Quaternion, float)> errors = [Held(chain: 0)];

        EntityModelSet.IkFor(Model(StudioIkRuleType.Self), 0, 0.5f, 1f, [], errors);

        errors.Count.ShouldBe(1, "a full-strength self replaces what its chain held");
        errors[0].Item1.Type.ShouldBe(StudioIkRuleType.Self);
        errors[0].Item2.X.ShouldBe(3f, 1e-4f);
        errors[0].Item2.Y.ShouldBe(4f, 1e-4f);
        errors[0].Item2.Z.ShouldBe(5f, 1e-4f);
        errors[0].Item3.W.ShouldBe(1f, 1e-4f);
        errors[0].Item4.ShouldBe(1f, 1e-4f);
    }

    private static (StudioIkRule, Vector3, Quaternion, float) Held(int chain) =>
        (new StudioIkRule(
            Type: StudioIkRuleType.Self, Chain: chain, Bone: 0, Slot: 0, Height: 0f, Radius: 0f, Floor: 0f,
            Position: (0f, 0f, 0f), Rotation: (0f, 0f, 0f, 1f), CompressedError: 0, FirstFrame: 0, ErrorIndex: 0,
            Start: 0f, Peak: 0f, Tail: 1f, End: 1f, Contact: 0f, Drop: 0f, Top: 0f, AttachmentName: 0),
         Vector3.Zero, Quaternion.Identity, 0.5f);

    /// <summary>A one-bone model with one IK chain and one animation declaring one rule of a type.</summary>
    private static PropModels.SkinnedModel Model(int type) =>
        SyntheticSkinnedModel.WithBones("root") with { Models = [Bytes(type)] };

    /// <summary>The <c>.mdl</c>: header, one 31-frame animation, one chain, one rule, and a constant error track.</summary>
    private static byte[] Bytes(int type)
    {
        const int Animation = 512;
        const int Chain = Animation + 100;
        const int Rule = Chain + 16;
        const int Error = Rule + 152;
        const int Track = Error + 36;

        byte[] file = new byte[Track + 64];
        Span<byte> span = file;

        BitConverter.TryWriteBytes(span[180..], 1);
        BitConverter.TryWriteBytes(span[184..], Animation);
        BitConverter.TryWriteBytes(span[284..], 1);
        BitConverter.TryWriteBytes(span[288..], Chain);

        BitConverter.TryWriteBytes(span[(Animation + 8)..], 30f);
        BitConverter.TryWriteBytes(span[(Animation + 16)..], 31);
        BitConverter.TryWriteBytes(span[(Animation + 60)..], 1);
        BitConverter.TryWriteBytes(span[(Animation + 64)..], Rule - Animation);

        BitConverter.TryWriteBytes(span[(Rule + 4)..], type);
        BitConverter.TryWriteBytes(span[(Rule + 60)..], Error - Rule);
        BitConverter.TryWriteBytes(span[(Rule + 76)..], 0.2f);
        BitConverter.TryWriteBytes(span[(Rule + 80)..], 0.4f);
        BitConverter.TryWriteBytes(span[(Rule + 84)..], 0.6f);
        BitConverter.TryWriteBytes(span[(Rule + 88)..], 0.8f);

        // mstudiocompressedikerror_t: six scales, then six offsets from its own start. X, Y, Z constant 3, 4, 5;
        // the three angles have no track and read zero.
        for (int channel = 0; channel < 3; channel++)
        {
            int at = Track + (channel * 4);

            BitConverter.TryWriteBytes(span[(Error + (channel * 4))..], 1f);
            BitConverter.TryWriteBytes(span[(Error + 24 + (channel * 2))..], (short)(at - Error));

            // mstudioanimvalue_t: one valid value covering every frame.
            span[at] = 1;
            span[at + 1] = 255;
            BitConverter.TryWriteBytes(span[(at + 2)..], (short)(3 + channel));
        }

        return file;
    }
}
