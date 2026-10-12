using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every shipped `.phy`'s text as production reads it, one digest a file — for comparing two readers (B532).</summary>
/// <remarks>
/// **A before-and-after instrument**: run it, change the reader, run it again, and diff the two outputs. Each line is
/// the file, what its text declared — solids, constraints, collision pairs, break pieces — and a hash over every field
/// of every one of them, so a value that changed shows as a changed line even when no count did. The totals at the end
/// are the control: files read and files that threw.
/// </remarks>
public sealed class PhyCensusProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "phy-census";

    /// <inheritdoc/>
    public string Summary => "every shipped .phy's text section as read, one digest line a file, to diff two readers: phy-census [nocase]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } game)
        {
            output.WriteLine("No TF2 install found, so there is nothing to read.");
            return;
        }

        GameArchives archives = GameArchives.Open(game);
        int read = 0;
        int threw = 0;
        int withText = 0;

        foreach (string path in archives.Paths()
                     .Where(static path => path.EndsWith(".phy", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            if (archives.Read(path) is not { } bytes)
            {
                continue;
            }

            PhysicsModel model;

            try
            {
                model = PhysicsModel.Read(bytes);
            }
            catch (InvalidDataException refused)
            {
                // Counted and named: a file the reader refuses is a line of its own, not a silence.
                threw++;
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{path} | REFUSED {refused.Message}"));
                continue;
            }

            read++;

            StringBuilder fields = new();

            fields.AppendJoin('\n', model.Solids).Append('\n');
            fields.AppendJoin('\n', model.Constraints).Append('\n');
            fields.Append(model.CollisionRules?.SelfCollisions).AppendJoin(',', model.CollisionRules?.Pairs ?? []).Append('\n');
            fields.AppendJoin('\n', model.BreakPieces);

            int pairs = model.CollisionRules?.Pairs.Count ?? 0;

            withText += model.Solids.Count + model.Constraints.Count + pairs + model.BreakPieces.Count > 0 ? 1 : 0;

            // `phy-census nocase` hashes the fields in one case, to tell a change of case from a change of value.
            string hashed = arguments.Count > 0 && arguments[0].Equals("nocase", StringComparison.OrdinalIgnoreCase)
                ? fields.ToString().ToUpperInvariant()
                : fields.ToString();

            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashed)), 0, 6);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{path} | solids {model.Solids.Count} constraints {model.Constraints.Count} pairs {pairs} pieces {model.BreakPieces.Count} | {hash}"));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"{read:N0} read, {withText:N0} with text that declared something, {threw:N0} refused"));
    }
}
