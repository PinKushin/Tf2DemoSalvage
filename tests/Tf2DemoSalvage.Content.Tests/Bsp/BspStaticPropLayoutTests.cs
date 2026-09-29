using System;
using System.Buffers.Binary;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// <c>m_Flags</c> and <c>m_LightingOrigin</c> read byte by byte from hand-built placements (B427).
/// </summary>
/// <remarks>
/// Offsets from `public/gamebspfile.h`: V6 (:186-204, 64 bytes) keeps <c>m_Flags</c> as a byte at 31;
/// <c>StaticPropLump_t</c> (:206-225, version 10, 72 bytes) moves it to a <c>uint</c> at 64 and leaves
/// 31 as padding; version 11 appends a scale (76). <c>m_LightingOrigin</c> is at 44 in all of them.
/// Each fixture writes a decoy into the OTHER version's flags slot, so reading the wrong offset gives a
/// different number rather than the same one.
/// </remarks>
public sealed class BspStaticPropLayoutTests
{
    [TestCase(6, 64, 0x02, 0x10)]
    [TestCase(10, 72, 0x122, 0xFF)]
    [TestCase(11, 76, 0x122, 0xFF)]
    public void ReadPayload_FlagsAtThisVersionsOffset_AreRead(int version, int stride, int flags, int decoy)
    {
        byte[] record = new byte[stride];

        if (version >= 10)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(64), (uint)flags);
            record[31] = (byte)decoy;
        }
        else
        {
            record[31] = (byte)flags;
        }

        BspStaticProp prop = BspStaticProps.ReadPayload(Payload(record), version).ShouldHaveSingleItem();

        prop.Flags.ShouldBe(flags);
        prop.UsesLightingOrigin.ShouldBeTrue();
    }

    [TestCase(6, 64)]
    [TestCase(10, 72)]
    [TestCase(11, 76)]
    public void ReadPayload_LightingOrigin_IsTheThreeFloatsAt44(int version, int stride)
    {
        byte[] record = new byte[stride];
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(40), -1f); // m_FadeMaxDist, the neighbour
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(44), 12.5f);
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(48), -300f);
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(52), 64f);
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(56), 7f); // m_flForcedFadeScale, the other

        BspStaticProp prop = BspStaticProps.ReadPayload(Payload(record), version).ShouldHaveSingleItem();

        prop.LightingOrigin.ShouldBe((12.5f, -300f, 64f));
        prop.UsesLightingOrigin.ShouldBeFalse("no flag was written");
    }

    [TestCase(6, 64)]
    [TestCase(10, 72)]
    [TestCase(11, 76)]
    public void ReadPayload_FadeDistances_AreTheFloatsAt36And40(int version, int stride)
    {
        byte[] record = new byte[stride];
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(32), 3); // m_Skin, the neighbour below
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(36), 1200f);
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(40), 1500f);
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(44), 9f); // m_LightingOrigin, above

        BspStaticProp prop = BspStaticProps.ReadPayload(Payload(record), version).ShouldHaveSingleItem();

        prop.FadeMinimum.ShouldBe(1200f);
        prop.FadeMaximum.ShouldBe(1500f);
    }

    /// <remarks>`m_flForcedFadeScale`, after `m_LightingOrigin` from `StaticPropLumpV5_t` on (`gamebspfile.h`), B432.</remarks>
    [TestCase(5, 60)]
    [TestCase(6, 64)]
    [TestCase(10, 72)]
    [TestCase(11, 76)]
    public void ReadPayload_ForcedFadeScale_IsTheFloatAt56(int version, int stride)
    {
        byte[] record = new byte[stride];
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(52), 7f); // m_LightingOrigin.z, below
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(56), 2.5f);

        BspStaticProps.ReadPayload(Payload(record), version).ShouldHaveSingleItem().ForcedFadeScale.ShouldBe(2.5f);
    }

    /// <remarks>Version 4 has no such field; the neutral scale of 1 divides the screen width by nothing.</remarks>
    [Test]
    public void ReadPayload_ForcedFadeScaleAtVersion4_IsOne()
    {
        byte[] record = new byte[56];
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(52), 7f);

        BspStaticProps.ReadPayload(Payload(record), 4).ShouldHaveSingleItem().ForcedFadeScale.ShouldBe(1f);
    }

    /// <summary>One dictionary entry, no leaves, and the one placement.</summary>
    private static byte[] Payload(byte[] record)
    {
        byte[] payload = new byte[4 + BspStaticProps.ModelNameBytes + 4 + 4 + record.Length];
        Span<byte> span = payload;

        BinaryPrimitives.WriteInt32LittleEndian(span, 1);
        Encoding.UTF8.GetBytes("models/a.mdl").CopyTo(span[4..]);
        int at = 4 + BspStaticProps.ModelNameBytes;
        BinaryPrimitives.WriteInt32LittleEndian(span[at..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(span[(at + 4)..], 1);
        record.CopyTo(span[(at + 8)..]);

        return payload;
    }
}
