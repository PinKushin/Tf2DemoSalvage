using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Reading a compiled scene archive, and finding the sequence a taunt plays (B351).
/// </summary>
/// <remarks>
/// **Synthetic, and the fixture is written the way `CChoreoScene::SaveToBinaryBuffer` writes**
/// (<c>choreoscene.cpp:3702</c>): the top-level event list carries only the events with NO actor,
/// and everything belonging to an actor lives under actor → channel → event. A taunt's gesture is
/// an actor's, so a reader that stops at the top-level list finds a `LOOP` and gives up — which is
/// exactly what a real `taunt_hi5_start.vcd` did before this test existed.
///
/// The fixture HAS ground truth because this file put the sequence name there (D38). The one value
/// stored twice is the directory's CRC: <see cref="TauntCrc"/> is written into the fixture by hand
/// and found by production's own hashing, so the two must agree.
/// </remarks>
[TestFixture]
public sealed class SceneImageTests
{
    /// <summary><c>Crc32("scenes\test\taunt.vcd")</c>, read back little-endian.</summary>
    private const uint TauntCrc = 0xF86749A5;

    /// <summary><c>MAKEID('V','S','I','F')</c>.</summary>
    private const uint SceneImageId = 0x46495356;

    /// <summary><c>MAKEID('b','v','c','d')</c>.</summary>
    private const uint SceneTag = 0x64637662;

    /// <summary>Pool slot of the name the gesture's parameter carries.</summary>
    private const short SequenceSlot = 3;

    /// <summary>What the fixture's gesture names, and what the reader must return.</summary>
    private const string Sequence = "taunt_hi5_start";

    /// <summary>The strings the fixture pools, index 0 empty as the compiler's pool starts.</summary>
    private static string[] Pool => ["", "loop_event", "hi5", Sequence];

    [Test]
    public void SequenceFor_AGestureUnderAnActor_IsFoundPastTheActorlessEvents()
    {
        // **The manipulation: a scene shaped like a real taunt.** One actor-less `LOOP` first —
        // which is all a `taunt_hi5_start` has at the top level — then the actor whose channel
        // holds the gesture.
        byte[] image = Image(TauntCrc, Scene(
            loose: [Loop(name: 1, loopCount: 3)],
            channelled: [Gesture(name: 2, parameters: SequenceSlot)]));

        SceneImage archive = SceneImage.Read(image).ShouldNotBeNull();

        archive.Count.ShouldBe(1);
        archive.CrcAt(0).ShouldBe(TauntCrc);

        // Production's own normalisation and hashing must land on the CRC written above.
        archive.SequenceFor("test/taunt").ShouldBe(Sequence);
    }

    [Test]
    public void SequenceFor_ASpeakBeforeTheGesture_StillReachesIt()
    {
        // **A `SPEAK` writes three extra fields after its flex tracks** — caption type, token and
        // flags (`choreoevent.cpp:4221`). Skipping them leaves the cursor four bytes inside the
        // next event, so the gesture that follows decodes as some other type and is lost.
        byte[] image = Image(TauntCrc, Scene(
            loose: [Speak(name: 1, caption: 2)],
            channelled: [Gesture(name: 2, parameters: SequenceSlot)]));

        SceneImage.Read(image).ShouldNotBeNull()
            .SequenceFor("test/taunt").ShouldBe(Sequence);
    }

    [Test]
    public void SequenceFor_AGestureCarryingFlexTracks_StillReachesTheNextEvent()
    {
        // **A flex sample is seven bytes, not five** — a float time, a byte value and an unsigned
        // short curve type (`choreoevent.cpp:4419`). Two bytes short per sample walks the cursor
        // into the middle of whatever follows, and the error grows with the sample count.
        byte[] image = Image(TauntCrc, Scene(
            loose: [Flexed(name: 1, samples: 5)],
            channelled: [Gesture(name: 2, parameters: SequenceSlot)]));

        SceneImage.Read(image).ShouldNotBeNull()
            .SequenceFor("test/taunt").ShouldBe(Sequence);
    }

    [Test]
    public void SequenceFor_ASceneWithNoGesture_IsNull()
    {
        // The control for every assertion above: a reader that returned something for everything
        // would pass them all.
        byte[] image = Image(TauntCrc, Scene(
            loose: [Loop(name: 1, loopCount: 1)],
            channelled: []));

        SceneImage.Read(image).ShouldNotBeNull().SequenceFor("test/taunt").ShouldBeNull();
    }

    [Test]
    public void SequenceFor_ANameNotInTheDirectory_IsNull()
    {
        byte[] image = Image(TauntCrc, Scene(
            loose: [],
            channelled: [Gesture(name: 2, parameters: SequenceSlot)]));

        SceneImage.Read(image).ShouldNotBeNull().SequenceFor("test/missing").ShouldBeNull();
    }

    [Test]
    public void EventsFor_ASceneWithThreeEvents_CarriesEachTypeAndTime()
    {
        // **Playback needs the times, not one name** — the engine runs the events on the scene's own
        // clock, so a scene staging two gestures cannot be reduced to whichever came first.
        byte[] image = Image(TauntCrc, Scene(
            loose: [Loop(name: 1, loopCount: 2), Speak(name: 1, caption: 2)],
            channelled: [Gesture(name: 2, parameters: SequenceSlot)]));

        IReadOnlyList<SceneEvent> events =
            SceneImage.Read(image).ShouldNotBeNull().EventsFor("test/taunt");

        events.Count.ShouldBe(3);

        // The actor-less events first, in the order the compiler wrote them, then the channel's.
        events[0].Type.ShouldBe<byte>(12);
        events[1].Type.ShouldBe<byte>(5);
        events[2].Type.ShouldBe<byte>(6);

        events[2].Start.ShouldBe(0.5f);
        events[2].End.ShouldBe(2.5f);
        events[2].Parameters.ShouldBe(Sequence);

        // Each offset must be past the one before it: an event read from the wrong place is the
        // failure this whole walk exists to avoid, and a repeated offset is what that looks like.
        events[1].At.ShouldBeGreaterThan(events[0].At);
        events[2].At.ShouldBeGreaterThan(events[1].At);
    }

    [Test]
    public void SequenceAt_ASceneCutShort_ReportsAnIncompleteWalk()
    {
        // **A null sequence and an incomplete walk are different findings.** Collapsing them is how
        // the actor tree stayed missing, so the truncated case has to be distinguishable from the
        // scene that simply plays no gesture.
        byte[] whole = Image(TauntCrc, Scene(
            loose: [],
            channelled: [Gesture(name: 2, parameters: SequenceSlot)]));

        SceneImage.Read(whole).ShouldNotBeNull().SequenceAt(0).Complete.ShouldBeTrue();

        // The same image with eight bytes lopped off the scene body, which the directory still
        // claims are there.
        byte[] cut = Image(TauntCrc, Scene(
            loose: [],
            channelled: [Gesture(name: 2, parameters: SequenceSlot)])[..^8]);

        SceneWalk walk = SceneImage.Read(cut).ShouldNotBeNull().SequenceAt(0);

        walk.Complete.ShouldBeFalse();
        walk.Length.ShouldBeGreaterThan(0);
    }

    [Test]
    public void Read_AnImageThatIsNotVsif_IsNull()
    {
        byte[] image = Image(TauntCrc, Scene(loose: [], channelled: []));

        BinaryPrimitives.WriteUInt32LittleEndian(image, 0xDEADBEEF);

        SceneImage.Read(image).ShouldBeNull();
    }

    /// <summary>A whole VSIF file around one scene body, laid out as the compiler lays it out.</summary>
    /// <remarks>
    /// Header, then the string offset table, the strings themselves, the CRC-sorted directory and
    /// the bodies. The offsets are absolute from the start of the file, which is what
    /// <c>CSceneImage::String</c> reads: <c>(char *)this + pTable[iString]</c>.
    /// </remarks>
    private static byte[] Image(uint crc, byte[] body)
    {
        string[] pool = Pool;

        int tableAt = 20;
        int stringsAt = tableAt + (pool.Length * 4);

        int[] offsets = new int[pool.Length];
        int running = stringsAt;

        for (int index = 0; index < pool.Length; index++)
        {
            offsets[index] = running;
            running += Encoding.UTF8.GetByteCount(pool[index]) + 1;
        }

        int directoryAt = running;
        int bodyAt = directoryAt + 16;

        byte[] bytes = new byte[bodyAt + body.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(bytes, SceneImageId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), pool.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), directoryAt);

        for (int index = 0; index < pool.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(tableAt + (index * 4)), offsets[index]);
            Encoding.UTF8.GetBytes(pool[index], bytes.AsSpan(offsets[index]));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(directoryAt), crc);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(directoryAt + 4), bodyAt);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(directoryAt + 8), body.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(directoryAt + 12), 0);

        body.CopyTo(bytes.AsSpan(bodyAt));

        return bytes;
    }

    /// <summary>A compiled scene: the actor-less events, then one actor with one channel.</summary>
    /// <param name="loose">Events belonging to no actor, which the compiler writes first.</param>
    /// <param name="channelled">Events belonging to the single actor's single channel.</param>
    private static byte[] Scene(IReadOnlyList<byte[]> loose, IReadOnlyList<byte[]> channelled)
    {
        List<byte> bytes = [];

        bytes.AddRange(BitConverter.GetBytes(SceneTag));
        bytes.Add(4);                                   // SCENE_BINARY_VERSION
        bytes.AddRange(BitConverter.GetBytes(0x1234u)); // the text version's CRC, which readers skip

        bytes.Add((byte)loose.Count);

        foreach (byte[] one in loose)
        {
            bytes.AddRange(one);
        }

        // One actor is enough: the failure being tested is not reaching the actors at all.
        bytes.Add(channelled.Count > 0 ? (byte)1 : (byte)0);

        if (channelled.Count > 0)
        {
            bytes.AddRange(BitConverter.GetBytes((short)2));   // the actor's name
            bytes.Add(1);                                      // one channel

            bytes.AddRange(BitConverter.GetBytes((short)2));   // the channel's name
            bytes.Add((byte)channelled.Count);

            foreach (byte[] one in channelled)
            {
                bytes.AddRange(one);
            }

            bytes.Add(1);                                      // the channel is active
            bytes.Add(1);                                      // the actor is active
        }

        // The scene ramp and the rest follow in a real file; nothing under test reads past here.
        bytes.Add(0);

        return [.. bytes];
    }

    /// <summary>An event's common part, up to and including the flex tracks.</summary>
    /// <param name="type">The <c>EVENTTYPE</c> (<c>choreoevent.h:257</c>).</param>
    /// <param name="name">Pool slot of the event's name.</param>
    /// <param name="parameters">Pool slot of the first parameter — the sequence, for a gesture.</param>
    /// <param name="flexSamples">Samples on a single flex track, or zero for no tracks at all.</param>
    private static List<byte> Common(byte type, short name, short parameters, int flexSamples)
    {
        List<byte> bytes =
        [
            type,
            .. BitConverter.GetBytes(name),
            .. BitConverter.GetBytes(0.5f),             // start time
            .. BitConverter.GetBytes(2.5f),             // end time
            .. BitConverter.GetBytes(parameters),
            .. BitConverter.GetBytes((short)0),         // parameters 2
            .. BitConverter.GetBytes((short)0),         // parameters 3
        ];

        // A ramp with two samples, so the reader's stride is exercised rather than skipped.
        bytes.Add(2);
        bytes.AddRange(BitConverter.GetBytes(0.0f));
        bytes.Add(0);
        bytes.AddRange(BitConverter.GetBytes(1.0f));
        bytes.Add(255);

        bytes.Add(0x08);                                // flags: active
        bytes.AddRange(BitConverter.GetBytes(0.0f));    // distance to target

        // One relative tag and one timing tag, then both absolute tag types with one each — every
        // stride the walk has to know, present rather than empty.
        bytes.Add(1);
        bytes.AddRange(BitConverter.GetBytes((short)1));
        bytes.Add(128);

        bytes.Add(1);
        bytes.AddRange(BitConverter.GetBytes((short)1));
        bytes.Add(64);

        for (int kind = 0; kind < 2; kind++)
        {
            bytes.Add(1);
            bytes.AddRange(BitConverter.GetBytes((short)1));
            bytes.AddRange(BitConverter.GetBytes((ushort)2048));
        }

        if (type == 6)
        {
            bytes.AddRange(BitConverter.GetBytes(1.75f));   // the gesture's own duration
        }

        bytes.Add(0);                                   // not using a relative tag

        if (flexSamples <= 0)
        {
            bytes.Add(0);                               // no flex tracks

            return bytes;
        }

        bytes.Add(1);                                   // one flex track
        bytes.AddRange(BitConverter.GetBytes((short)1));
        bytes.Add(0x01);                                // active, not a combo
        bytes.AddRange(BitConverter.GetBytes(0.0f));    // min
        bytes.AddRange(BitConverter.GetBytes(1.0f));    // max
        bytes.AddRange(BitConverter.GetBytes((short)flexSamples));

        for (int sample = 0; sample < flexSamples; sample++)
        {
            bytes.AddRange(BitConverter.GetBytes(sample * 0.1f));
            bytes.Add((byte)sample);
            bytes.AddRange(BitConverter.GetBytes((ushort)1));
        }

        return bytes;
    }

    /// <summary>A <c>GESTURE</c>, whose first parameter is the sequence name.</summary>
    private static byte[] Gesture(short name, short parameters) =>
        [.. Common(6, name, parameters, flexSamples: 0)];

    /// <summary>A <c>FLEXANIMATION</c> carrying tracks, to exercise the sample stride.</summary>
    private static byte[] Flexed(short name, int samples) =>
        [.. Common(10, name, 0, samples)];

    /// <summary>A <c>LOOP</c>, which writes its count after the flex tracks.</summary>
    private static byte[] Loop(short name, sbyte loopCount)
    {
        List<byte> bytes = Common(12, name, 0, flexSamples: 0);

        bytes.Add((byte)loopCount);

        return [.. bytes];
    }

    /// <summary>A <c>SPEAK</c>, which writes a caption type, token and flags after the tracks.</summary>
    private static byte[] Speak(short name, short caption)
    {
        List<byte> bytes = Common(5, name, 0, flexSamples: 0);

        bytes.Add(0);                                   // CC_MASTER
        bytes.AddRange(BitConverter.GetBytes(caption));
        bytes.Add(0);                                   // no combined file, no gender token

        return [.. bytes];
    }
}
