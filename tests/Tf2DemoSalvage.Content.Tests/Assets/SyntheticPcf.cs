using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>A binary DMX <c>.pcf</c> of bare definitions, each with a text <c>material</c> to tell them apart.</summary>
/// <remarks>The layout <c>ParticleSystemsConformanceTests.Build</c> writes, measured from TF2's own files.</remarks>
internal static class SyntheticPcf
{
    /// <summary>One definition per pair, in order.</summary>
    public static byte[] Of(params (string Name, string Material)[] definitions)
    {
        List<byte> file = [.. Encoding.ASCII.GetBytes("<!-- dmx encoding binary 2 format pcf 1 -->\n"), 0];
        string[] strings = ["DmeParticleSystemDefinition", "material"];

        file.AddRange(BitConverter.GetBytes((ushort)strings.Length));

        foreach (string one in strings)
        {
            file.AddRange([.. Encoding.UTF8.GetBytes(one), 0]);
        }

        file.AddRange(BitConverter.GetBytes(definitions.Length));

        foreach ((string name, _) in definitions)
        {
            file.AddRange(BitConverter.GetBytes((ushort)0));
            file.AddRange([.. Encoding.UTF8.GetBytes(name), 0]);
            file.AddRange(new byte[16]);
        }

        foreach ((_, string material) in definitions)
        {
            file.AddRange(BitConverter.GetBytes(1));
            file.AddRange(BitConverter.GetBytes((ushort)1));
            file.Add((byte)DmxAttributeType.Text);
            file.AddRange([.. Encoding.UTF8.GetBytes(material), 0]);
        }

        return [.. file];
    }

    /// <summary>The <c>material</c> a definition written by <see cref="Of"/> carries.</summary>
    public static string Material(ParticleSystem system) => system.Parameters["material"].Text ?? string.Empty;
}
