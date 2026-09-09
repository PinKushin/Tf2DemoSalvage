using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The binary DMX container TF2's particle definitions ship in (B373).
/// </summary>
/// <remarks>
/// **Synthetic, and that is what makes it stronger than reading a `.pcf`** (D38). A test over a
/// shipped file cannot know the right answer and can only compare two readings of the same bytes;
/// these tests WRITE the bytes, so the expected value is the one the test put there.
///
/// **The types are Valve's** — `src/public/datamodel/dmattributetypes.h:66` — and the LAYOUT is
/// measured, because the SDK ships dmxloader's headers and not its implementation. The layout the
/// fixture below encodes is the one read out of `particles/rockettrail.pcf`, recorded in
/// <see cref="DmxFile"/>'s own remarks.
/// </remarks>
[TestFixture]
public sealed class DmxFileConformanceTests
{
    /// <summary>The header TF2's particle files carry, verbatim.</summary>
    private const string Header = "<!-- dmx encoding binary 2 format pcf 1 -->\n";

    [Test]
    public void Read_AFileWithOneElement_ReportsItsTypeNameAndAttributes()
    {
        // One element of type "DmeParticleSystemDefinition" named "rockettrail", carrying an int
        // and a float. Every value here is chosen by this test, which is the point.
        byte[] file = Build(
            ["DmeParticleSystemDefinition", "maximum_particles", "radius"],
            [(TypeIndex: 0, Name: "rockettrail", Attributes: new (int, byte, byte[])[]
            {
                (1, (byte)DmxAttributeType.Whole, BitConverter.GetBytes(400)),
                (2, (byte)DmxAttributeType.Real, BitConverter.GetBytes(7.5f)),
            })]);

        IReadOnlyList<DmxElement> elements = DmxFile.Read(file);

        elements.Count.ShouldBe(1);
        elements[0].Type.ShouldBe("DmeParticleSystemDefinition");
        elements[0].Name.ShouldBe("rockettrail");

        elements[0].Attributes["maximum_particles"].Number.ShouldBe(400d);
        elements[0].Attributes["radius"].Number.ShouldBe(7.5d, 0.0001d);
    }

    [Test]
    public void Read_AnElementTypeIndex_NamesTheStringTableEntryNotTheElement()
    {
        // **The asymmetry a reader gets wrong first**, and it is visible in the shipped bytes: an
        // element's TYPE is a string-table index and its NAME is written inline. A reader that
        // treats both as indices, or both as inline, produces plausible garbage rather than
        // failing — so this pins the pair with a table whose entries could be confused.
        byte[] file = Build(
            ["first", "second", "third"],
            [(TypeIndex: 2, Name: "first", Attributes: [])]);

        IReadOnlyList<DmxElement> elements = DmxFile.Read(file);

        elements.Count.ShouldBe(1);

        // The type is the table's THIRD entry, and the name is the literal that follows it - which
        // happens to equal the table's first entry, so a reader confusing the two would return
        // "first" for the type and pass a weaker test.
        elements[0].Type.ShouldBe("third");
        elements[0].Name.ShouldBe("first");
    }

    [Test]
    public void Read_AnArrayAttribute_IsItsElementTypePlusFourteen()
    {
        // `AT_ELEMENT_ARRAY = AT_FIRST_ARRAY_TYPE` sits immediately after `AT_VMATRIX`, so an array
        // type is its element type + 14 rather than a separate enumeration. A `float_array` is
        // therefore type 3 + 14 = 17, and its payload is an int32 count then that many floats.
        byte[] payload = [.. BitConverter.GetBytes(3), .. BitConverter.GetBytes(1.5f),
            .. BitConverter.GetBytes(2.5f), .. BitConverter.GetBytes(3.5f)];

        byte[] file = Build(
            ["DmeParticleOperator", "curve"],
            [(TypeIndex: 0, Name: "fade", Attributes: new (int, byte, byte[])[]
            {
                (1, (byte)DmxAttributeType.Real + 14, payload),
            })]);

        IReadOnlyList<DmxElement> elements = DmxFile.Read(file);

        DmxValue curve = elements[0].Attributes["curve"];

        curve.Numbers.ShouldNotBeNull();
        curve.Numbers.Count.ShouldBe(3);
        curve.Numbers[0].ShouldBe(1.5d, 0.0001d);
        curve.Numbers[2].ShouldBe(3.5d, 0.0001d);
    }

    [Test]
    public void Read_AnElementArray_CarriesTheIndicesItReferences()
    {
        // How a particle system names its operators: `operators` is an element array holding
        // indices into the file's own element list, which is why `DmxFile.Read` must preserve
        // order. Two elements, the first referencing the second.
        byte[] payload = [.. BitConverter.GetBytes(1), .. BitConverter.GetBytes(1)];

        byte[] file = Build(
            ["DmeParticleSystemDefinition", "DmeParticleOperator", "operators"],
            [
                (TypeIndex: 0, Name: "trail", Attributes: new (int, byte, byte[])[]
                {
                    (2, (byte)DmxAttributeType.Element + 14, payload),
                }),
                (TypeIndex: 1, Name: "lifespan", Attributes: []),
            ]);

        IReadOnlyList<DmxElement> elements = DmxFile.Read(file);

        elements.Count.ShouldBe(2);

        DmxValue operators = elements[0].Attributes["operators"];

        operators.Elements.ShouldNotBeNull();
        operators.Elements.Count.ShouldBe(1);
        operators.Elements[0].ShouldBe(1);

        elements[operators.Elements[0]].Name.ShouldBe("lifespan");
    }

    [Test]
    public void Read_AnEncodingThisDoesNotRead_IsRefusedRatherThanGuessedAt()
    {
        // **A refusal, not a best effort.** `binary 5` and the text encoding lay their elements out
        // differently; reading one as the other resynchronises onto rubbish and reports it as data,
        // which is worse than reporting nothing. The control is the test above: the same builder
        // with the supported header does parse.
        byte[] file = Build(
            ["DmeElement"],
            [(TypeIndex: 0, Name: "untitled", Attributes: [])],
            header: "<!-- dmx encoding binary 5 format pcf 2 -->\n");

        DmxFile.Read(file).ShouldBeEmpty();
    }

    [Test]
    public void Read_AFileTruncatedMidAttribute_KeepsWhatItReadRatherThanThrowing()
    {
        // A `.pcf` is a stranger's file (D32). Half an attribute must cost that attribute, not the
        // whole read and not an exception out of arithmetic.
        byte[] whole = Build(
            ["DmeParticleOperator", "radius"],
            [(TypeIndex: 0, Name: "fade", Attributes: new (int, byte, byte[])[]
            {
                (1, (byte)DmxAttributeType.Real, BitConverter.GetBytes(7.5f)),
            })]);

        byte[] cut = whole[..(whole.Length - 2)];

        IReadOnlyList<DmxElement> elements = DmxFile.Read(cut);

        elements.Count.ShouldBe(1);
        elements[0].Name.ShouldBe("fade");
        elements[0].Attributes.ShouldNotContainKey("radius");
    }

    /// <summary>Writes a binary DMX file in the layout measured from TF2's own particle files.</summary>
    /// <remarks>
    /// **The fixture IS the specification here**, so it is written out in full rather than
    /// generated: header, uint16 string count, the strings, int32 element count, each element's
    /// uint16 type index / inline name / sixteen-byte id, then each element's attributes.
    /// </remarks>
    private static byte[] Build(
        string[] strings,
        (int TypeIndex, string Name, (int NameIndex, byte Type, byte[] Payload)[] Attributes)[] elements,
        string header = Header)
    {
        List<byte> file = [.. Encoding.ASCII.GetBytes(header), 0];

        file.AddRange(BitConverter.GetBytes((ushort)strings.Length));

        foreach (string one in strings)
        {
            file.AddRange(Encoding.UTF8.GetBytes(one));
            file.Add(0);
        }

        file.AddRange(BitConverter.GetBytes(elements.Length));

        foreach ((int type, string name, _) in elements)
        {
            file.AddRange(BitConverter.GetBytes((ushort)type));
            file.AddRange(Encoding.UTF8.GetBytes(name));
            file.Add(0);
            file.AddRange(new byte[16]);
        }

        foreach ((_, _, (int NameIndex, byte Type, byte[] Payload)[] attributes) in elements)
        {
            file.AddRange(BitConverter.GetBytes(attributes.Length));

            foreach ((int named, byte type, byte[] payload) in attributes)
            {
                file.AddRange(BitConverter.GetBytes((ushort)named));
                file.Add(type);
                file.AddRange(payload);
            }
        }

        return [.. file];
    }
}
