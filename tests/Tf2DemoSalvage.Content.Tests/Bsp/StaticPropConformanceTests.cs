using System;
using System.Collections.Generic;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The static prop lump, whose layout changes with its version — and the reason that is survivable.
/// </summary>
/// <remarks>
/// **This was written off as uncoverable and it is not.** The static prop lump lives in
/// <c>public/gamebspfile.h</c> rather than <c>bspfile.h</c>, and it exists in four declared shapes,
/// so <c>BspStructLayout</c> records it as a gap. But the versions are all declared, and the fact
/// this project's reader actually depends on is checkable: **the three fields it reads sit at the
/// same offsets in every one of them.**
///
/// That is what makes a version-agnostic reader correct rather than lucky. Origin, angles and prop
/// type are the first three members of V4, V5, V6 and V10 alike; the versions differ only by
/// appending — a forced fade scale, then DirectX bounds, then flags and lightmap resolution. A
/// reader that stops after the third field is right for all of them, and this says so with the
/// declarations rather than by having tried some maps.
///
/// **The stride still has to be right per version**, because it is the step between props. V4 is 56
/// bytes and V10 is 72, so reading a V10 lump at 56 would put the second prop 16 bytes early — into
/// the middle of the first one's lighting origin, which decodes as a position somewhere near the
/// map's centre.
/// </remarks>
public sealed class StaticPropConformanceTests
{
    /// <summary>Where the engine declares the game lumps.</summary>
    private const string GameBsp = "src/public/gamebspfile.h";

    /// <summary>The declared shapes, oldest first.</summary>
    private static readonly string[] Versions =
    [
        "StaticPropLumpV4_t",
        "StaticPropLumpV5_t",
        "StaticPropLumpV6_t",
        "StaticPropLump_t",
    ];

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void StaticProp_TheFieldsThisReaderUses_ShareOneOffsetAcrossVersions()
    {
        // **The claim the reader's design rests on.** If any version moved origin, angles or prop
        // type, reading without dispatching on the version would place props from that era
        // somewhere else entirely — and a prop at the wrong position is a prop, not an error.
        List<string> moved = [];

        foreach (string version in Versions)
        {
            CLayout layout = Layout(version);

            Check(version, layout, "m_Origin", 0, moved);
            Check(version, layout, "m_Angles", 12, moved);
            Check(version, layout, "m_PropType", 24, moved);
        }

        moved.ShouldBeEmpty(
            "these fields are not where every other version puts them, so a version-agnostic " +
            "reader is wrong: " + string.Join("; ", moved));
    }

    [Test]
    public void StaticProp_TheVersions_OnlyEverAppend()
    {
        // **Stated as the general property rather than the three instances above.** Each version is
        // a prefix of the next, so any reader that stops early is safe at any version — which is a
        // stronger and more useful statement than "the fields we happen to read did not move".
        //
        // V10 is the exception that proves it needs checking: it drops V6's one-byte m_Flags and
        // adds a four-byte m_Flags at the END. So the prefix holds only up to m_Solid, and a reader
        // depending on the byte after it would be wrong on exactly one version.
        int[] sizes = [.. Array.ConvertAll(Versions, version => Layout(version).Size)];

        sizes.ShouldBe(new[] { 56, 60, 64, 72 });

        for (int at = 1; at < sizes.Length; at++)
        {
            sizes[at].ShouldBeGreaterThan(
                sizes[at - 1],
                $"{Versions[at]} is not larger than {Versions[at - 1]}, so the versions are not " +
                "purely additive and this reader's assumption needs revisiting");
        }
    }

    [Test]
    public void StaticProp_TheOldestVersion_IsTheSmallestAcceptedRecord()
    {
        // BspStaticProps refuses a lump whose stride is below the smallest declared shape, which is
        // the bound that stops a corrupt count producing millions of props. 56 is not a number
        // someone chose; it is what V4 comes to.
        Layout("StaticPropLumpV4_t").Size.ShouldBe(56);
    }

    [Test]
    public void StaticProp_TheFlagsField_MovedAndWidenedAtVersionTen()
    {
        // **Recorded because it is the one thing a naive reader would get wrong**, and because
        // BspStaticProps does not read flags today. When something does, this is the trap: V4
        // through V6 carry m_Flags as one byte at offset 31, immediately after m_Solid; V10 removes
        // it from there and adds a four-byte m_Flags near the end. Reading offset 31 on a V10 lump
        // returns the low byte of m_Skin.
        Layout("StaticPropLumpV6_t").Offset("m_Flags").ShouldBe(31);

        CLayout latest = Layout("StaticPropLump_t");

        latest.Offset("m_Flags").ShouldBe(64);
        latest.Offset("m_Skin").ShouldBe(32);
    }

    /// <summary>Adds a complaint when a field is not where every version should put it.</summary>
    private static void Check(
        string version, CLayout layout, string field, int expected, List<string> moved)
    {
        int actual = layout.Offset(field);

        if (actual != expected)
        {
            moved.Add($"{version}.{field} is at {actual}, not {expected}");
        }
    }

    /// <summary>Reads one version's layout, failing rather than skipping when it cannot.</summary>
    private static CLayout Layout(string name)
    {
        string text = SourceSdk.Text(GameBsp)
            ?? throw new InvalidOperationException($"{GameBsp} is missing from the SDK checkout");

        CLayoutAttempt attempt = CStruct.Attempt(
            text,
            name,
            SourceSdk.Constants(GameBsp),
            new Dictionary<string, CTypeSize>(StringComparer.Ordinal)
            {
                ["Vector"] = new(12, 4),
                ["QAngle"] = new(12, 4),
            });

        return attempt.Layout
            ?? throw new InvalidOperationException(
                $"the layout of {name} could not be derived from {GameBsp}. " +
                $"Stopped at: {attempt.Refused}");
    }
}
