using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Which shipped models carry a studiohdr flag, every one named: `$ambientboost`'s for B672.</summary>
/// <remarks>
/// Reads the header flags of every `.mdl` in the install's VPKs at <see cref="StudioLayout.HeaderFlagsOffset"/>. The control is
/// `STUDIOHDR_FLAGS_STATIC_PROP`, counted beside the asked flag: a word read from the wrong offset would find none of those either.
/// </remarks>
public sealed class ModelFlagProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "model-flag";

    /// <inheritdoc/>
    public string Summary => "every shipped model carrying a studiohdr flag, with $staticprop's count as the control: model-flag <hex mask>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 ||
            !int.TryParse(arguments[0].Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int mask) ||
            new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } game)
        {
            output.WriteLine("usage: model-flag <hex mask>, with TF2 installed");
            return;
        }

        List<VpkArchive> archives = [.. Directory.GetFiles(game, "*_dir.vpk").Select(VpkArchive.Open)];
        int read = 0;
        int staticProps = 0;
        List<string> carrying = [];

        foreach (string path in archives.SelectMany(static archive => archive.Paths)
            .Where(static path => path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal))
        {
            byte[]? bytes = null;

            foreach (VpkArchive archive in archives)
            {
                bytes ??= archive.ReadFile(path);
            }

            if (bytes is null || bytes.Length < StudioLayout.HeaderFlagsOffset + sizeof(int))
            {
                continue;
            }

            read++;
            int flags = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(StudioLayout.HeaderFlagsOffset));
            staticProps += (flags & StudioModelFlags.StaticProp) != 0 ? 1 : 0;

            if ((flags & mask) != 0)
            {
                carrying.Add(path);
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{read} models read; $staticprop (the control) {staticProps}; carrying 0x{mask:x}: {carrying.Count}"));

        foreach (string path in carrying)
        {
            output.WriteLine("  " + path);
        }
    }
}
