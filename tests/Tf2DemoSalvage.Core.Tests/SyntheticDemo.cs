using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// Builds a demo containing exactly the messages a test asks for.
/// </summary>
/// <remarks>
/// **The enabler for moving the corpus suite to synthetic fixtures.** Those tests read real demos
/// because that was the only way to obtain a demo; this project can now write one the engine
/// accepts, so a case can be constructed instead of found.
///
/// The reason to move is practical and the owner's: a corpus test needs 305 MB of Git LFS, which
/// means it cannot run on the fuzz box, costs bandwidth on every CI job that fetches it, and gates
/// the weekly mutation run. A synthetic test runs anywhere, in milliseconds, and can exercise cases
/// no recording happens to contain — the era gaps at protocols 12–13 and 17–23 among them.
///
/// **A synthetic test is STRONGER than the corpus test it replaces, not a compromise**, and that is
/// worth stating because the opposite reads as obvious. A corpus test does not know the right
/// answer: given a real demo, nobody can say what <c>svc_ServerInfo</c>'s map name should be, so
/// <c>CorpusServerInfoTests</c> compares it against the file header as a substitute oracle. That is
/// a good workaround for missing ground truth. A synthetic demo HAS ground truth — the map name is
/// whatever the test put there — so it asserts the value directly, which is a stricter claim than
/// "these two agree".
///
/// **What is genuinely lost is narrower than it looks.** A synthetic fixture encodes this project's
/// belief about the format on both sides, so a symmetric misreading passes — and a byte-identical
/// round trip does not catch that either, because a consistent misunderstanding round-trips
/// perfectly. What anchors the semantics is reading Valve's source (the conformance suites) and the
/// engine accepting a demo this project wrote. Neither needs the corpus.
///
/// See <c>docs/memory/author-the-specimen-the-corpus-lacks.md</c>.
/// </remarks>
internal static class SyntheticDemo
{
    /// <summary>The protocol most of TF2's life used, and the corpus's newest.</summary>
    public const ushort DefaultProtocol = 24;

    /// <summary>A demo whose single packet carries the given messages.</summary>
    /// <param name="messages">What the packet should decode to.</param>
    /// <returns>The demo's bytes, as a file would hold them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is null.</exception>
    public static byte[] Containing(params INetMessage[] messages) =>
        Containing(DefaultProtocol, messages);

    /// <summary>A demo at a chosen protocol whose single packet carries the given messages.</summary>
    /// <param name="protocol">Network protocol, which decides message-id width and field layouts.</param>
    /// <param name="messages">What the packet should decode to.</param>
    /// <returns>The demo's bytes.</returns>
    /// <remarks>
    /// **The protocol is a parameter because it is the axis the corpus is thinnest on.** Five are
    /// represented by a recording — 11, 14, 15, 16 and 24 — and 12–13 and 17–23 have no specimen at
    /// all. A synthetic demo can be written at any of them, which is the one thing the real corpus
    /// can never be extended to do without someone finding a twenty-year-old file.
    /// </remarks>
    public static byte[] Containing(ushort protocol, params INetMessage[] messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // **Ended with a dem_stop, as the engine ends every recording.** Its absence means a file
        // that was cut short, and this project warns about exactly that — so a fixture without one
        // would make every test using it noisy for a reason unrelated to what it measures.
        return DemoWriter.Write(
            Header(protocol),
            [
                Packet(protocol, 0, messages),
                new DemoCommand(DemoCommandType.Stop, 66, ReadOnlyMemory<byte>.Empty),
            ]);
    }

    /// <summary>A demo built from whole commands, for cases a single packet cannot express.</summary>
    /// <param name="protocol">Network protocol, recorded in the header.</param>
    /// <param name="commands">The commands, in stream order, without the terminating stop.</param>
    /// <returns>The demo's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commands"/> is null.</exception>
    /// <remarks>
    /// **The entity path needs this and the single-packet form cannot provide it.** A schema
    /// arrives in <c>dem_datatables</c>, which is a command rather than a message, so anything
    /// touching entities needs at least two commands in a particular order — the tables before the
    /// snapshot that references them.
    /// </remarks>
    public static byte[] From(ushort protocol, params DemoCommand[] commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        return DemoWriter.Write(
            Header(protocol),
            [.. commands, new DemoCommand(DemoCommandType.Stop, 66, ReadOnlyMemory<byte>.Empty)]);
    }

    /// <summary>A <c>dem_datatables</c> command carrying a schema.</summary>
    /// <param name="schema">The entity schema.</param>
    /// <param name="protocol">Protocol, which sizes two of the schema's fields.</param>
    /// <param name="tick">The tick the command is stamped with.</param>
    /// <returns>The command.</returns>
    /// <remarks>
    /// The payload is length-prefixed and carries no prologue, which is why this needs no
    /// <c>democmdinfo_t</c> — unlike a packet or a signon.
    /// </remarks>
    public static DemoCommand DataTables(
        Core.Schema.DemoSchema schema, ushort protocol = DefaultProtocol, int tick = 0) =>
        new(DemoCommandType.DataTables, tick, SyntheticSchema.Write(schema, protocol));

    /// <summary>A <c>svc_CreateStringTable</c> carrying the given strings, in order.</summary>
    /// <param name="name">Table name, e.g. <c>modelprecache</c>.</param>
    /// <param name="strings">The entries, whose positions become their indices.</param>
    /// <param name="maxEntries">Table capacity, which sizes the index field.</param>
    /// <returns>The message, complete with the wire form the writer needs.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="strings"/> is null.</exception>
    /// <remarks>
    /// **A string table is the one message this project could not previously build in a test, and
    /// it blocked a whole group of them.** <c>NetMessageWriter.CanWrite</c> accepts a
    /// <c>CreateStringTableMessage</c> only when its <c>Wire</c> is not null — that is, only when
    /// it came off a real demo — because a table built from values alone has no wire form to
    /// reproduce and inventing one would be re-encoding a different message.
    ///
    /// That is the right rule for the writer and the wrong obstacle for a fixture, and
    /// <c>StringTableCodec.WriteEntries</c> resolves it: the entry encoding is derivable from the
    /// entries when nothing reuses history, which is exactly the case a fixture wants. So the wire
    /// form here is genuine rather than fabricated — it is what a sender that never used the
    /// back-reference would have written.
    ///
    /// What this deliberately does NOT do is compression. A compressed payload has to reproduce a
    /// particular Snappy implementation's output byte for byte, which no parser can promise; the
    /// corpus covers that path and a fixture cannot.
    /// </remarks>
    public static CreateStringTableMessage StringTable(
        string name, IReadOnlyList<string> strings, int maxEntries = 64) =>
        StringTable(
            name,
            strings.Select(text => (text, (IReadOnlyList<byte>)Array.Empty<byte>())).ToList(),
            maxEntries);

    /// <summary>A string table whose entries carry user data as well as text.</summary>
    /// <param name="name">Table name, e.g. <c>userinfo</c>.</param>
    /// <param name="entries">Each entry's text and its user data payload.</param>
    /// <param name="maxEntries">Table capacity, which sizes the index field.</param>
    /// <returns>The message, complete with its wire form.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <remarks>
    /// **User data is where the roster lives, and it is why this overload exists.** The
    /// <c>userinfo</c> table's entries are named for the entity index and carry a 132-byte
    /// <c>player_info_t</c> in their user data — the name, the user id and the Steam id are all in
    /// there, not in the entry text. A table helper that set only the text could build a
    /// precache but never a roster.
    /// </remarks>
    public static CreateStringTableMessage StringTable(
        string name,
        IReadOnlyList<(string Text, IReadOnlyList<byte> UserData)> entries,
        int maxEntries = 64)
    {
        ArgumentNullException.ThrowIfNull(entries);

        List<StringTableEntry> built =
        [
            .. entries.Select(
                (entry, index) => new StringTableEntry(index, entry.Text, entry.UserData)),
        ];

        return Table(name, built, maxEntries);
    }

    private static CreateStringTableMessage Table(
        string name, List<StringTableEntry> entries, int maxEntries)
    {

        (byte[] body, int bits) = StringTableCodec.WriteEntries(
            entries, maxEntries, fixedUserData: false, userDataSizeBits: 0);

        return new CreateStringTableMessage(
            name,
            maxEntries,
            entries,
            IsCompressed: false,
            UndecodedReason: null,
            Wire: new CreateStringTableWire(entries.Count, bits, body, null, 0));
    }

    /// <summary>One packet command carrying the given messages.</summary>
    /// <param name="protocol">Network protocol, which the encoder needs.</param>
    /// <param name="tick">The tick the packet is stamped with.</param>
    /// <param name="messages">What the packet should decode to.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A message has no encoder.</exception>
    public static DemoCommand Packet(ushort protocol, int tick, params INetMessage[] messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        BitWriter writer = new();
        NetDecodeState state = new() { NetworkProtocol = protocol };

        foreach (INetMessage message in messages)
        {
            if (!NetMessageWriter.TryWrite(writer, message, state))
            {
                // **Loudly, because the alternative is a demo missing the message under test.** A
                // silently skipped write produces a file that decodes to nothing and a test that
                // fails somewhere else entirely.
                throw new InvalidOperationException(
                    $"{message.GetType().Name} has no encoder, so it cannot be put in a demo.");
            }

            // **The writer's state has to learn what a reader learns at the same point**, or every
            // later message whose width depends on it is encoded at the wrong size. A prefetch is
            // sized from svc_ServerInfo, a string-table update from that table's capacity, an event
            // from the event list — so a synthetic demo carrying two related messages is exactly
            // where getting this wrong shows.
            switch (message)
            {
                case ServerInfoMessage info:
                    state.ServerInfo = info;
                    break;

                case CreateStringTableMessage table:
                    state.AddStringTable(table.Name, table.MaxEntries);
                    break;

                case GameEventListMessage list:
                    state.AddEventDefinitions(list.Definitions);
                    break;

                default:
                    break;
            }
        }

        // **The prologue is democmdinfo_t plus the two sequence numbers**, which a packet command
        // always carries and the reader always consumes. Zeroed rather than omitted: the reader
        // takes a fixed number of bytes here, so leaving them out shifts the payload.
        return new DemoCommand(DemoCommandType.Packet, tick, writer.Build(), new byte[PrologueBytes]);
    }

    /// <summary>
    /// Bytes of <c>democmdinfo_t</c> and the sequence numbers that precede a packet's payload.
    /// </summary>
    /// <remarks>
    /// 76 for <c>democmdinfo_t</c> and 8 for the two sequence numbers, matching
    /// <c>DemoCommandReader</c>'s own constants. Stated as one number because the reader treats it
    /// as one opaque run — this project writes it back as it was read rather than rebuilding it
    /// from decoded fields, which is why a demo can be reproduced from what was read rather than
    /// from what was understood.
    ///
    /// **Guessed at 160 first and the fixture would not read back**, which is the useful direction
    /// for a builder to fail in: a wrong prologue shifts the payload, so the reader hit
    /// "Unrecognised demo command 0" rather than quietly returning a demo with no messages in it.
    /// </remarks>
    private const int PrologueBytes = 76 + 8;

    /// <summary>A header that is consistent with itself, for a demo of one packet.</summary>
    /// <remarks>
    /// **Consistent on purpose**, because two of this project's own warnings fire on headers that
    /// are not: a frame count disagreeing with the packets present, and a missing <c>dem_stop</c>.
    /// A fixture that tripped either would make every test using it noisy for a reason unrelated to
    /// what it measures.
    /// </remarks>
    public static DemoHeader Header(ushort protocol = DefaultProtocol) => new()
    {
        DemoProtocol = 3,
        NetworkProtocol = protocol,
        ServerName = "synthetic",
        ClientName = "synthetic",
        MapName = "cp_process_final",
        GameDirectory = "tf",
        PlaybackTimeSeconds = 1f,
        PlaybackTicks = 66,
        PlaybackFrames = 1,
        SignonLengthBytes = 0,
    };

    /// <summary>The messages a demo's packets decode to, in stream order.</summary>
    /// <param name="demo">A demo's bytes.</param>
    /// <returns>Every message from every packet.</returns>
    /// <remarks>
    /// The read side of the round trip, so a test can assert on what came back rather than on what
    /// went in. Kept here so the pair stays together: a change to how a packet is built has to be
    /// matched by how one is read, and separating them is how the two drift.
    /// </remarks>
    public static IReadOnlyList<INetMessage> MessagesIn(byte[] demo)
    {
        ArgumentNullException.ThrowIfNull(demo);

        DemoHeader header = DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes));
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        List<INetMessage> messages = [];

        foreach (DemoCommand command in
            DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is DemoCommandType.Packet or DemoCommandType.Signon)
            {
                messages.AddRange(NetMessageReader.Read(command.Payload.Span, state).Messages);
            }
        }

        return messages;
    }
}
