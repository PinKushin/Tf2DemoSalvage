using System.Linq;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests for <c>svc_ClassInfo</c>, written before the decoder this time.
/// </summary>
/// <remarks>
/// The class list is what entity updates index into: an entity's class id is read with a bit
/// width derived from how many classes exist, so this message sizes a field in every later
/// packet. Getting the count wrong would not fail here — it would misread every entity.
/// </remarks>
public sealed class ClassInfoTests
{
    /// <summary>
    /// The width entry ids are written at, stated once for the fixtures that do not pin it.
    /// </summary>
    /// <remarks>
    /// Deliberately the production function rather than a second copy. A fixture helper that
    /// recomputes the width shares whatever the reader believes, so the two agree by construction
    /// and the test cannot fail — which is how the ceiling form survived here.
    /// <see cref="ClassInfo_ReadsEntryIdsAtFloorLogTwoPlusOne"/> states the widths as literals and
    /// is what actually pins them.
    /// </remarks>
    private static int BitsFor(int count) => WireWidths.ClassId(count);

    /// <summary>Writes a ClassInfo carrying explicit entries.</summary>
    private static void WriteInto(BitWriter writer, params (int Id, string Class, string Table)[] classes)
    {
        writer.Message(NetMessageType.ClassInfo)
            .Write((uint)classes.Length, 16)
            .Write(0, 1);   // entries follow rather than being created client-side

        foreach ((int id, string className, string tableName) in classes)
        {
            writer.Write((uint)id, BitsFor(classes.Length)).String(className).String(tableName);
        }
    }

    private static byte[] Build(params (int Id, string Class, string Table)[] classes)
    {
        BitWriter writer = new();
        WriteInto(writer, classes);
        return writer.Build();
    }
    [TestCase(3, 2)]
    [TestCase(4, 3)]
    [TestCase(256, 9)]
    [TestCase(362, 9)]
    public void ClassInfo_ReadsEntryIdsAtFloorLogTwoPlusOne(int count, int idBits)
    {
        // Same width the entity decoder already uses - floor(log2(n)) + 1, the engine's
        // GetServerClassBits - and this message is where it is first established. Two formulas
        // for one wire quantity had drifted apart in this file: the entity decoder's, corrected
        // against a real demo's 362 classes, and a ceiling form here.
        //
        // They disagree on exactly two shapes, and both rows are present:
        //   3 classes - ceiling says 2, ceiling-plus-one (what the reader used) says 3, wire is 2
        //   256 classes - a power of two, where ceiling says 8 and the wire is 9
        // 256 is not hypothetical: it is max_classes for protocol 15, build 3862.
        //
        // The fixture states the width as a literal rather than calling the helper the reader
        // agrees with. The old test could not fail, because fixture and reader shared one wrong
        // formula - each confirmed the other and the wire was never consulted.
        //
        // A net_tick follows as the control. A wrong width leaves the reader mid-message, so the
        // tick either fails to appear or comes back as something other than 4242.
        BitWriter writer = new();
        writer.Message(NetMessageType.ClassInfo).Write((uint)count, 16).Write(0, 1);
        foreach (int id in Enumerable.Range(0, count))
        {
            writer.Write((uint)id, idBits).String("C").String("DT");
        }

        writer.NetTick(4242, 0, 0);

        NetMessageReadResult result = NetMessageReader.Read(writer.Build());
        ClassInfoMessage info = result.Messages[0].ShouldBeOfType<ClassInfoMessage>();

        info.Classes.Count.ShouldBe(count);
        info.Classes[^1].Id.ShouldBe(count - 1);
        info.ClassIdBits.ShouldBe(idBits);
        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(4242);
    }

    [Test]
    public void ClassInfo_DecodesEveryClass()
    {
        byte[] packet = Build(
            (0, "CTFPlayer", "DT_TFPlayer"),
            (1, "CObjectSentrygun", "DT_ObjectSentrygun"),
            (2, "CTFProjectile_Rocket", "DT_TFProjectile_Rocket"));

        NetMessageReadResult result = NetMessageReader.Read(packet);

        ClassInfoMessage info = result.Messages[0].ShouldBeOfType<ClassInfoMessage>();
        info.CreateOnClient.ShouldBeFalse();
        info.Classes.Count.ShouldBe(3);
        info.Classes[0].Id.ShouldBe(0);
        info.Classes[0].ClassName.ShouldBe("CTFPlayer");
        info.Classes[0].TableName.ShouldBe("DT_TFPlayer");
        info.Classes[2].ClassName.ShouldBe("CTFProjectile_Rocket");
    }

    [Test]
    public void ClassInfo_ReportsTheClassIdBitWidthEntitiesWillUse()
    {
        // The entity decoder reads class ids at this width. It is derived, not transmitted,
        // so it is exposed here rather than recomputed at every use site.
        byte[] packet = Build(
            (0, "A", "DT_A"), (1, "B", "DT_B"), (2, "C", "DT_C"), (3, "D", "DT_D"));

        ClassInfoMessage info = NetMessageReader.Read(packet)
            .Messages[0].ShouldBeOfType<ClassInfoMessage>();

        // Four classes need three bits: floor(log2(4)) + 1, the count's width in binary.
        info.ClassIdBits.ShouldBe(3);
    }

    [Test]
    public void ClassInfo_CreateOnClientFlag_CarriesNoEntries()
    {
        // When the server says the client should build the list itself, nothing follows the
        // flag. Reading entries anyway would consume bits belonging to the next message.
        BitWriter writer = new();
        writer.Message(NetMessageType.ClassInfo).Write(275, 16).Write(1, 1);
        writer.NetTick(99, 0, 0);

        NetMessageReadResult result = NetMessageReader.Read(writer.Build());

        ClassInfoMessage info = result.Messages[0].ShouldBeOfType<ClassInfoMessage>();
        info.Classes.ShouldBeEmpty();
        info.CreateOnClient.ShouldBeTrue();
        info.ClassCount.ShouldBe(275);

        // The width is still known from the count, even with no entries transmitted.
        info.ClassIdBits.ShouldBe(9);

        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(99);
    }

    [Test]
    public void ClassInfo_LeavesTheReaderPositionedForTheNextMessage()
    {
        BitWriter writer = new();
        WriteInto(writer, (0, "CTFPlayer", "DT_TFPlayer"), (1, "CWorld", "DT_World"));
        writer.NetTick(7, 0, 0);

        NetMessageReadResult result = NetMessageReader.Read(writer.Build());

        result.Messages[0].ShouldBeOfType<ClassInfoMessage>().Classes.Count.ShouldBe(2);
        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(7);
    }

    [Test]
    public void ClassInfo_NoClasses_DecodesToAnEmptyList()
    {
        ClassInfoMessage info = NetMessageReader.Read(Build())
            .Messages[0].ShouldBeOfType<ClassInfoMessage>();

        info.Classes.ShouldBeEmpty();
        info.ClassCount.ShouldBe(0);
    }

    [Test]
    public void ClassInfo_IsRememberedInDecodeState()
    {
        // Entity decoding needs the class list and its bit width, so it has to outlive the
        // packet that carried it.
        NetDecodeState state = new();

        NetMessageReader.Read(Build((0, "CTFPlayer", "DT_TFPlayer")), state);

        state.ClassInfo.ShouldNotBeNull().Classes.Count.ShouldBe(1);
    }
}
