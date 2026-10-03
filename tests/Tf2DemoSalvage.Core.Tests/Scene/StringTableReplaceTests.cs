using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A created table is a fresh table: what an earlier copy held and this one does not is gone (B452).</summary>
/// <remarks>
/// `CNetworkStringTableContainer::ReadStringTables` (engine.dll `0x1801e86a0`, x64 disassembly) finds each table by
/// name and calls `CNetworkStringTable::ReadStringTable` (`0x1801e82f0`), whose first act is `DeleteAllStrings`
/// (`0x1801e6880`: the item dictionary destroyed and rebuilt empty) before `AddString` re-adds every entry in block
/// order. The block therefore REPLACES each table — and a create, which the block is handed on as, starts from empty
/// too. Each test puts an entry in first, replaces without it, and expects it gone; a merge would keep it.
/// </remarks>
public sealed class StringTableReplaceTests
{
    [Test]
    public void Replace_NameTableWithoutAnEarlierIndex_ForgetsIt()
    {
        NameTable table = new();
        table.Apply([Entry(0, "a"), Entry(1, "b")]);

        table.Replace([Entry(0, "c")]);

        (table.Name(0), table.Name(1)).ShouldBe(("c", null));
    }

    [Test]
    public void Replace_ModelPrecacheWithoutAnEarlierIndex_ForgetsIt()
    {
        ModelPrecache precache = new();
        precache.Apply([Entry(0, ""), Entry(1, "models/a.mdl"), Entry(2, "models/b.mdl")]);

        precache.Replace([Entry(0, ""), Entry(1, "models/c.mdl")]);

        (precache.Path(1), precache.Path(2)).ShouldBe(("models/c.mdl", null));
    }

    [Test]
    public void ReplaceDynamic_DynamicModelsWithoutAnEarlierIndex_ForgetsIt()
    {
        // -2 - 2 * slot: dynamic index 2 * slot, even, networked.
        ModelPrecache precache = new();
        precache.ApplyDynamic([Entry(0, "models/a.mdl"), Entry(1, "models/b.mdl")]);

        precache.ReplaceDynamic([Entry(0, "models/c.mdl")]);

        (precache.Path(-2), precache.Path(-4)).ShouldBe(("models/c.mdl", null));
    }

    [Test]
    public void Replace_ScenePrecacheWithoutAnEarlierIndex_ForgetsIt()
    {
        ScenePrecache scenes = new();
        scenes.Apply([Entry(0, "a.vcd"), Entry(1, "b.vcd")]);

        scenes.Replace([Entry(0, "a.vcd")]);

        scenes.Path(1).ShouldBeNull();
    }

    [Test]
    public void Add_ASecondSoundTableCreate_ForgetsTheFirstTablesEntries()
    {
        SoundNames sounds = new();
        sounds.Add(SyntheticDemo.StringTable(SoundNames.TableName, ["a.wav", "b.wav"]));

        sounds.Add(SyntheticDemo.StringTable(SoundNames.TableName, ["c.wav"]));

        (sounds.Resolve(0), sounds.Resolve(1)).ShouldBe(("c.wav", null));
    }

    [Test]
    public void Replace_LightStylesWithoutAnEarlierStyle_HoldsNoPattern()
    {
        LightStyleFeed styles = new();
        styles.Apply([Style(0, "m"), Style(1, "a")], tick: null);

        styles.Replace([Style(0, "z")]);

        (styles.PatternAt(0, 100), styles.PatternAt(1, 100)).ShouldBe(("z", string.Empty));
    }

    [Test]
    public void Replace_RosterWithoutAnEarlierSlot_EmptiesItButEveryoneRemembersThePlayer()
    {
        // `everyone` is this project's record of who PLAYED, not the engine's table, so it keeps Bob.
        Dictionary<int, PlayerInfo> slots = [];
        Dictionary<int, PlayerInfo> everyone = [];
        RosterBuilder.Apply([Player(0, "Alice", 7), Player(1, "Bob", 9)], slots, everyone);

        RosterBuilder.Replace([Player(0, "Alice", 7)], slots, everyone);

        slots.Keys.ShouldBe([1]);
        everyone.Keys.ShouldBe([7, 9], ignoreOrder: true);
    }

    [Test]
    public void Replace_BaselinesWithoutAnEarlierClass_LeavesItWithNone()
    {
        DemoSchema schema = SyntheticPlayer.Schema();
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
        BaselineBuilder.Apply([new StringTableEntry(0, "7", [0x01])], decoder);

        BaselineBuilder.Replace([], decoder);

        decoder.Baseline(7).ShouldBeNull();
    }

    private static StringTableEntry Entry(int index, string text) => new(index, text, Array.Empty<byte>());

    private static StringTableEntry Style(int index, string pattern) =>
        new(index, index.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.ASCII.GetBytes(pattern + "\0"));

    private static StringTableEntry Player(int index, string name, int userId)
    {
        byte[] record = new byte[PlayerInfo.RecordBytes];
        Encoding.UTF8.GetBytes(name).CopyTo(record, 0);
        BitConverter.GetBytes((uint)userId).CopyTo(record, 32);

        return new StringTableEntry(index, index.ToString(System.Globalization.CultureInfo.InvariantCulture), record);
    }
}
