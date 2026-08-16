using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The <c>player_info_t</c> record, derived from the engine's declaration of it.
/// </summary>
/// <remarks>
/// **This record says who a demo is of**, which for a documented run is not a cosmetic field. Name,
/// user id and Steam id all come out of one fixed-size blob in a string table entry, and every offset
/// after the first variable-length member depends on padding the declaration never spells out.
///
/// **It is the first structure here whose padding is load-bearing.** <c>guid</c> is 33 bytes —
/// <c>SIGNED_GUID_LEN + 1</c> — so <c>friendsID</c> cannot start at 69; the compiler pushes it to 72,
/// and the record ends at 132 rather than 129 for the same reason. Both gaps are invisible in the
/// header and both are in this project's constants as bare numbers. Deriving them is the only way to
/// check the arithmetic rather than the transcription of it.
///
/// **<c>REPLAY_ENABLED</c> is deliberately undefined**, which is what makes the offsets come out.
/// The declaration carries an <c>isreplay</c> flag under that guard; with it defined every field
/// after <c>ishltv</c> moves by one byte and this project's record would be wrong by three. TF2
/// demos parse at the undefined layout, so that is the build the format was written by.
/// </remarks>
public sealed class PlayerInfoConformanceTests
{
    /// <summary>Where the engine declares the record.</summary>
    private const string ClientDll = "src/public/cdll_int.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void TheRecordIsAsLongAsTheEngineDeclaresIt()
    {
        // 132, not 129: filesDownloaded is one byte and the record pads to its widest member.
        Layout().Size.ShouldBe(PlayerInfo.RecordBytes);
    }

    [Test]
    public void EveryFieldWeReadSitsWhereTheEnginePutsIt()
    {
        CLayout record = Layout();

        List<string> wrong = [];

        foreach ((string name, int ours) in PlayerInfo.RecordFields)
        {
            int theirs = record.Offset(name);

            if (theirs != ours)
            {
                wrong.Add($"{name}: we read it at {ours}, the engine puts it at {theirs}");
            }
        }

        wrong.ShouldBeEmpty(string.Join("; ", wrong));
    }

    [Test]
    public void TheGuidLeavesAGapBeforeFriendsId()
    {
        // **The padding, asserted as its own claim.** guid is SIGNED_GUID_LEN + 1 = 33 bytes
        // starting at 36, so it ends at 69 — and friendsID, being four bytes wide, starts at 72.
        // Three bytes of nothing. A reader that packed the struct would put friendsID at 69 and be
        // wrong about every field after it, which is the entire back half of the record.
        CLayout record = Layout();

        int guidEnd = record.Offset("guid") + 33;

        guidEnd.ShouldBe(69);
        record.Offset("friendsID").ShouldBe(72);
    }

    [Test]
    public void TheReplayFlagIsAbsentAndItsAbsenceMatters()
    {
        // **The control, and the first version of it measured the wrong quantity.** Asserting that
        // the SIZE differs looks obviously right and is insensitive: isreplay is one byte at 110,
        // and customFiles is four-byte aligned, so it starts at 112 either way and the record is 132
        // both times. The extra field lands entirely inside padding the struct already had.
        //
        // So the measurement is the member list, where the difference actually appears. This is the
        // same trap as everywhere else in this project — a plausible number that is blind to the
        // manipulation.
        CLayout without = Layout();
        CLayout with = Layout(new HashSet<string>(StringComparer.Ordinal) { "REPLAY_ENABLED" });

        without.Members.ShouldNotContain(member => member.Name == "isreplay");
        with.Members.ShouldContain(member => member.Name == "isreplay");

        without.Size.ShouldBe(PlayerInfo.RecordBytes);

        // Stated because it is the surprising half: the guard changes what the record CONTAINS
        // without changing how long it is, so a reader checking only the length cannot tell the two
        // builds apart. Every field this project reads sits before isreplay, which is why the
        // ambiguity costs nothing here — and why it would matter to anything reading customFiles.
        with.Size.ShouldBe(without.Size);
    }

    /// <summary>Reads the record's layout, failing rather than skipping when it cannot.</summary>
    private static CLayout Layout(IReadOnlySet<string>? defined = null)
    {
        string text = SourceSdk.Text(ClientDll)
            ?? throw new InvalidOperationException($"{ClientDll} is missing from the SDK checkout");

        // The array bounds live in const.h rather than beside the structure, so both headers'
        // constants are needed to size a single field.
        Dictionary<string, int> constants =
            new(SourceSdk.Constants("src/public/const.h"), StringComparer.Ordinal);

        foreach ((string name, int value) in SourceSdk.Constants(ClientDll))
        {
            constants[name] = value;
        }

        // SIGNED_GUID_LEN + 1 is an expression, and the parser resolves a single name rather than
        // arithmetic. Supplying the sum under its own key is the smallest honest fix: the value is
        // stated here with the expression it came from, next to the header that declares both halves.
        constants["SIGNED_GUID_LEN + 1"] = constants["SIGNED_GUID_LEN"] + 1;

        CLayoutAttempt attempt = CStruct.Attempt(
            text,
            "player_info_s",
            constants,
            new Dictionary<string, CTypeSize>(StringComparer.Ordinal)
            {
                // typedef unsigned int CRC32_t, from checksum_crc.h.
                ["CRC32_t"] = new(4, 4),
                ["uint32"] = new(4, 4),
            },
            pointerBytes: 4,
            defined);

        return attempt.Layout
            ?? throw new InvalidOperationException(
                $"player_info_s could not be derived from {ClientDll}, so its layout is unchecked " +
                $"rather than correct. Stopped at: {attempt.Refused}");
    }
}
