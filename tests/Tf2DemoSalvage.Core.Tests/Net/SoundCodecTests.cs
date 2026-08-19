using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The <c>svc_Sounds</c> codec, driven by sounds this test wrote rather than sounds a demo held.
/// </summary>
/// <remarks>
/// **Replaces <c>CorpusSoundTests</c> and <c>CorpusSoundRoundTripTests</c>, and reaches the thing
/// both of them named as their own blind spot.** Their remarks say it outright: four of the
/// protocol boundaries in Valve's <c>proto_version.h</c> are sound-related — the sound index width
/// at 22, the flags width at 18, the special DSP at 21 — and *the corpus holds nothing between
/// protocols 15 and 24*. Those tests could not exercise a single one of them, however many demos
/// were added, because no recording from that decade exists to add.
///
/// A sound can simply be written at protocol 18, or 21, or 22. That is the whole argument for
/// synthetic fixtures in one paragraph, and it is why these tests are not a weaker substitute for
/// the corpus ones — they answer a question the corpus is structurally unable to be asked.
///
/// **The width tests measure the BIT COUNT, not the values, and that is deliberate.** A sound whose
/// index is read at the wrong width still produces a number, and on a small index it produces the
/// *right* number — 13 bits and 14 bits agree on every value below 8192. Asserting the value is
/// therefore insensitive to the defect: correct and broken predict the same observation. What they
/// cannot agree on is how many bits it took, so the encoded length is the measurement, and a
/// one-bit difference between two adjacent protocols is a decisive prediction rather than a range
/// check.
///
/// What the corpus tests DID have that this cannot: real bodies produced by the engine. That
/// evidence is not lost, it moves — the CLI suite still round-trips whole demos, which is where an
/// end-to-end claim about real bits belongs.
/// </remarks>
public sealed class SoundCodecTests
{
    /// <summary>Protocol 24: the corpus's newest, and the modern width of every field.</summary>
    private const ushort Modern = 24;

    [Test]
    public void EveryFieldComesBackAsItWentIn()
    {
        // **Distinct values in every field, which is what a real demo could not arrange.** Sounds
        // as recorded are full of shared defaults — volume 1, pitch 100, channel 6 — and two
        // fields holding the same number look identical however they are transposed. These do not
        // collide, so a swapped pair cannot pass.
        DecodedSound sent = new(
            EntityIndex: 1337,
            SoundNumber: 4321,
            Flags: 0b0001_0000_1,
            Channel: 3,
            IsAmbient: true,
            IsSentence: false,
            SequenceNumber: 511,
            Volume: 64f / 127f,
            SoundLevel: 300,
            Pitch: 153,
            DelaySeconds: 0.4f,

            // Multiples of eight, because the wire quantises an origin to an eight-unit grid.
            // Choosing anything else would make this a test of rounding rather than of layout.
            OriginX: 1024f,
            OriginY: -2048f,
            OriginZ: 512f,
            SpeakerEntity: 42,
            SpecialDsp: 17,
            Sent: SoundFields.Entity | SoundFields.SoundNumber | SoundFields.Flags |
                SoundFields.Channel | SoundFields.SequenceExplicit | SoundFields.Volume |
                SoundFields.SoundLevel | SoundFields.Pitch | SoundFields.SpecialDsp |
                SoundFields.Delay | SoundFields.OriginX | SoundFields.OriginY |
                SoundFields.OriginZ | SoundFields.Speaker);

        DecodedSound read = RoundTrip(sent, Modern);

        read.EntityIndex.ShouldBe(1337);
        read.SoundNumber.ShouldBe(4321);
        read.Flags.ShouldBe(0b0001_0000_1);
        read.Channel.ShouldBe(3);
        read.IsAmbient.ShouldBeTrue();
        read.IsSentence.ShouldBeFalse();
        read.SequenceNumber.ShouldBe(511);
        read.Volume.ShouldBe(64f / 127f, 0.0001f);
        read.SoundLevel.ShouldBe(300);
        read.Pitch.ShouldBe(153);
        read.DelaySeconds.ShouldBe(0.4f, 0.002f);
        read.OriginX.ShouldBe(1024f);
        read.OriginY.ShouldBe(-2048f);
        read.OriginZ.ShouldBe(512f);
        read.SpeakerEntity.ShouldBe(42);
        read.SpecialDsp.ShouldBe(17);

        // Which fields were sent is part of the value, not bookkeeping: without it the sound
        // cannot be written back to the bits it came from. See `SoundFields`.
        read.Sent.ShouldBe(sent.Sent);
    }

    [Test]
    public void ANegativeOriginIsNotReadAsAHugePositiveOne()
    {
        // **The failure this guards is silent and was described in the decoder's own remarks.** A
        // twelve-bit coordinate without sign extension comes back near 4096, which scaled by eight
        // puts the sound sixteen thousand units outside the map — plausible arithmetic, impossible
        // position, no error anywhere.
        //
        // The corpus test could only bound this with `MathF.Abs(origin) <= 16384`, because with a
        // real demo nobody knows where the sound was. A range check is exactly what a wrong sign
        // slips through when the coordinate is small.
        DecodedSound read = RoundTrip(
            Sound(originX: -8f, originY: -16376f, originZ: -8f) with
            {
                Sent = SoundFields.OriginX | SoundFields.OriginY | SoundFields.OriginZ,
            },
            Modern);

        read.OriginX.ShouldBe(-8f);
        read.OriginY.ShouldBe(-16376f);
        read.OriginZ.ShouldBe(-8f);
    }

    [Test]
    public void TheSpeakerEntitysMinusOneSurvives()
    {
        // -1 is "no speaker", and it is the exact value an unextended sign loses: twelve bits of
        // ones reads back as 4095. The engine's own default is -1, so a decoder that dropped the
        // extension would agree with the default on the first sound and disagree on every one
        // that stated it.
        DecodedSound read = RoundTrip(
            Sound() with { SpeakerEntity = -1, Sent = SoundFields.Speaker }, Modern);

        read.SpeakerEntity.ShouldBe(-1);
    }

    [Test]
    public void ANegativeDelayTakesTheTenfoldBranch()
    {
        // The wire biases a delay by 100ms and expands anything still negative tenfold, so
        // precision is lost only on large skip-aheads. Both branches are one expression apart and
        // a demo contains whichever the server happened to send.
        RoundTrip(Delay(0.4f), Modern).DelaySeconds.ShouldBe(0.4f, 0.002f);
        RoundTrip(Delay(-1.1f), Modern).DelaySeconds.ShouldBe(-1.1f, 0.002f);

        // The boundary itself: exactly -0.1 biases to zero, which is neither branch.
        RoundTrip(Delay(-0.1f), Modern).DelaySeconds.ShouldBe(-0.1f, 0.002f);
    }

    [Test]
    public void AnOmittedFieldInheritsThePreviousSound()
    {
        // **The delta base, which is the one thing the corpus round trip explicitly could not
        // check.** Its remarks say so: the flag bits rather than the values decide how much is
        // read, so a wrong base consumes exactly the right number of bits and produces wrong
        // numbers. Bit-exact re-encoding passes either way.
        //
        // Two sounds settle it. The second sends nothing at all, so every value it reports has to
        // be the first one's — and a decoder deltaing against the engine defaults instead would
        // return pitch 100 and channel 6 here.
        DecodedSound first = Sound(pitch: 200, channel: 2) with
        {
            SoundNumber = 900,
            Sent = SoundFields.Pitch | SoundFields.Channel | SoundFields.SoundNumber,
        };

        IReadOnlyList<DecodedSound> read = RoundTrip([first, Sound() with { Sent = SoundFields.None }], Modern);

        read.Count.ShouldBe(2);
        read[1].Pitch.ShouldBe(200);
        read[1].Channel.ShouldBe(2);
        read[1].SoundNumber.ShouldBe(900);
        read[1].Sent.ShouldBe(SoundFields.None);
    }

    [Test]
    public void TheFirstSoundInAMessageDeltasAgainstTheEngineDefaults()
    {
        // The other half of the same question. A sound that sends nothing is not empty — it is
        // every engine default, and those are specific numbers rather than zeroes.
        DecodedSound read = RoundTrip(Sound() with { Sent = SoundFields.None }, Modern);

        read.Channel.ShouldBe(6);
        read.Volume.ShouldBe(1f);
        read.SoundLevel.ShouldBe(75);
        read.Pitch.ShouldBe(100);
        read.SpeakerEntity.ShouldBe(-1);
    }

    [Test]
    public void AStopSoundCarriesNothingAfterItsChannel()
    {
        // SND_STOP truncates the record. Reading the playback fields anyway would take bits
        // belonging to the next sound, so this is checked by what follows rather than by what the
        // stop itself reports: a second sound decodes correctly only if the first consumed the
        // right number of bits.
        DecodedSound stop = Sound() with
        {
            Flags = 4,
            EntityIndex = 7,
            Sent = SoundFields.Flags | SoundFields.Entity,
        };

        // **The flags have to be re-sent, and finding that out is the point of writing this by
        // hand.** Flags are delta-coded like every other field, so a sound following a stop that
        // says nothing about them INHERITS SND_STOP and is itself a stop — it carries no playback
        // fields at all, and its pitch and origin are whatever the stop's were. The first version
        // of this fixture omitted them and reported pitch 100 against an expected 111, which
        // looked like a decoder bug and is the format working correctly.
        //
        // That is a real property of the wire and it is pinned below, because a demo where a stop
        // is the last sound in its message would never show it.
        DecodedSound after = Sound(pitch: 111) with
        {
            Flags = 0,
            SoundNumber = 1234,
            Sent = SoundFields.Pitch | SoundFields.SoundNumber | SoundFields.Flags,
        };

        IReadOnlyList<DecodedSound> read = RoundTrip([stop, after], Modern);

        read[0].Flags.ShouldBe(4);
        read[0].EntityIndex.ShouldBe(7);

        // The decisive half. A stop that read the playback fields would leave the reader inside
        // this sound's bits and these two values would be noise.
        read[1].Pitch.ShouldBe(111);
        read[1].SoundNumber.ShouldBe(1234);
    }

    [Test]
    public void ASoundAfterAStopInheritsSndStopUnlessItSaysOtherwise()
    {
        // The consequence of flags being delta-coded, stated on its own because it is surprising
        // and because nothing else in the suite would fail if it changed. The second sound asks
        // for pitch 111 and does not mention flags, so it inherits SND_STOP, becomes a stop
        // itself, and never reaches the pitch field.
        IReadOnlyList<DecodedSound> read = RoundTrip(
            [
                Sound() with { Flags = 4, Sent = SoundFields.Flags },
                Sound(pitch: 111) with { Sent = SoundFields.Pitch },
            ],
            Modern);

        read[1].Flags.ShouldBe(4);
        read[1].Pitch.ShouldBe(100);

        // And it costs nothing to say so: a stop's record ends after the sentence bit, so the
        // second sound's pitch flag is not on the wire at all.
        read[1].Sent.ShouldBe(SoundFields.None);
    }

    [Test]
    public void TheSoundIndexIs13BitsThrough22And14BitsAfter()
    {
        // **The corpus has no demo between protocols 15 and 24, so this boundary was untestable
        // until a sound could be written rather than found.**
        //
        // Measured as a length rather than a value because the values agree: any index below 8192
        // decodes identically at either width, so an assertion on the number is insensitive to the
        // defect it exists to catch.
        //
        // **Measured WITHIN one protocol, and that correction came from a sabotage that this test
        // survived in its first form.** It compared the whole record at 22 against the whole
        // record at 23. Two boundaries sit between those protocols — the index at 22 and the
        // special DSP at 21 — so moving both by one made the index lose a bit while the DSP flag
        // gained one, and the difference stayed at 1. The test passed against a decoder with both
        // boundaries wrong.
        //
        // Subtracting two encodings at the SAME protocol cancels everything the field is not.
        Width(SoundFields.SoundNumber, protocol: 22).ShouldBe(13);
        Width(SoundFields.SoundNumber, protocol: 23).ShouldBe(14);
    }

    [Test]
    public void TheFlagsFieldIs9BitsThrough18And11BitsAfter()
    {
        // Nine to eleven, so a two-bit step. Isolated within each protocol for the reason spelled
        // out above: a cross-protocol difference cannot tell this field from any other that moves
        // at the same time.
        Width(SoundFields.Flags, protocol: 18).ShouldBe(9);
        Width(SoundFields.Flags, protocol: 19).ShouldBe(11);
    }

    [Test]
    public void TheSpecialDspFieldIsEntirelyAbsentThrough21()
    {
        // The subtle one. Below the boundary the DSP value is not merely omitted — *the bit that
        // would say whether it follows* is not on the wire either. A decoder that kept the flag
        // and skipped the value would consume one bit too many and shift the delay and the three
        // origins after it, which is the characteristic way this format fails: no exception, a
        // sound in the wrong place.
        //
        // Zero is the prediction, and it is a different claim from "narrower": asking for the
        // field at protocol 21 has to cost nothing at all, flag bit included. Every other field
        // measured this way costs at least its flag, so zero cannot be reached by accident.
        Width(SoundFields.SpecialDsp, protocol: 21).ShouldBe(0);
        Width(SoundFields.SpecialDsp, protocol: 22).ShouldBe(8);
    }

    [Test]
    public void ADspValueIsDroppedEntirelyBelowProtocol22()
    {
        // The value side of the same boundary, and it is asymmetric on purpose: a sound asking to
        // send a DSP at protocol 21 has no way to, so what comes back must report that it did
        // not. Silently writing it would desynchronise every sound after it.
        DecodedSound asked = Sound() with { SpecialDsp = 9, Sent = SoundFields.SpecialDsp };

        DecodedSound old = RoundTrip(asked, protocol: 21);
        old.SpecialDsp.ShouldBe(0);
        old.Sent.ShouldBe(SoundFields.None);

        DecodedSound modern = RoundTrip(asked, protocol: 22);
        modern.SpecialDsp.ShouldBe(9);
        modern.Sent.ShouldBe(SoundFields.SpecialDsp);
    }

    [Test]
    public void TheNarrowEntityFormIsRecordedRatherThanInferred()
    {
        // An entity index below 32 fits the five-bit form, and nothing forces a sender to use it.
        // Both forms decode to 7; only the length tells them apart, which is exactly why the
        // chosen form has to be carried on the decoded sound rather than re-derived from the
        // value. Re-deriving it is the mistake that cost twelve bits per occurrence on the corpus.
        DecodedSound wide = Sound() with { EntityIndex = 7, Sent = SoundFields.Entity };
        DecodedSound narrow = wide with { Sent = SoundFields.Entity | SoundFields.EntityNarrow };

        RoundTrip(wide, Modern).EntityIndex.ShouldBe(7);
        RoundTrip(narrow, Modern).EntityIndex.ShouldBe(7);

        EncodedBits(wide, Modern).ShouldBe(EncodedBits(narrow, Modern) + 6);

        RoundTrip(narrow, Modern).Sent.ShouldBe(SoundFields.Entity | SoundFields.EntityNarrow);
        RoundTrip(wide, Modern).Sent.ShouldBe(SoundFields.Entity);
    }

    [Test]
    public void ASequenceNumberSentAsAnIncrementIsNotConfusedWithOneSentInFull()
    {
        // Three-way, and the sense of the first bit is inverted from every other flag here: a SET
        // bit means unchanged. The increment form and an explicit value one higher decode to the
        // same number and occupy different bits, so the value alone cannot distinguish them.
        DecodedSound first = Sound() with
        {
            SequenceNumber = 40,
            Sent = SoundFields.SequenceExplicit,
        };

        IReadOnlyList<DecodedSound> incremented = RoundTrip(
            [first, Sound() with { Sent = SoundFields.SequenceIncrement }], Modern);

        incremented[1].SequenceNumber.ShouldBe(41);
        incremented[1].Sent.ShouldBe(SoundFields.SequenceIncrement);

        IReadOnlyList<DecodedSound> unchanged = RoundTrip(
            [first, Sound() with { Sent = SoundFields.None }], Modern);

        unchanged[1].SequenceNumber.ShouldBe(40);

        // The increment form costs a bit more than saying nothing, which is the observable
        // difference between the two branches.
        EncodedBits(Sound() with { Sent = SoundFields.SequenceIncrement }, Modern)
            .ShouldBe(EncodedBits(Sound() with { Sent = SoundFields.None }, Modern) + 1);
    }

    [Test]
    public void SoundsSurviveAWholeDemo()
    {
        // **The output-level assertion.** Everything above tests the codec when called with the
        // values the test chose; this is the only one here that fails if nothing production-side
        // calls it, or calls it with the wrong protocol. The message goes through the encoder, the
        // packet framing, the demo header and back out through the reader.
        DecodedSound sound = Sound(pitch: 120) with
        {
            EntityIndex = 55,
            SoundNumber = 700,
            Sent = SoundFields.Entity | SoundFields.SoundNumber | SoundFields.Pitch,
        };

        (byte[] body, int bits) = SoundEncoder.Encode([sound], Modern);

        byte[] demo = SyntheticDemo.Containing(
            Modern, new SoundsMessage(IsReliable: false, Count: 1, BodyBits: bits, Body: body));

        SoundsMessage read = Single<SoundsMessage>(demo);
        read.Count.ShouldBe(1);
        read.BodyBits.ShouldBe(bits);

        DecodedSound decoded = SoundDecoder
            .Decode(read.Body.Span, read.Count, read.BodyBits, Modern)
            .ShouldHaveSingleItem();

        decoded.EntityIndex.ShouldBe(55);
        decoded.SoundNumber.ShouldBe(700);
        decoded.Pitch.ShouldBe(120);
    }

    [Test]
    public void AReliableSoundsMessageKeepsItsNarrowerLengthField()
    {
        // The reliable flag changes two fields at once: a reliable message implies a single sound
        // and shrinks its length field from sixteen bits to eight. Reading one shape for the other
        // desynchronises the rest of the packet, so both are put through a demo rather than
        // asserted on the header alone.
        (byte[] body, int bits) = SoundEncoder.Encode(
            [Sound() with { Sent = SoundFields.Pitch }], Modern);

        SoundsMessage reliable = Single<SoundsMessage>(SyntheticDemo.Containing(
            Modern, new SoundsMessage(IsReliable: true, Count: 1, BodyBits: bits, Body: body)));

        reliable.IsReliable.ShouldBeTrue();
        reliable.Count.ShouldBe(1);
        reliable.BodyBits.ShouldBe(bits);

        // A reliable body has eight bits to state its length in, so this fixture is only
        // meaningful while it fits. Asserted rather than assumed: a wider sound would silently
        // make the test measure truncation instead.
        bits.ShouldBeLessThan(256);
    }

    /// <summary>A sound carrying the engine's own defaults, overridden per test.</summary>
    private static DecodedSound Sound(
        int pitch = 100, int channel = 6, float originX = 0f, float originY = 0f,
        float originZ = 0f) =>
        SoundDefaults with
        {
            Pitch = pitch,
            Channel = channel,
            OriginX = originX,
            OriginY = originY,
            OriginZ = originZ,
        };

    /// <summary>A sound sending only a delay, which is the field with two branches.</summary>
    private static DecodedSound Delay(float seconds) =>
        SoundDefaults with { DelaySeconds = seconds, Sent = SoundFields.Delay };

    /// <summary>
    /// The engine defaults, restated here rather than read from <c>SoundDecoder.Default</c>.
    /// </summary>
    /// <remarks>
    /// **Deliberately a second copy.** Building the fixture out of the value under test would make
    /// <see cref="TheFirstSoundInAMessageDeltasAgainstTheEngineDefaults"/> tautological: change
    /// the decoder's default channel to 3 and both the expectation and the observation move
    /// together. These numbers come from <c>soundinfo.h</c>, so the two agreeing is a finding.
    /// </remarks>
    private static DecodedSound SoundDefaults => new(
        EntityIndex: 0,
        SoundNumber: 0,
        Flags: 0,
        Channel: 6,
        IsAmbient: false,
        IsSentence: false,
        SequenceNumber: 0,
        Volume: 1f,
        SoundLevel: 75,
        Pitch: 100,
        DelaySeconds: 0f,
        OriginX: 0f,
        OriginY: 0f,
        OriginZ: 0f,
        SpeakerEntity: -1);

    private static DecodedSound RoundTrip(DecodedSound sound, ushort protocol) =>
        RoundTrip([sound], protocol)[0];

    private static IReadOnlyList<DecodedSound> RoundTrip(
        IReadOnlyList<DecodedSound> sounds, ushort protocol)
    {
        (byte[] body, int bits) = SoundEncoder.Encode(sounds, protocol);
        return SoundDecoder.Decode(body, sounds.Count, bits, protocol);
    }

    private static int EncodedBits(DecodedSound sound, ushort protocol) =>
        SoundEncoder.Encode([sound], protocol).BitCount;

    /// <summary>
    /// How many bits one field's VALUE occupies at a protocol, its flag bit excluded.
    /// </summary>
    /// <param name="field">The field to send.</param>
    /// <param name="protocol">The protocol to encode at.</param>
    /// <returns>
    /// The value's width, or <c>0</c> when the field is absent from the wire entirely at this
    /// protocol. No field is zero bits wide, so the two cannot be confused.
    /// </returns>
    /// <remarks>
    /// **Both encodings are at the SAME protocol, which is the entire point.** A width measured by
    /// differencing two protocols cannot tell the field under test from any other field whose
    /// width or presence changes at the same boundary, and this suite had that bug: a sabotage
    /// that moved the sound-index boundary and the DSP boundary together left the cross-protocol
    /// difference unchanged and the test green. Subtracting within one protocol cancels every
    /// field that is not this one, whatever the decoder believes about the boundaries.
    ///
    /// The flag bit needs no correction because it is written in both encodings — a field that is
    /// not sent still costs its <c>false</c>. It is exactly when that is NOT true, at a protocol
    /// where the field does not exist, that the difference falls to zero.
    /// </remarks>
    private static int Width(SoundFields field, ushort protocol) =>
        EncodedBits(Sound() with { Sent = field }, protocol) -
        EncodedBits(Sound() with { Sent = SoundFields.None }, protocol);

    /// <summary>The single message of a kind a synthetic demo carries.</summary>
    private static TMessage Single<TMessage>(byte[] demo)
        where TMessage : INetMessage
    {
        List<TMessage> found = [];
        foreach (INetMessage message in SyntheticDemo.MessagesIn(demo))
        {
            if (message is TMessage wanted)
            {
                found.Add(wanted);
            }
        }

        return found.ShouldHaveSingleItem();
    }
}
