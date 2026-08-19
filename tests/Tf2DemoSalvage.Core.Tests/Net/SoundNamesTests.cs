using System.Collections.Generic;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests resolution of a sound index to the file the server precached for it.
/// </summary>
/// <remarks>
/// A <c>svc_Sounds</c> body carries an index into the <c>soundprecache</c> string table, never a
/// name — so a trace that prints the number alone is reporting the one thing about the sound that
/// is meaningless outside this demo. The table is per-server and per-map: index 4440 is a
/// different sound in the next recording.
/// </remarks>
public sealed class SoundNamesTests
{
    [Test]
    public void SoundNames_AnIndex_ResolvesToThePrecachedPath()
    {
        SoundNames names = new();
        names.Add(Table(
            (0, ""),
            (1, "player/footsteps/concrete1.wav"),
            (2, "weapons/shotgun_shoot.wav")));

        names.Resolve(1).ShouldBe("player/footsteps/concrete1.wav");
        names.Resolve(2).ShouldBe("weapons/shotgun_shoot.wav");
    }

    [Test]
    public void TheIndexIsThePositionInTheTable_NotTheEntrysOwnIndexField()
    {
        // String table entries carry an explicit index, and it is the one the sound message
        // refers to. Resolving by position in the list instead would work only while the two
        // happen to agree - which they do not once a table update inserts out of order.
        SoundNames names = new();
        names.Add(new CreateStringTableMessage(
            "soundprecache",
            MaxEntries: 16384,
            Entries:
            [
                new StringTableEntry(7, "weapons/rocket_shoot.wav", []),
                new StringTableEntry(3, "ambient/water_drip.wav", []),
            ],
            IsCompressed: false,
            UndecodedReason: null));

        names.Resolve(7).ShouldBe("weapons/rocket_shoot.wav");
        names.Resolve(3).ShouldBe("ambient/water_drip.wav");

        // Position 0 held index 7, so a positional reading would have answered here.
        names.Resolve(0).ShouldBeNull();
    }

    [Test]
    public void SoundNames_OnlyTheSoundTable_IsUsed()
    {
        // Every string table flows past the same reader. Taking entries from the wrong one would
        // resolve sound indices to model or decal paths - a confident, plausible, wrong answer.
        SoundNames names = new();
        names.Add(new CreateStringTableMessage(
            "modelprecache",
            MaxEntries: 2048,
            Entries: [new StringTableEntry(1, "models/player/scout.mdl", [])],
            IsCompressed: false,
            UndecodedReason: null));

        names.Resolve(1).ShouldBeNull();
    }

    [Test]
    public void SoundNames_AnUnknownIndex_IsUnresolvedNotGuessed()
    {
        SoundNames names = new();
        names.Add(Table((1, "player/footsteps/concrete1.wav")));

        names.Resolve(9999).ShouldBeNull();
        names.Resolve(-1).ShouldBeNull();
    }

    [Test]
    public void SoundNames_AnEmptyOrUndecodedTable_ResolvesNothing()
    {
        // A compressed table this project cannot read yields no entries and a reason. That must
        // leave indices unresolved rather than throwing: the rest of the demo still decodes, and
        // the trace should keep printing sound numbers.
        SoundNames names = new();
        names.Add(new CreateStringTableMessage(
            "soundprecache",
            MaxEntries: 16384,
            Entries: [],
            IsCompressed: true,
            UndecodedReason: "compressed with an unknown magic"));

        names.Resolve(1).ShouldBeNull();
    }

    [Test]
    public void SoundNames_AnEntryWithNoText_IsNotAName()
    {
        // Table entries may carry user data and no string at all. Those are real entries and
        // must not resolve to an empty name that reads like a sound called "".
        SoundNames names = new();
        names.Add(new CreateStringTableMessage(
            "soundprecache",
            MaxEntries: 16384,
            Entries: [new StringTableEntry(4, null, [1, 2, 3])],
            IsCompressed: false,
            UndecodedReason: null));

        names.Resolve(4).ShouldBeNull();
    }

    private static CreateStringTableMessage Table(params (int Index, string Text)[] entries)
    {
        List<StringTableEntry> list = [];
        foreach ((int index, string text) in entries)
        {
            list.Add(new StringTableEntry(index, text, []));
        }

        return new CreateStringTableMessage(
            "soundprecache", 16384, list, IsCompressed: false, UndecodedReason: null);
    }
}
