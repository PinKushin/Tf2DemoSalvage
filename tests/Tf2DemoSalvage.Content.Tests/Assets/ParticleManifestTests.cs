using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>Which <c>.pcf</c> files the manifest names (B415).</summary>
/// <remarks>
/// **Synthetic, because every claim here is about the READER.** Whether TF2's shipped manifest lists
/// `explosion.pcf` is a claim about Valve, answered by `game-file particles/particles_manifest.txt` and recorded
/// in `docs/findings/58` — and a test that asserted it would be asserting that the owner's install is stock.
/// </remarks>
public sealed class ParticleManifestTests
{
    /// <remarks>
    /// **The `!` is a precache marker, not part of the path** (`particle_parse.cpp:130`). Ninety-odd of TF2's
    /// hundred entries carry one, so a reader that kept it finds nothing at all — and the symptom is a manifest
    /// that appears to name only files the game does not ship.
    /// </remarks>
    [Test]
    public void Files_APrecacheMarker_IsStrippedFromThePath()
    {
        Files("""
            particles_manifest
            {
                "file"  "!particles/explosion.pcf"
                "file"  "particles/level_fx.pcf"
            }
            """)
            .ShouldBe(["particles/explosion.pcf", "particles/level_fx.pcf"]);
    }

    /// <remarks>
    /// **Order is kept because the engine's is**: `ReadParticleConfigFile` runs down the list, so which file
    /// declares a name first is a fact about the manifest.
    /// </remarks>
    [Test]
    public void Files_SeveralEntries_AreInTheManifestsOwnOrder()
    {
        Files("""
            particles_manifest
            {
                "file"  "a.pcf"
                "file"  "b.pcf"
                "file"  "c.pcf"
            }
            """)
            .ShouldBe(["a.pcf", "b.pcf", "c.pcf"]);
    }

    /// <remarks>
    /// **A repeat is kept, because Valve's loop does not deduplicate** — and the shipped file really does list
    /// `buildingdamage.pcf` twice. What a duplicate NAME means belongs to whoever merges the systems.
    /// </remarks>
    [Test]
    public void Files_AFileListedTwice_IsKeptTwice()
    {
        Files("""
            particles_manifest
            {
                "file"  "!particles/buildingdamage.pcf"
                "file"  "!particles/buildingdamage.pcf"
            }
            """)
            .Count.ShouldBe(2);
    }

    /// <remarks>
    /// **Only `file` is a path.** Valve warns on anything else and skips it (`particle_parse.cpp:79`); taking
    /// every key would hand the loader a path that is not one.
    /// </remarks>
    [Test]
    public void Files_AKeyThatIsNotFile_IsIgnored()
    {
        Files("""
            particles_manifest
            {
                "file"     "a.pcf"
                "nonsense" "b.pcf"
            }
            """)
            .ShouldBe(["a.pcf"]);
    }

    /// <remarks>
    /// A machine with no TF2 has no manifest, which is every CI run. Empty rather than an exception: the viewer
    /// draws models and no effects, which is what it already does for a missing sprite sheet.
    /// </remarks>
    [Test]
    public void Files_WithNoManifest_IsEmpty()
    {
        ParticleManifest.Files(_ => null).ShouldBeEmpty();
    }

    /// <summary>The manifest's entries, from a document written here.</summary>
    private static IReadOnlyList<string> Files(string manifest)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(manifest);

        return ParticleManifest.Files(path =>
            string.Equals(path, ParticleManifest.Path, StringComparison.Ordinal) ? bytes : null);
    }
}
