using System.Linq;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// A string table written into a demo and read back out.
/// </summary>
/// <remarks>
/// **The one message this project could not build in a test, which blocked a group of them.**
/// <c>NetMessageWriter.CanWrite</c> accepts a <c>CreateStringTableMessage</c> only when its
/// <c>Wire</c> is not null — that is, only when it came off a real demo — because a table built
/// from values alone has no wire form to reproduce, and inventing one would be re-encoding a
/// different message. That is the right rule for the writer and the wrong obstacle for a fixture.
///
/// <c>StringTableCodec.WriteEntries</c> resolves it. The entry encoding is derivable from the
/// entries when nothing reuses the rolling history, which is exactly the case a fixture wants, so
/// the wire form is genuine rather than fabricated: it is what a sender that never used the
/// back-reference would have written.
///
/// String tables are what the model precache, the sound precache and the player roster all arrive
/// in, so this is the gate in front of converting those.
/// </remarks>
public sealed class StringTableDemoTests
{
    [Test]
    public void RoundTrip_ATableOfStrings_KeepsItsEntriesAndTheirOrder()
    {
        // Order is the whole contract: an entry's INDEX is its position, and everything that
        // references a table does so by index. A table that came back with the same strings in a
        // different order would satisfy any set-based assertion and resolve every lookup wrongly.
        CreateStringTableMessage read = Read(SyntheticDemo.StringTable(
            "modelprecache",
            ["", "models/player/scout.mdl", "models/props/barrel.mdl"],
            maxEntries: 64));

        read.Name.ShouldBe("modelprecache");
        read.MaxEntries.ShouldBe(64);
        read.IsDecoded.ShouldBeTrue();

        read.Entries.Select(entry => entry.Text)
            .ShouldBe(["", "models/player/scout.mdl", "models/props/barrel.mdl"]);
    }

    [Test]
    public void RoundTrip_ATableWithOneEntry_NeedsNoIndexFieldAtAll()
    {
        // **A one-entry table writes no index bits**, because floor(log2(1)) is zero and the
        // reader consumes nothing there — so writing a bit would insert one and shift everything
        // after it. The narrowest table is the one that catches an unconditional index write.
        Read(SyntheticDemo.StringTable("single", ["only"], maxEntries: 1))
            .Entries.ShouldHaveSingleItem().Text.ShouldBe("only");
    }

    [Test]
    public void RoundTrip_AnInternationalName_SurvivesAsUtf8()
    {
        // Every decoder here is UTF-8, and an ASCII reader corrupts a name into a plausible one
        // rather than failing — which is why this is asserted rather than assumed. The roster is a
        // string table, and player names are the least ASCII data in a demo.
        Read(SyntheticDemo.StringTable("userinfo", ["Ко́т", "Ωμέγα", "日本語"], maxEntries: 32))
            .Entries.Select(entry => entry.Text)
            .ShouldBe(["Ко́т", "Ωμέγα", "日本語"]);
    }

    [Test]
    public void RoundTrip_ATableAtItsCapacity_SizesTheIndexFieldFromMaxEntries()
    {
        // The index field's width comes from the table's declared capacity, not from how many
        // entries it holds, so a full table and a nearly-empty one of the same capacity encode
        // their indices identically. Filling one to capacity exercises the widest index it can
        // write.
        string[] strings = [.. Enumerable.Range(0, 32).Select(index => $"entry{index}")];

        CreateStringTableMessage read = Read(
            SyntheticDemo.StringTable("full", strings, maxEntries: 32));

        read.Entries.Count.ShouldBe(32);
        read.Entries[^1].Text.ShouldBe("entry31");
    }

    /// <summary>The single string table a synthetic demo carries.</summary>
    private static CreateStringTableMessage Read(CreateStringTableMessage table) =>
        SyntheticDemo.MessagesIn(SyntheticDemo.Containing(table))
            .OfType<CreateStringTableMessage>()
            .ShouldHaveSingleItem();
}
