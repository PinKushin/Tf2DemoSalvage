using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests.Net;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The temp-entity codec, driven by effects this test wrote.
/// </summary>
/// <remarks>
/// **Two details of this layout are ones a guess gets wrong, and both are only observable at the
/// second effect in a burst** — which is why a corpus test over whatever effects a recording
/// happened to contain is a weak instrument for them:
///
/// * the class id is stored ONE HIGHER than it is, so a raw zero means "no class"
/// * an effect may omit its class and repeat the previous one, so a decoder treating each effect
///   independently desynchronises from the second onward
///
/// A written burst puts both under an assertion directly. It also reaches the reliable form, where
/// a count byte of zero means one effect rather than none — the engine spends the unused zero on
/// the case it can infer, and a decoder looping <c>count</c> times drops a real effect while
/// nothing reports a problem.
/// </remarks>
public sealed class TempEntityCodecTests
{
    [Test]
    public void RoundTrip_ABurstRepeatingItsClass_KeepsEveryEffectsClass()
    {
        // **The desynchronising case, and it needs at least two effects to exist.** The second
        // omits its class on the wire and inherits the first's; a decoder that read a class for
        // every effect would consume bits belonging to the delay and every effect after it would
        // be noise.
        IReadOnlyList<DecodedTempEntity> read = RoundTrip(
            Effect(classId: 7, delay: 0f),
            Effect(classId: 7, delay: 0f),
            Effect(classId: 9, delay: 0f));

        read.Select(effect => effect.ClassId).ShouldBe([7, 7, 9]);
    }

    [Test]
    public void RoundTrip_AClassIdOfZero_IsNotConfusedWithNoClass()
    {
        // The id is stored one higher precisely so that a raw zero can mean "no class followed".
        // Class 0 is therefore the value that breaks a decoder which skips the bias — it comes
        // back as whatever the previous effect was, or as -1.
        RoundTrip(Effect(classId: 0, delay: 0f)).ShouldHaveSingleItem().ClassId.ShouldBe(0);
    }

    [Test]
    public void RoundTrip_ADelay_IsHundredthsOfASecondRatherThanAFloat()
    {
        // Eight bits of hundredths, so 0.25 survives exactly and the quantisation is visible at
        // the third decimal. A decoder reading a float here would produce a plausible number from
        // the wrong bits, which is the characteristic failure of this format.
        RoundTrip(Effect(classId: 3, delay: 0.25f))
            .ShouldHaveSingleItem().DelaySeconds.ShouldBe(0.25f, 0.005f);

        // Zero takes a different branch — a single clear bit rather than a value — so it is not
        // the same case as a small delay.
        RoundTrip(Effect(classId: 3, delay: 0f))
            .ShouldHaveSingleItem().DelaySeconds.ShouldBe(0f);
    }

    [Test]
    public void Decode_ACountOfZero_MeansOneReliableEffectRatherThanNone()
    {
        // **The case that loses an effect silently.** A reliable message spends its count byte on
        // a zero because the count is always one, so a decoder looping `count` times reads
        // nothing, returns an empty list, and leaves the body unread with no error anywhere.
        EntityDecoder decoder = Decoder();

        byte[] body = decoder.EncodeTempEntities(
            [Effect(classId: 4, delay: 0f)], reliable: true, lengthBits: 0);

        IReadOnlyList<DecodedTempEntity> read =
            decoder.DecodeTempEntities(body, count: 0, lengthBits: body.Length * 8);

        read.ShouldHaveSingleItem().ClassId.ShouldBe(4);
    }

    [Test]
    public void Decode_ACountOfOne_IsTheUnreliableFormAndStillYieldsOneEffect()
    {
        // The control for the case above. Both forms carry one effect, so an assertion on the
        // count alone cannot tell them apart — what differs is how many bits were consumed, and
        // the way that shows is a second effect decoding correctly after it.
        EntityDecoder decoder = Decoder();

        byte[] body = decoder.EncodeTempEntities(
            [Effect(classId: 4, delay: 0f), Effect(classId: 5, delay: 0f)],
            reliable: false,
            lengthBits: 0);

        IReadOnlyList<DecodedTempEntity> read =
            decoder.DecodeTempEntities(body, count: 2, lengthBits: body.Length * 8);

        read.Select(effect => effect.ClassId).ShouldBe([4, 5]);
    }

    [Test]
    public void Decode_AnEffectRepeatingItsClass_IsADeltaOnThePreviousEffect()
    {
        // **An effect that omits its class is a delta against the effect before it**, not against zero: the
        // server writes it with `SendTable_WriteAllDeltaProps( lastEvent->pData, … )` and the client decodes it
        // onto the previous event's state. Measured on f12: a sentry's `TF_3rdPersonMuzzleFlash_SentryGun`
        // follows its `Tracer` and read entity 0 and attachment 0 — exactly the two fields the pair shares.
        DemoSchema schema = new(
            [new SendTable("DT_Test", true, [
                new SendProperty(SendPropType.Int, "m_iHealth", 1, string.Empty, 0f, 0f, 10, 0),
                new SendProperty(SendPropType.Int, "m_iTeamNum", 1, string.Empty, 0f, 0f, 3, 0)])],
            [.. Enumerable.Range(0, 4).Select(id => new Core.Net.ServerClass(id, $"CTest{id}", "DT_Test"))]);
        int classBits = EntityDecoder.ClassIdBits(schema.ServerClasses.Count);
        EntityDecoder decoder = new(schema, classBits);

        BitWriter writer = new();

        // Class 0, both fields; then class 0 again, only the team; then class 1, only the team.
        writer.Write(0, 1).Write(1, 1).Write(1, classBits)
            .Write(1, 1).UBitVar(0).Write(125, 10).Write(1, 1).UBitVar(0).Write(2, 3).Write(0, 1);
        writer.Write(0, 1).Write(0, 1)
            .Write(1, 1).UBitVar(1).Write(3, 3).Write(0, 1);
        writer.Write(0, 1).Write(1, 1).Write(2, classBits)
            .Write(1, 1).UBitVar(1).Write(1, 3).Write(0, 1);

        byte[] body = writer.Build();
        IReadOnlyList<DecodedTempEntity> read = decoder.DecodeTempEntities(body, 3, body.Length * 8);

        read[1].Properties.Select(Named).ShouldBe([("m_iTeamNum", 3L)], "the wire carries only the change");
        read[1].State.Select(Named).ShouldBe([("m_iHealth", 125L), ("m_iTeamNum", 3L)]);

        // The control: a new class is a full update against zero, and inherits nothing.
        read[2].State.Select(Named).ShouldBe([("m_iTeamNum", 1L)]);
    }

    private static (string Name, long Value) Named(DecodedProperty property) =>
        (property.Definition.Property.Name, property.Value.AsInt);

    /// <summary>Encodes effects and reads them back through the same decoder.</summary>
    private static IReadOnlyList<DecodedTempEntity> RoundTrip(params DecodedTempEntity[] effects)
    {
        EntityDecoder decoder = Decoder();
        byte[] body = decoder.EncodeTempEntities(effects, reliable: false, lengthBits: 0);

        return decoder.DecodeTempEntities(body, effects.Length, body.Length * 8);
    }

    /// <summary>A decoder whose class-id field is wide enough for the ids these tests use.</summary>
    /// <remarks>
    /// **The class field is sized by the schema's class COUNT, and that caught the first draft of
    /// this fixture.** `SyntheticPlayer`'s schema declares one class, so its id field is a single
    /// bit — and since the id is stored one higher than it is, the only value that fits is class
    /// 0. Every effect written with a larger id truncated to zero and came back as "carries no
    /// class and none preceded it".
    ///
    /// That is the format being consistent rather than a defect: a demo never references a class
    /// its own schema does not declare. The fixture has to declare them.
    /// </remarks>
    private static EntityDecoder Decoder()
    {
        DemoSchema schema = new(
            [new SendTable("DT_Effect", NeedsDecoder: true, [])],
            [.. Enumerable.Range(0, 32).Select(
                id => new Core.Net.ServerClass(id, $"CEffect{id}", "DT_Effect"))]);

        return new EntityDecoder(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
    }

    /// <summary>An effect carrying no properties, which is the shape this codec decides.</summary>
    /// <remarks>
    /// Properties are deliberately empty: they are decoded against the schema by the same path
    /// entities use, which <c>SyntheticTimelineTests</c> covers. What is unique to a temp entity
    /// is the class-repeat rule, the biased id and the delay, and a fixture carrying properties
    /// would make a failure in any of those look like a property bug.
    /// </remarks>
    private static DecodedTempEntity Effect(int classId, float delay) =>
        new(classId, delay, []);
}
