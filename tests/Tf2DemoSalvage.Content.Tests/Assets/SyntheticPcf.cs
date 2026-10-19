using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>A binary DMX <c>.pcf</c> of bare definitions, each with a text <c>material</c> to tell them apart.</summary>
/// <remarks>
/// The layout <c>ParticleSystemsConformanceTests.Build</c> writes, measured from TF2's own files: element 0 is the root,
/// a <c>DmeElement</c> named <c>untitled</c> whose <c>particleSystemDefinitions</c> array lists the definitions.
/// </remarks>
internal static class SyntheticPcf
{
    private const int DefinitionType = 0;
    private const int MaterialName = 1;
    private const int RootType = 2;
    private const int DefinitionsName = 3;

    /// <summary>A rooted file whose array lists the definitions in the order given.</summary>
    public static byte[] Of(params (string Name, string Material)[] definitions) =>
        Rooted([.. Enumerable.Range(0, definitions.Length)], definitions);

    /// <summary>A rooted file: definitions stored in the order given, the root's array naming them in <paramref name="order"/>.</summary>
    /// <param name="order">Indices into <paramref name="definitions"/>, in array order.</param>
    /// <param name="definitions">The definitions, in element-table order after the root.</param>
    public static byte[] Rooted(int[] order, params (string Name, string Material)[] definitions)
    {
        List<byte> payload = [.. BitConverter.GetBytes(order.Length)];

        foreach (int one in order)
        {
            payload.AddRange(BitConverter.GetBytes(one + 1));
        }

        return Write(
        [
            (RootType, "untitled", [(DefinitionsName, (byte)((int)DmxAttributeType.Element + 14), [.. payload])]),
            .. definitions.Select(Definition),
        ]);
    }

    /// <summary>A file whose ROOT is itself a definition, followed by more definitions no array names.</summary>
    public static byte[] RootIsADefinition(params (string Name, string Material)[] definitions) =>
        Write([.. definitions.Select(Definition)]);

    /// <summary>The <c>material</c> a definition written here carries.</summary>
    public static string Material(ParticleSystem system) => system.Parameters["material"].Text ?? string.Empty;

    private static (int Type, string Name, (int Named, byte Kind, byte[] Payload)[] Attributes) Definition(
        (string Name, string Material) definition) =>
        (DefinitionType, definition.Name,
            [(MaterialName, (byte)DmxAttributeType.Text, [.. Encoding.UTF8.GetBytes(definition.Material), 0])]);

    private static byte[] Write((int Type, string Name, (int Named, byte Kind, byte[] Payload)[] Attributes)[] elements)
    {
        List<byte> file = [.. Encoding.ASCII.GetBytes("<!-- dmx encoding binary 2 format pcf 1 -->\n"), 0];
        string[] strings = ["DmeParticleSystemDefinition", "material", "DmeElement", "particleSystemDefinitions"];

        file.AddRange(BitConverter.GetBytes((ushort)strings.Length));

        foreach (string one in strings)
        {
            file.AddRange([.. Encoding.UTF8.GetBytes(one), 0]);
        }

        file.AddRange(BitConverter.GetBytes(elements.Length));

        foreach ((int type, string name, _) in elements)
        {
            file.AddRange(BitConverter.GetBytes((ushort)type));
            file.AddRange([.. Encoding.UTF8.GetBytes(name), 0]);
            file.AddRange(new byte[16]);
        }

        foreach ((_, _, (int Named, byte Kind, byte[] Payload)[] attributes) in elements)
        {
            file.AddRange(BitConverter.GetBytes(attributes.Length));

            foreach ((int named, byte kind, byte[] payload) in attributes)
            {
                file.AddRange(BitConverter.GetBytes((ushort)named));
                file.Add(kind);
                file.AddRange(payload);
            }
        }

        return [.. file];
    }
}
