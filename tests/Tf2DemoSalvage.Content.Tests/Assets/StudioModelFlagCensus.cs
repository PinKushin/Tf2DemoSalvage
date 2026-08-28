using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// How many models TF2 actually ships with each header flag.
/// </summary>
/// <remarks>
/// **Run to size a change before making it, which is the point of a census.** Two-pass drawing was
/// implemented against every model in the scene before anything asked which models the engine would
/// split; this reports how many there are, so "we now honour <c>$mostlyopaque</c>" can be stated
/// with a denominator instead of as a capability.
///
/// **The number that matters is <c>TRANSLUCENT_TWOPASS</c> against the models a match actually
/// draws**, not against the whole archive — a few thousand unused props dilute it either way. The
/// report prints both the total and the names, so a specific model can be checked by eye.
///
/// Explicit, because it reports rather than asserts, and because a full archive scan is far slower
/// than anything belonging in the gate.
/// </remarks>
[Explicit("Diagnostic: reports which studiohdr flags TF2's shipped models carry.")]
public sealed class StudioModelFlagCensus
{
    [Test]
    public void ReportFlagsAcrossEveryShippedModel()
    {
        if (!GameInstall.Available)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        List<VpkArchive> archives = [.. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
            .Select(name => Path.Combine(GameInstall.Require(), name))
            .Where(File.Exists)
            .Select(VpkArchive.Open)];

        List<string> paths = [.. archives
            .SelectMany(archive => archive.Paths)
            .Where(path => path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)];

        int read = 0;
        int twoPass = 0;
        int forceOpaque = 0;
        int staticProp = 0;

        List<string> twoPassNames = [];

        foreach (string path in paths)
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

            int flags = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(StudioLayout.HeaderFlagsOffset));

            if ((flags & StudioModelFlags.TranslucentTwoPass) != 0)
            {
                twoPass++;

                // **All of them, not a sample.** The first version printed sixty and the question
                // that mattered — "is the sticky launcher one of these?" — fell off the end of the
                // list. A census that truncates cannot answer a question about a specific member,
                // which is most of what a census gets asked.
                twoPassNames.Add(path);
            }

            if ((flags & StudioModelFlags.ForceOpaque) != 0)
            {
                forceOpaque++;
            }

            if ((flags & StudioModelFlags.StaticProp) != 0)
            {
                staticProp++;
            }
        }

        TestContext.Out.WriteLine($"{read:N0} models read of {paths.Count:N0} paths");
        TestContext.Out.WriteLine(
            $"  TRANSLUCENT_TWOPASS  {twoPass,6:N0}  ({(double)twoPass / read:P2})");
        TestContext.Out.WriteLine(
            $"  FORCE_OPAQUE         {forceOpaque,6:N0}  ({(double)forceOpaque / read:P2})");
        TestContext.Out.WriteLine(
            $"  STATIC_PROP          {staticProp,6:N0}  ({(double)staticProp / read:P2})");

        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine("two-pass models, every one:");

        foreach (string name in twoPassNames)
        {
            TestContext.Out.WriteLine($"  {name}");
        }

        read.ShouldBeGreaterThan(0, "the archives must contain models, or this measured nothing");
    }
}
