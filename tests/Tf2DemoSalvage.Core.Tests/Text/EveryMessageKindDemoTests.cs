using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// A demo carrying one of every writable message kind, decompiled to text and compiled back.
/// </summary>
/// <remarks>
/// **The synthetic counterpart to <c>CorpusAssemblyRoundTripTests</c>, and it reaches what that
/// one cannot: the kinds a recording does not happen to contain.** A demo holds whatever the
/// server sent. <c>svc_BspDecal</c>, <c>svc_GetCvarValue</c>, <c>svc_File</c> and
/// <c>svc_SetView</c> are all real messages with real encoders here, and a two-minute era specimen
/// may carry none of them — so their write paths sat unexecuted while the corpus round trip
/// reported a clean pass over twenty megabytes.
///
/// That is the shape of the gap this measures. <c>MessageAssembly</c> had 224 of 488 lines never
/// executed by <c>Core.Tests</c>, almost all of it the per-kind write and parse bodies, and the
/// corpus could only cover the kinds its ten demos held.
///
/// **The list cannot go stale, which is the other half of the design.** Rather than a hand-kept
/// set of kinds, <see cref="EveryKindTheWriterClaimsToSupportIsExercised"/> asks
/// <c>NetMessageWriter.CanWrite</c> what it accepts and fails when something here does not cover
/// it. A new encoder therefore breaks this test until a specimen is added — the same pattern as
/// <c>SdkCoverageTests</c>, where the denominator is generated rather than written down.
/// </remarks>
public sealed class EveryMessageKindDemoTests
{
    /// <summary>Protocol 24, where every conditional field takes its modern width.</summary>
    private const ushort Protocol = 24;

    [Test]
    public void EveryWritableKindSurvivesADemoIntact()
    {
        // The demo is built, written, read back, and each kind is found by type. This is the
        // output-level assertion for the writer: a kind whose encoder silently wrote nothing
        // would be missing here rather than merely untested.
        IReadOnlyList<INetMessage> read = SyntheticDemo.MessagesIn(Demo());

        // Values chosen to be distinctive, so a field read at the wrong offset shows as a wrong
        // number rather than as another zero.
        read.OfType<PrintMessage>().ShouldHaveSingleItem().Text.ShouldBe("a printed line");
        read.OfType<StringCmdMessage>().ShouldHaveSingleItem().Command.ShouldBe("echo hello");
        read.OfType<PrefetchMessage>().ShouldHaveSingleItem().SoundIndex.ShouldBe(1234);
        read.OfType<SetViewMessage>().ShouldHaveSingleItem().EntityIndex.ShouldBe(19);
        read.OfType<GetCvarValueMessage>().ShouldHaveSingleItem().CvarName.ShouldBe("cl_interp");

        NetTickMessage tick = read.OfType<NetTickMessage>().ShouldHaveSingleItem();
        tick.Tick.ShouldBe(123456);
        tick.HostFrameTimeRaw.ShouldBe((ushort)512);

        FixAngleMessage angle = read.OfType<FixAngleMessage>().ShouldHaveSingleItem();
        angle.IsRelative.ShouldBeTrue();

        // **A negative pitch, which is what a player looking up sends, and it comes back as 315
        // rather than -45.** That is the engine, not a defect: `bf_write::WriteBitAngle` casts to
        // a signed int and then masks (`tier1/bitbuf.cpp:551`), so a negative angle is stored as
        // its positive representative and reads back pointing the same direction. There is no
        // sign on the wire to recover.
        //
        // The value is asserted anyway because the failure it guards against is NOT wrapping. A
        // straight float-to-uint cast looks equivalent and saturates in .NET, turning every
        // negative angle into exactly 0 — a player looking up would be flattened to level. 315 and
        // 0 are both "not -45", and only one of them is right. The corpus round trip reported 100%
        // of payload bits reproduced while that bug was live, because a demo full of downward
        // angles never exercises it.
        angle.Pitch.ShouldBe(315f, 0.01f);
        angle.Yaw.ShouldBe(90f, 0.01f);

        FileMessage file = read.OfType<FileMessage>().ShouldHaveSingleItem();
        file.FileName.ShouldBe("maps/cp_process_final.bsp");
        file.IsRequested.ShouldBeTrue();

        SignOnStateMessage signon = read.OfType<SignOnStateMessage>().ShouldHaveSingleItem();
        signon.State.ShouldBe(5);
        signon.SpawnCount.ShouldBe(77);

        BspDecalMessage decal = read.OfType<BspDecalMessage>().ShouldHaveSingleItem();
        decal.OnEntity.ShouldBeTrue();
        decal.EntityIndex.ShouldBe(31);
        decal.TextureIndex.ShouldBe(9);

        VoiceInitMessage voice = read.OfType<VoiceInitMessage>().ShouldHaveSingleItem();
        voice.Codec.ShouldBe("vaudio_celt");

        // **The sample rate rides in the quality field's 255 escape, so it overwrites quality.**
        // That is the wire, not a defect: there is one byte, and 255 means "a sixteen-bit rate
        // follows" rather than "quality 255". Asserting quality 5 here would be asserting that a
        // field survives which the format does not send.
        //
        // Both are kept on the record for a reason the type's own remarks give: without the
        // separate `SampleRate`, "quality 22050" and "the escape followed by 22050" are the same
        // message and only one of them can be written back.
        voice.SampleRate.ShouldBe(22050);
        voice.Quality.ShouldBe(22050);

        ClassInfoMessage classes = read.OfType<ClassInfoMessage>().ShouldHaveSingleItem();
        classes.ClassCount.ShouldBe(3);
        classes.Classes.Select(entry => entry.ClassName)
            .ShouldBe(["CTFPlayer", "CWeaponMedigun", "CTFAmmoPack"]);

        SetConVarMessage convars = read.OfType<SetConVarMessage>().ShouldHaveSingleItem();
        convars.Variables.ShouldContain(
            new KeyValuePair<string, string>("sv_gravity", "800"));

        GameEventListMessage list = read.OfType<GameEventListMessage>().ShouldHaveSingleItem();
        list.Definitions.Select(definition => definition.Name)
            .ShouldBe(["player_death", "teamplay_round_win"]);

        GameEventMessage fired = read.OfType<GameEventMessage>().ShouldHaveSingleItem();
        fired.Name.ShouldBe("player_death");
        fired.Values["userid"].ShouldBe((short)12);
        fired.Values["weapon"].ShouldBe("scattergun");
        fired.Values["customkill"].ShouldBe((byte)7);
        fired.Values["crit"].ShouldBe(true);
    }

    [Test]
    public void EveryWritableKindCompilesBackToItsOwnBytes()
    {
        // **The criterion the Quake demo tools set**, applied to a demo built to hold every kind
        // rather than to whatever a server happened to send. Byte-exactness is the only claim
        // about a decode with a yes-or-no answer: a demo that compiles back to all but one byte
        // is a demo that cannot be played.
        //
        // The text is written by one code path, read by a second, and re-encoded by a third, so
        // nothing here is self-referential in the way a decoder checked against its own encoder
        // would be.
        //
        // **What it still cannot catch, MEASURED rather than assumed.** Swapping the entity index
        // and model index in `svc_BspDecal`'s encoder leaves this test green: a synthetic demo is
        // written by the same code that reads it, so a symmetric misreading round-trips perfectly.
        // The sibling test that asserts entity 31 and model 42 failed immediately.
        //
        // That is the whole argument for pairing the two, and it is why the round trip is not
        // sufficient on its own however satisfying byte-exactness feels. Against a REAL demo the
        // same swap would be caught here, because the engine wrote those bytes and did not agree
        // — which is the reason `CorpusAssemblyRoundTripTests` is kept rather than replaced.
        byte[] original = Demo();

        DemoHeader header = DemoHeader.Parse(original.AsSpan(0, DemoHeader.SizeBytes));
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(original.AsMemory(DemoHeader.SizeBytes))];

        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands);

        using StringReader reader = new(text.ToString());
        (DemoHeader compiledHeader, IReadOnlyList<DemoCommand> compiled) =
            DemoAssembly.Parse(reader);

        compiled.Count.ShouldBe(commands.Count);
        DemoWriter.Write(compiledHeader, compiled).ShouldBe(original);
    }

    [Test]
    public void ExactlyTheKnownKindsAreStillCarriedAsBits()
    {
        // **Measured from the OUTPUT, not from a predicate, which is the mistake this kind of
        // report has made here before.** Asking which types have a text form answers a different
        // question from asking which messages got one: a type can have a text form that declines
        // on every instance, and a report built from the predicate reads clean while 6.3 million
        // bits are still hex. The writer labels each raw line with what it stands for, so counting
        // the output cannot disagree with the output.
        //
        // **Asserted as an exact set rather than a permitted list**, so it is a ratchet in both
        // directions. A kind that starts falling back fails this; so does a kind that stops. Both
        // are things someone should have to notice — the second one silently is progress that
        // nobody records.
        //
        // This demo holds one of every writable kind, so the set below is complete rather than a
        // sample of whatever a server happened to send.
        HashSet<string> labels = new(StringComparer.Ordinal);

        foreach (string line in Assemble(Demo()).Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("raw ", StringComparison.Ordinal))
            {
                continue;
            }

            int marker = trimmed.IndexOf("# ", StringComparison.Ordinal);
            labels.Add(marker < 0 ? "unlabelled" : trimmed[(marker + 2)..]);
        }

        labels.OrderBy(label => label, StringComparer.Ordinal).ShouldBe(StillBits);
    }

    /// <summary>
    /// What the text assembly still carries as bits, exactly, on a demo holding every kind.
    /// </summary>
    /// <remarks>
    /// **PacketEntities is the interesting one.** It has a binary encoder — <c>CanWrite</c> accepts
    /// it — and no text form, which is exactly the gap this test exists to make visible: the
    /// message round-trips byte for byte while remaining unreadable. Bit-exactness and legibility
    /// are different properties and only one of them is finished here. A temp entity is the same
    /// shape: a private format inside a length prefix, needing its own decoder.
    ///
    /// **`padding` is deliberately NOT in this list, and its absence is a real limitation of the
    /// synthetic fixture rather than an omission.** A real packet ends with whatever bits were in
    /// the writer's buffer after its last message, and those are stale bits of an earlier write
    /// rather than zeroes — see <c>docs/memory/padding-is-not-zero.md</c>. A packet built here
    /// ends on a byte boundary with nothing left over, so no padding line is produced and this
    /// test cannot say anything about that path. <c>CorpusAssemblyRoundTripTests</c> is what
    /// covers it, and that is one of the reasons the corpus suite is narrowed rather than removed.
    ///
    /// The entity, user and voice bodies are absent for a duller reason: their text forms already
    /// carry the header and emit the payload inline, so they produce no separately-labelled raw
    /// line here.
    /// </remarks>
    private static readonly string[] StillBits =
    [
        "PacketEntities declined",
        "TempEntities declined",
    ];

    [Test]
    public void TraceNamesEveryMessageKind()
    {
        // **A different writer over the same demo, and it fails for reasons the assembly cannot.**
        // The trace is the readable artefact — the demo decompiled message by message in stream
        // order — and it is the only one a person reads. A message the assembly reproduces
        // perfectly can still be missing from the trace, or named wrongly there, and no
        // round-trip test would notice: they are separate code paths over the same input.
        //
        // This is the assertion the project's own rule asks for. A unit test proves a component
        // works when called with the values the test chose; only an assertion on the rendered
        // artefact can fail when production does not call it.
        // **These are the trace's own names, taken from the writer rather than guessed, and two
        // of them are not what the message is called anywhere else.** `svc_StringCmd` renders as
        // `svc_stufftext` and a chat line renders as `svc_chat` rather than as the
        // `svc_usermessage` it arrived in. Both are deliberate — the trace is written to read like
        // the engine's own vocabulary, not like this project's type names.
        //
        // Pinning the vocabulary is the point. This is the artefact a person reads, so a rename
        // in it should be a deliberate act rather than a side effect.
        string trace = Trace();

        foreach (string expected in new[]
        {
            "svc_serverinfo", "svc_print", "svc_stufftext", "net_signonstate",
            "svc_classinfo", "svc_prefetch", "svc_setview", "svc_fixangle", "svc_file",
            "svc_getcvarvalue", "svc_bspdecal", "svc_voiceinit", "svc_voicedata",
            "svc_entitymessage", "svc_tempentities", "svc_usermessage", "svc_sounds",
            "svc_packetentities", "svc_chat", "net_tick",
        })
        {
            // The subject goes in the message, so a failure says which kind is missing instead of
            // printing "trace should contain" and leaving the reader to open the file.
            //
            // Written as an explicit predicate rather than ShouldContain(text, message): Shouldly
            // binds that overload to its IEnumerable<char> form and the message is lost.
            Names(trace, expected).ShouldBeTrue($"the trace never names {expected}");
        }
    }

    /// <summary>Whether rendered output contains a token, for an assertion that names it.</summary>
    private static bool Names(string output, string token) =>
        output.Contains(token, StringComparison.Ordinal);

    [Test]
    public void TraceExpandsBodiesRatherThanNamingThem()
    {
        // Naming a message is the easy half. These are the parts the trace decodes INTO the line,
        // and each one is a place where a decode can be correct while nothing renders it — the
        // failure that shipped three no-ops here with a green suite.
        string trace = Trace();

        // A game event's fields, by name and value, not just its id.
        Names(trace, "player_death").ShouldBeTrue("the trace does not name the event that fired");
        Names(trace, "scattergun")
            .ShouldBeTrue("the trace does not expand a game event's string field");

        // Chat rendered as what was said, which is the whole reason chat is lifted out of
        // svc_UserMessage in the first place.
        Names(trace, "spah sappin mah sentry")
            .ShouldBeTrue("the trace does not render what was said in chat");

        // The map, from the header rather than from ServerInfo, so the two paths agree.
        Names(trace, "cp_process_final").ShouldBeTrue("the trace does not carry the map name");
    }

    [Test]
    public void TheDumpAndTheJsonLinesBothSurviveEveryKind()
    {
        // Neither of these has a round trip to protect it, so the only thing that can fail is an
        // assertion on what they emit. Both walk the same message list and both have thrown on
        // unfamiliar shapes before.
        StringWriter dump = new() { NewLine = "\n" };
        StringWriter json = new() { NewLine = "\n" };

        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(Demo());

        DemoTextDumper.Write(dump, "synthetic.dem", header, commands, options: null);
        DemoJsonLinesWriter.Write(json, "synthetic.dem", header, commands);

        dump.ToString().ShouldContain("cp_process_final");

        // JSON Lines means one complete object per line, so a writer that emitted a pretty-printed
        // document would still contain the map name and be unusable. Checked structurally.
        string[] lines = json.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBeGreaterThan(1);
        lines.ShouldAllBe(line => line.StartsWith('{') && line.EndsWith('}'));
    }

    [Test]
    public void EveryKindTheWriterClaimsToSupportIsExercised()
    {
        // **The denominator is generated, so this cannot go stale.** A hand-kept list of kinds is
        // a list that stops matching the code the first time an encoder is added, and it fails
        // silently — the new kind is simply never tested and nothing says so.
        //
        // Asking `CanWrite` inverts that: adding an encoder breaks this test until a specimen
        // joins the demo above.
        HashSet<NetMessageType> covered =
            [.. SyntheticDemo.MessagesIn(Demo()).Select(message => message.Type)];

        List<string> missing = [];
        foreach (INetMessage candidate in EveryKindAnEncoderExistsFor)
        {
            if (!covered.Contains(candidate.Type))
            {
                missing.Add(candidate.GetType().Name);
            }
        }

        missing.ShouldBeEmpty();

        // Both string-table kinds are deliberately absent and this states why rather than leaving
        // a silent hole: `CanWrite` accepts them only when `Wire` is not null, which means only
        // when the message came off a demo. A table built in a test has no wire form to
        // reproduce, and inventing one would be re-encoding a different message.
        //
        // They are covered by StringTableCodecTests and by the corpus round trip instead.
        NetMessageWriter.CanWrite(
                new CreateStringTableMessage(
                    Name: "test",
                    MaxEntries: 8,
                    Entries: [],
                    IsCompressed: false,
                    UndecodedReason: null))
            .ShouldBeFalse();
    }

    /// <summary>One instance of every kind <c>NetMessageWriter</c> has an encoder for.</summary>
    /// <remarks>
    /// Ordered deliberately. <c>svc_ServerInfo</c> comes first because the reader sizes several
    /// later fields from it and a message ahead of it is read at protocol 0; the game event list
    /// comes before the event that references it, for the same reason a client cannot decode an
    /// event it has no definition for.
    /// </remarks>
    private static IReadOnlyList<INetMessage> EveryKindAnEncoderExistsFor =>
    [
        ServerInfo(),
        NetEmptyMessage.Instance,
        new NetTickMessage(Tick: 123456, HostFrameTimeRaw: 512, HostFrameTimeStdDevRaw: 64),
        new PrintMessage("a printed line"),
        new StringCmdMessage("echo hello"),
        new SetConVarMessage(
        [
            new KeyValuePair<string, string>("sv_gravity", "800"),
            new KeyValuePair<string, string>("tf_weapon_criticals", "0"),
        ]),
        new SignOnStateMessage(State: 5, SpawnCount: 77),
        ClassInfo(),
        new PrefetchMessage(SoundIndex: 1234),
        new SetViewMessage(EntityIndex: 19),

        // A negative pitch, for the saturation bug described in the round-trip test above.
        new FixAngleMessage(IsRelative: true, Pitch: -45f, Yaw: 90f, Roll: 0f),
        new FileMessage(TransferId: 0xABCD, FileName: "maps/cp_process_final.bsp", IsRequested: true),
        new GetCvarValueMessage(Cookie: 0x1234, CvarName: "cl_interp"),
        new BspDecalMessage(
            OnEntity: true, EntityIndex: 31, ModelIndex: 42,
            X: 64f, Y: -128f, Z: 256f, TextureIndex: 9, IsLowPriority: false),
        new VoiceInitMessage(Codec: "vaudio_celt", Quality: 5, SampleRate: 22050),
        new VoiceDataMessage(
            Client: 3, Proximity: 1, BodyBits: 24, Body: new byte[] { 0xDE, 0xAD, 0xBE }),
        new EntityMessage(
            EntityIndex: 88, ClassId: 7, BodyBits: 16, Body: new byte[] { 0x12, 0x34 }),
        new TempEntitiesMessage(Count: 2, BodyBits: 16, Body: new byte[] { 0x55, 0xAA }),
        new UserMessage(
            UserMessageType: 23, Name: null, BodyBits: 16, Body: new byte[] { 0x0F, 0xF0 }),
        Chat(),
        PacketEntities(),
        Sounds(),
        EventList(),
        PlayerDeath(),
    ];

    private static byte[] Demo() => SyntheticDemo.Containing(Protocol, [.. EveryKindAnEncoderExistsFor]);

    private static string Assemble(byte[] demo)
    {
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(demo);

        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands);
        return text.ToString();
    }

    private static string Trace()
    {
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(Demo());

        StringWriter text = new() { NewLine = "\n" };
        DemoTraceWriter.Write(text, "synthetic.dem", header, commands);
        return text.ToString();
    }

    private static (DemoHeader Header, IReadOnlyList<DemoCommand> Commands) Read(byte[] demo) =>
        (DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes)),
            [.. DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes))]);

    private static ServerInfoMessage ServerInfo() => new(
        NetworkProtocol: Protocol,
        ServerCount: 7,
        IsSourceTv: false,
        IsDedicated: true,
        MapCrc: 0x9ABC_DEF0,
        MaxClasses: 9,
        MapHash: [.. Enumerable.Range(1, 16).Select(value => (byte)value)],
        PlayerSlot: 1,
        MaxPlayers: 24,
        IntervalPerTick: 1f / 66.67f,
        Platform: 'w',
        GameDirectory: "tf",
        Map: "cp_process_final",
        Skybox: "sky_tf2_04",
        ServerName: "synthetic",
        IsReplay: false);

    private static ClassInfoMessage ClassInfo() => new(
        ClassCount: 3,
        CreateOnClient: false,
        Classes:
        [
            new ServerClass(0, "CTFPlayer", "DT_TFPlayer"),
            new ServerClass(1, "CWeaponMedigun", "DT_WeaponMedigun"),
            new ServerClass(2, "CTFAmmoPack", "DT_TFAmmoPack"),
        ]);

    private static PacketEntitiesMessage PacketEntities() => new(
        MaxEntries: 64,
        IsDelta: true,
        DeltaFromTick: 100,
        BaselineIndex: false,
        UpdatedEntries: 3,
        LengthBits: 24,
        UpdateBaseline: false,
        Body: new byte[] { 0x11, 0x22, 0x33 });

    private static SoundsMessage Sounds()
    {
        (byte[] body, int bits) = SoundEncoder.Encode(
            [
                new DecodedSound(
                    EntityIndex: 12, SoundNumber: 300, Flags: 0, Channel: 6,
                    IsAmbient: false, IsSentence: false, SequenceNumber: 0, Volume: 1f,
                    SoundLevel: 75, Pitch: 100, DelaySeconds: 0f,
                    OriginX: 0f, OriginY: 0f, OriginZ: 0f, SpeakerEntity: -1,
                    SpecialDsp: 0,
                    Sent: SoundFields.Entity | SoundFields.SoundNumber),
            ],
            Protocol);

        return new SoundsMessage(IsReliable: false, Count: 1, BodyBits: bits, Body: body);
    }

    private static GameEventListMessage EventList() => new(
    [
        new GameEventDefinition(
            Id: 3,
            Name: "player_death",
            Fields:
            [
                // One field of each width the format has, so a type read one place along in the
                // enum cannot produce the same bits. `customkill` is a byte and `userid` a short,
                // which is the pair that shipped a no-op annotation once: a dumper matching on
                // `int` matched neither.
                new GameEventField("userid", GameEventValueType.Short),
                new GameEventField("weapon", GameEventValueType.String),
                new GameEventField("damagebits", GameEventValueType.Long),
                new GameEventField("customkill", GameEventValueType.Byte),
                new GameEventField("crit", GameEventValueType.Bool),
                new GameEventField("distance", GameEventValueType.Float),
            ]),
        new GameEventDefinition(
            Id: 9,
            Name: "teamplay_round_win",
            Fields: [new GameEventField("team", GameEventValueType.Byte)]),
    ]);

    private static GameEventMessage PlayerDeath() => new(
        EventId: 3,
        Name: "player_death",
        Values: new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["userid"] = (short)12,
            ["weapon"] = "scattergun",
            ["damagebits"] = 1048576,
            ["customkill"] = (byte)7,
            ["crit"] = true,
            ["distance"] = 512.5f,
        });

    /// <summary>A <c>SayText2</c> body, built to the shape the chat reader expects.</summary>
    /// <remarks>
    /// Two header bytes — the sender's entity index and a flag — then NUL-terminated strings.
    /// Constructed rather than copied from a demo so the expected text is known; the assertion
    /// that it really is a chat body rather than an unrecognised user message lives in the test,
    /// where a change to the reader would surface as a failure instead of as a silent
    /// reclassification.
    /// </remarks>
    private static ChatMessage Chat()
    {
        List<byte> body = [2, 1];
        foreach (string part in new[] { "TF_Chat_Team", "Heavy", "spah sappin mah sentry" })
        {
            body.AddRange(Encoding.UTF8.GetBytes(part));
            body.Add(0);
        }

        body.Add(0);
        body.Add(0);

        ChatMessage? parsed = ChatMessage.Parse([.. body]);

        // A precondition, not the test. If this body stopped being readable as chat the demo
        // would silently carry a plain user message instead, and every chat assertion would
        // vanish rather than fail.
        parsed.ShouldNotBeNull();

        return parsed with { BodyBits = body.Count * 8, Body = body.ToArray() };
    }
}
