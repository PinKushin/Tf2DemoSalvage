using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// <c>StringTableAssembly</c> both ways — string tables rendered as text and read back.
/// </summary>
/// <remarks>
/// **The last of the four big uncovered blocks: 111 mutants no `Core.Tests` test reached**
/// (measured 2026-08-18, `docs/MEASUREMENT-PLAN.md`).
///
/// **The wire body is built by the project's own encoder, never by hand.**
/// <c>StringTableCodec.WriteEntries</c> produces the bytes and the bit count, exactly as it does in
/// production; writing those bits out by hand in the test would be checking this project's format
/// against my reading of it, which is the trap in `docs/memory/fixtures-are-the-weak-point.md`.
///
/// **What makes string tables their own case is that the values do not determine the encoding.**
/// An entry may repeat a prefix of one of the last 32 strings, and which one and how much is a
/// choice the sender made that the decoded text does not record; likewise a sequential index costs
/// one bit where an explicit one costs a full field. So the entry carries its own encoding shape
/// (`FollowsPrevious`, `HistoryIndex`, `CopyLength`) and the round trip has to preserve that, not
/// just the strings — `docs/memory/round-trip-needs-the-encoding-shape.md`.
///
/// The compressed case is deliberately different and is asserted as such: a compressed payload
/// cannot become text, because reproducing it means reproducing one particular compressor's output.
/// It keeps its bytes and only its header is promoted, which is a documented limit rather than a
/// gap.
/// </remarks>
public sealed class StringTableAssemblyTests
{
    private const int MaxEntries = 32;

    [Test]
    public void APlainTableRoundTripsItsHeaderAndEntries()
    {
        CreateStringTableMessage table = Create(
            "userinfo",
            [
                Entry(0, "player_one"),
                Entry(1, "player_two"),
                Entry(2, "player_three"),
            ]);

        CreateStringTableMessage read = RoundTripCreate(table);

        read.Name.ShouldBe("userinfo");
        read.MaxEntries.ShouldBe(MaxEntries);
        read.IsCompressed.ShouldBeFalse();
        read.Entries.Count.ShouldBe(3);

        // Three entries rather than one: a count that is ignored still reproduces a single entry.
        read.Entries[0].Text.ShouldBe("player_one");
        read.Entries[1].Text.ShouldBe("player_two");
        read.Entries[2].Text.ShouldBe("player_three");
    }

    [Test]
    public void AnEntryKeepsItsHistoryReuseRatherThanJustItsText()
    {
        // **The case the decoded text cannot express.** Two entries with identical text can have
        // been sent completely differently - one in full, one as "reuse 6 characters of history
        // entry 0". Asserting only the strings would pass against a writer that dropped the reuse
        // entirely and re-sent every string in full, which reproduces the values and not the bits.
        CreateStringTableMessage table = Create(
            "modelprecache",
            [
                Entry(0, "models/player/scout.mdl"),
                new StringTableEntry(
                    Index: 1,
                    Text: "models/player/soldier.mdl",
                    UserData: [],
                    FollowsPrevious: true,
                    HistoryIndex: 0,
                    CopyLength: 14),
            ]);

        CreateStringTableMessage read = RoundTripCreate(table);

        read.Entries[1].Text.ShouldBe("models/player/soldier.mdl");
        read.Entries[1].FollowsPrevious.ShouldBeTrue();
        read.Entries[1].HistoryIndex.ShouldBe(0);
        read.Entries[1].CopyLength.ShouldBe(14);

        // The control: the first entry reused nothing, so a writer that hardcoded reuse fails here.
        read.Entries[0].FollowsPrevious.ShouldBeFalse();
        read.Entries[0].CopyLength.ShouldBe(0);
    }

    [Test]
    public void UserDataSurvivesIncludingItsLength()
    {
        CreateStringTableMessage table = Create(
            "instancebaseline",
            [
                new StringTableEntry(0, "1", [0xDE, 0xAD, 0xBE, 0xEF]),
                new StringTableEntry(1, "2", []),
            ]);

        CreateStringTableMessage read = RoundTripCreate(table);

        read.Entries[0].UserData.ShouldBe(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        // Empty user data is not the same as absent, and it is the case a length field gets wrong.
        read.Entries[1].UserData.ShouldBeEmpty();
    }

    [Test]
    public void AnEntryWithNoTextIsNotAnEntryWithEmptyText()
    {
        // A null string and a zero-length string are different on the wire - one sends no string
        // at all, the other sends a terminator. Conflating them is invisible in any assertion
        // that only checks for "no characters".
        CreateStringTableMessage table = Create(
            "dynamicmodel", [new StringTableEntry(0, null, []), new StringTableEntry(1, "", [])]);

        CreateStringTableMessage read = RoundTripCreate(table);

        read.Entries[0].Text.ShouldBeNull();
        read.Entries[1].Text.ShouldBe(string.Empty);
    }

    [Test]
    public void AnInternationalNameSurvives()
    {
        // userinfo carries player names, and players are not all anglophone -
        // docs/memory/international-names-are-required.md.
        CreateStringTableMessage read = RoundTripCreate(
            Create("userinfo", [Entry(0, "Ωmega_переменная_名前")]));

        read.Entries[0].Text.ShouldBe("Ωmega_переменная_名前");
    }

    [Test]
    public void ACompressedTableKeepsItsPayloadAndSaysSoInItsHeader()
    {
        // **A documented limit, asserted rather than assumed.** Reproducing a compressed payload
        // means reproducing one compressor's exact output, which no parser can promise - so the
        // header is promoted to text and the bytes ride along untouched. The test pins that this
        // is what happens, because silently decompressing and re-compressing would round-trip the
        // VALUES while changing the bits.
        byte[] payload = [0x01, 0x02, 0x03, 0x04];

        CreateStringTableMessage compressed = new(
            Name: "soundprecache",
            MaxEntries: MaxEntries,
            Entries: [],
            IsCompressed: true,
            UndecodedReason: "compressed",
            Wire: new CreateStringTableWire(
                EntryCount: 9,
                BodyBits: payload.Length * 8,
                Body: payload,
                FixedUserDataSizeBytes: null,
                FixedUserDataSizeBits: 0));

        IReadOnlyList<string> lines = StringTableAssembly.WriteCreate(compressed).ShouldNotBeNull();

        // One line, not a block: there are no entries to list.
        lines.Count.ShouldBe(1);
        lines[0].ShouldContain("compressed=1");
        lines[0].ShouldContain("payload 01020304");

        CreateStringTableMessage read = StringTableAssembly.BuildCreate(
            Tokenize(lines[0]), static () => null);

        read.Name.ShouldBe("soundprecache");
        read.IsCompressed.ShouldBeTrue();
        read.Wire.ShouldNotBeNull().Body.ToArray().ShouldBe(payload);
        read.Wire.EntryCount.ShouldBe(9);
    }

    [Test]
    public void AnUpdateRoundTripsAgainstItsTableCapacity()
    {
        (byte[] body, int bits) = StringTableCodec.WriteEntries(
            [Entry(0, "alpha"), Entry(1, "beta")], MaxEntries, fixedUserData: false, userDataSizeBits: 0);

        UpdateStringTableMessage update = new(
            TableId: 3,
            Entries: [Entry(0, "alpha"), Entry(1, "beta")],
            UndecodedReason: null,
            Wire: new UpdateStringTableWire(EntryCount: 2, BodyBits: bits, Body: body));

        IReadOnlyList<string> lines = StringTableAssembly.WriteUpdate(update).ShouldNotBeNull();

        int next = 1;

        // An update names its table by creation order and sizes its indices from that table's
        // capacity, so the state has to know the table before the update can be read back.
        NetDecodeState state = new() { NetworkProtocol = 24 };
        state.AddStringTable("a", MaxEntries);
        state.AddStringTable("b", MaxEntries);
        state.AddStringTable("c", MaxEntries);
        state.AddStringTable("userinfo", MaxEntries);

        UpdateStringTableMessage read = StringTableAssembly.BuildUpdate(
            Tokenize(lines[0]), () => next < lines.Count ? lines[next++] : null, state);

        read.TableId.ShouldBe(3);
        read.Entries.Count.ShouldBe(2);
        read.Entries[0].Text.ShouldBe("alpha");
        read.Entries[1].Text.ShouldBe("beta");
    }

    /// <summary>An ordinary entry sent in full.</summary>
    private static StringTableEntry Entry(int index, string text) => new(index, text, []);

    /// <summary>Builds a create message whose wire body comes from the project's own encoder.</summary>
    private static CreateStringTableMessage Create(
        string name, IReadOnlyList<StringTableEntry> entries)
    {
        (byte[] body, int bits) = StringTableCodec.WriteEntries(
            entries, MaxEntries, fixedUserData: false, userDataSizeBits: 0);

        return new CreateStringTableMessage(
            Name: name,
            MaxEntries: MaxEntries,
            Entries: entries,
            IsCompressed: false,
            UndecodedReason: null,
            Wire: new CreateStringTableWire(
                EntryCount: entries.Count,
                BodyBits: bits,
                Body: body,
                FixedUserDataSizeBytes: null,
                FixedUserDataSizeBits: 0));
    }

    /// <summary>Renders a create message and reads it straight back.</summary>
    private static CreateStringTableMessage RoundTripCreate(CreateStringTableMessage table)
    {
        IReadOnlyList<string> lines = StringTableAssembly.WriteCreate(table)
            .ShouldNotBeNull("the table has no text form");

        int next = 1;

        return StringTableAssembly.BuildCreate(
            Tokenize(lines[0]), () => next < lines.Count ? lines[next++] : null);
    }

    /// <summary>Splits a header line, keeping quoted names whole.</summary>
    /// <remarks>
    /// The header's only quoted field is the table name, and it is quoted precisely so a name with
    /// a space survives. A plain split would tear that apart — the mistake the first version of
    /// <c>EventAssemblyTests</c> made, and the reason this is spelled out rather than inlined.
    /// </remarks>
    private static List<string> Tokenize(string line)
    {
        List<string> tokens = [];
        System.Text.StringBuilder current = new();
        bool quoted = false;

        foreach (char character in line)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (character == ' ' && !quoted)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
