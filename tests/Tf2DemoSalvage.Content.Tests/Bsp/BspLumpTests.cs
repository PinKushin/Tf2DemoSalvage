using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Every lump number this project uses, checked against Valve's declaration of it.
/// </summary>
/// <remarks>
/// **This is the test the constants exist for.** A lump index is the highest-consequence magic
/// number in a format reader and the least likely to announce a mistake: off by one, it reads
/// another lump's bytes as its own — real data, wrong meaning, no error — and what surfaces is a map
/// that looks subtly strange somewhere else entirely.
///
/// **Our constant against the engine's, not a literal against the engine's.** Asserting
/// <c>45.ShouldBe(45)</c> in a test proves nothing about the code: someone can change the reader and
/// the test still passes. Every assertion below reads <see cref="BspLumpIndex"/> — the values the
/// readers actually use — so editing one fails here.
///
/// Skips when the SDK is not checked out, like everything else that needs it. Set
/// <c>SOURCE_SDK</c> to point at one.
/// </remarks>
public sealed class BspLumpTests
{
    [Test]
    public void EveryLumpWeUse_MatchesTheEnginesDeclaration()
    {
        Dictionary<string, int> engine = Declared();

        // The pairs are (what Valve calls it, what this project uses). Written out rather than
        // matched by name, because the mapping is the claim: LumpLeafAmbientLighting is 56 and
        // LumpLeafAmbientIndex is 52, and a reader that swapped them would still be "using the
        // right numbers" by any test that only checked the set.
        (string Name, int Ours)[] claims =
        [
            ("LUMP_ENTITIES", BspLumpIndex.Entities),
            ("LUMP_PLANES", BspLumpIndex.Planes),
            ("LUMP_TEXDATA", BspLumpIndex.Texdata),
            ("LUMP_VERTEXES", BspLumpIndex.Vertexes),
            ("LUMP_VISIBILITY", BspLumpIndex.Visibility),
            ("LUMP_NODES", BspLumpIndex.Nodes),
            ("LUMP_TEXINFO", BspLumpIndex.Texinfo),
            ("LUMP_FACES", BspLumpIndex.Faces),
            ("LUMP_LIGHTING", BspLumpIndex.Lighting),
            ("LUMP_LEAFS", BspLumpIndex.Leafs),
            ("LUMP_EDGES", BspLumpIndex.Edges),
            ("LUMP_SURFEDGES", BspLumpIndex.Surfedges),
            ("LUMP_MODELS", BspLumpIndex.Models),
            ("LUMP_WORLDLIGHTS", BspLumpIndex.WorldLights),
            ("LUMP_LEAFFACES", BspLumpIndex.LeafFaces),
            ("LUMP_DISPINFO", BspLumpIndex.DispInfo),
            ("LUMP_DISP_VERTS", BspLumpIndex.DispVerts),
            ("LUMP_GAME_LUMP", BspLumpIndex.GameLump),
            ("LUMP_PAKFILE", BspLumpIndex.PakFile),
            ("LUMP_CUBEMAPS", BspLumpIndex.Cubemaps),
            ("LUMP_TEXDATA_STRING_DATA", BspLumpIndex.TexdataStringData),
            ("LUMP_TEXDATA_STRING_TABLE", BspLumpIndex.TexdataStringTable),
            ("LUMP_OVERLAYS", BspLumpIndex.Overlays),
            ("LUMP_LEAF_AMBIENT_INDEX_HDR", BspLumpIndex.LeafAmbientIndexHdr),
            ("LUMP_LEAF_AMBIENT_INDEX", BspLumpIndex.LeafAmbientIndex),
            ("LUMP_LIGHTING_HDR", BspLumpIndex.LightingHdr),
            ("LUMP_FACES_HDR", BspLumpIndex.FacesHdr),
            ("LUMP_LEAF_AMBIENT_LIGHTING_HDR", BspLumpIndex.LeafAmbientLightingHdr),
            ("LUMP_LEAF_AMBIENT_LIGHTING", BspLumpIndex.LeafAmbientLighting),
        ];

        List<string> wrong = [];

        foreach ((string name, int ours) in claims)
        {
            if (!engine.TryGetValue(name, out int theirs))
            {
                wrong.Add($"{name} is not declared by the engine at all");
            }
            else if (theirs != ours)
            {
                wrong.Add($"{name}: we use {ours}, the engine declares {theirs}");
            }
        }

        wrong.ShouldBeEmpty(string.Join("; ", wrong));
    }

    [Test]
    public void BspLumps_TheDirectoryLength_Matches()
    {
        // HEADER_LUMPS bounds the directory this project walks. Reading fewer would make later
        // lumps unreachable; reading more walks past the header into the first lump's bytes.
        Declared()["HEADER_LUMPS"].ShouldBe(BspHeader.LumpCount);
    }

    [Test]
    public void BspLumps_TheHdrLumps_AreDistinctFromTheLdrOnes()
    {
        // **The pairing that is easy to get half right**, and the one worth stating separately: a
        // reader that used the LDR index for an HDR map finds stale or empty data and draws a
        // correctly lit map black, with nothing to report.
        BspLumpIndex.Lighting.ShouldNotBe(BspLumpIndex.LightingHdr);
        BspLumpIndex.Faces.ShouldNotBe(BspLumpIndex.FacesHdr);
        BspLumpIndex.LeafAmbientLighting.ShouldNotBe(BspLumpIndex.LeafAmbientLightingHdr);
        BspLumpIndex.LeafAmbientIndex.ShouldNotBe(BspLumpIndex.LeafAmbientIndexHdr);
    }

    /// <summary>Every lump constant the engine declares, read from its own header.</summary>
    private static Dictionary<string, int> Declared()
    {
        string? root = Environment.GetEnvironmentVariable("SOURCE_SDK");

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            root = @"F:\src\source-sdk-2013";
        }

        string header = Path.Combine(root, "src", "public", "bspfile.h");

        if (!File.Exists(header))
        {
            Assert.Ignore("source-sdk-2013 is not available; set SOURCE_SDK to run this.");
        }

        Dictionary<string, int> values = new(StringComparer.Ordinal);

        foreach (Match hit in Regex.Matches(
            File.ReadAllText(header),
            @"^\s*(?:#define\s+)?(LUMP_[A-Z0-9_]+|HEADER_LUMPS)\s*=?\s+(\d+)",
            RegexOptions.Multiline))
        {
            values.TryAdd(hit.Groups[1].Value, int.Parse(hit.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        // The instrument before its answer: an extraction that found nothing would make every
        // assertion above vacuously true.
        values.Count.ShouldBeGreaterThan(50, "no lump constants were extracted from bspfile.h");

        return values;
    }
}
