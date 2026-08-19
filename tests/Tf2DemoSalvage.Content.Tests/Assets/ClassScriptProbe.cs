using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Where the player class scripts actually live in a TF2 install.
/// </summary>
/// <remarks>
/// A probe, not a test: it asserts nothing and exists to be run by hand when a path guess fails.
/// Written because <c>scripts/playerclasses/scout.txt</c> — the path <c>tf_classdata.cpp</c>
/// implies — was not found in <c>tf2_misc_dir.vpk</c>, and guessing a second time would have been
/// the same mistake twice.
/// </remarks>
public sealed class ClassScriptProbe
{
    [Test]
    [Explicit("Diagnostic. Run by hand when the class scripts move.")]
    public void ClassScripts_TheirLocation_IsReported()
    {
        string tf = Environment.GetEnvironmentVariable("TF2_FOLDER")
            ?? @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf";

        string[] archives = Directory.GetFiles(tf, "*_dir.vpk");

        // The probe reports rather than judges, but an install with no archives at all would
        // print nothing and read as "no matches anywhere", which is a different conclusion.
        archives.ShouldNotBeEmpty();

        foreach (string archive in archives)
        {
            VpkArchive vpk = VpkArchive.Open(archive);

            string[] hits = [.. vpk.Paths
                .Where(path => path.Contains("playerclass", StringComparison.OrdinalIgnoreCase))
                .Take(15)];

            TestContext.Out.WriteLine($"{Path.GetFileName(archive)}: {hits.Length} matches");

            foreach (string hit in hits)
            {
                TestContext.Out.WriteLine("    " + hit);
            }
        }
    }
}
