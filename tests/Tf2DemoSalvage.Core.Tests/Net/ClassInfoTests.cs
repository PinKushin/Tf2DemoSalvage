using System.Linq;
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
    private static int BitsFor(int count)
    {
        int bits = 0;
        while (1 << bits < count)
        {
            bits++;
        }

        return bits;
    }

    /// <summary>Writes a ClassInfo carrying explicit entries.</summary>
    private static void WriteInto(BitWriter writer, params (int Id, string Class, string Table)[] classes)
    {
        writer.Message(NetMessageType.ClassInfo)
            .Write((uint)classes.Length, 16)
            .Write(0, 1);   // entries follow rather than being created client-side

        foreach ((int id, string className, string tableName) in classes)
        {
            writer.Write((uint)id, BitsFor(classes.Length) + 1).String(className).String(tableName);
        }
    }

    private static byte[] Build(params (int Id, string Class, string Table)[] classes)
    {
        BitWriter writer = new();
        WriteInto(writer, classes);
        return writer.Build();
    }

    [Fact]
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

    [Fact]
    public void ClassInfo_ReportsTheClassIdBitWidthEntitiesWillUse()
    {
        // The entity decoder reads class ids at this width. It is derived, not transmitted,
        // so it is exposed here rather than recomputed at every use site.
        byte[] packet = Build(
            (0, "A", "DT_A"), (1, "B", "DT_B"), (2, "C", "DT_C"), (3, "D", "DT_D"));

        ClassInfoMessage info = NetMessageReader.Read(packet)
            .Messages[0].ShouldBeOfType<ClassInfoMessage>();

        // Four classes need two bits to index.
        info.ClassIdBits.ShouldBe(2);
    }

    [Fact]
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

    [Fact]
    public void ClassInfo_LeavesTheReaderPositionedForTheNextMessage()
    {
        BitWriter writer = new();
        WriteInto(writer, (0, "CTFPlayer", "DT_TFPlayer"), (1, "CWorld", "DT_World"));
        writer.NetTick(7, 0, 0);

        NetMessageReadResult result = NetMessageReader.Read(writer.Build());

        result.Messages[0].ShouldBeOfType<ClassInfoMessage>().Classes.Count.ShouldBe(2);
        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(7);
    }

    [Fact]
    public void ClassInfo_NoClasses_DecodesToAnEmptyList()
    {
        ClassInfoMessage info = NetMessageReader.Read(Build())
            .Messages[0].ShouldBeOfType<ClassInfoMessage>();

        info.Classes.ShouldBeEmpty();
        info.ClassCount.ShouldBe(0);
    }

    [Fact]
    public void ClassInfo_IsRememberedInDecodeState()
    {
        // Entity decoding needs the class list and its bit width, so it has to outlive the
        // packet that carried it.
        NetDecodeState state = new();

        NetMessageReader.Read(Build((0, "CTFPlayer", "DT_TFPlayer")), state);

        state.ClassInfo.ShouldNotBeNull().Classes.Count.ShouldBe(1);
    }
}
