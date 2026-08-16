using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Every surface flag this project names, checked against <c>bspflags.h</c>.
/// </summary>
/// <remarks>
/// **A wrong bit here hides or shows the wrong half of a map.** <c>SURF_SKY</c> and
/// <c>SURF_NODRAW</c> are one bit apart in meaning and five apart in value; reading the wrong one
/// draws the skybox brushes as solid walls, or drops every wall and leaves the sky. Both are
/// pictures rather than errors.
///
/// **It also states what is NOT modelled.** Four flags exist that this project ignores, and naming
/// them is the difference between a decision and an oversight — the census lesson, applied to a
/// smaller set. A coverage claim that only counts what it handles cannot tell those apart.
/// </remarks>
public sealed class SurfaceFlagTests
{
    /// <summary>Where the engine declares the surface flags.</summary>
    private const string Flags = "src/public/bspflags.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void EveryFlagWeName_HasTheEnginesValue()
    {
        IReadOnlyDictionary<string, int> engine = Declared();

        (string Name, SurfaceProperties Ours)[] claims =
        [
            ("SURF_LIGHT", SurfaceProperties.Light),
            ("SURF_SKY2D", SurfaceProperties.Sky2D),
            ("SURF_SKY", SurfaceProperties.Sky),
            ("SURF_WARP", SurfaceProperties.Warp),
            ("SURF_TRANS", SurfaceProperties.Translucent),
            ("SURF_NOPORTAL", SurfaceProperties.NoPortal),
            ("SURF_TRIGGER", SurfaceProperties.Trigger),
            ("SURF_NODRAW", SurfaceProperties.NoDraw),
            ("SURF_HINT", SurfaceProperties.Hint),
            ("SURF_SKIP", SurfaceProperties.Skip),
            ("SURF_NOLIGHT", SurfaceProperties.NoLight),
            ("SURF_BUMPLIGHT", SurfaceProperties.BumpLight),
        ];

        List<string> wrong = [];

        foreach ((string name, SurfaceProperties ours) in claims)
        {
            if (!engine.TryGetValue(name, out int theirs))
            {
                wrong.Add($"{name} is not declared by the engine at all");
            }
            else if (theirs != (int)ours)
            {
                wrong.Add($"{name}: we use 0x{(int)ours:X4}, the engine declares 0x{theirs:X4}");
            }
        }

        wrong.ShouldBeEmpty(string.Join("; ", wrong));
    }

    [Test]
    public void TheFlagsWeDoNotModelAreNamed()
    {
        // **A gap stated is a decision; a gap unstated is an oversight.** These four are real flags
        // this project reads past: none of them changes whether a surface is drawn, which is why
        // ignoring them is defensible — and that sentence is the point of the test.
        //
        //   SURF_NOSHADOWS  no shadow casting, and this draws no shadows at all
        //   SURF_NODECALS   no decals, and overlays are placed by the map rather than chosen here
        //   SURF_NOCHOP     a vrad subdivision hint, meaningless after compilation
        //   SURF_HITBOX     collision geometry, never drawn
        //
        // If any of them disappears from bspflags.h, this fails and the comment above needs redoing.
        IReadOnlyDictionary<string, int> engine = Declared();

        string[] ignored = ["SURF_NOSHADOWS", "SURF_NODECALS", "SURF_NOCHOP", "SURF_HITBOX"];

        foreach (string name in ignored)
        {
            engine.ShouldContainKey(name);
        }

        // And none of them collides with one this project does act on, which is the failure that
        // would actually matter: a bit tested for two meanings.
        int modelled = Enum.GetValues<SurfaceProperties>().Aggregate(0, (all, flag) => all | (int)flag);

        foreach (string name in ignored)
        {
            (modelled & engine[name]).ShouldBe(0, $"{name} overlaps a flag we act on");
        }
    }

    [Test]
    public void TheSolidContentsBitIsTheEngines()
    {
        // **A different axis in the same header, and the only one of it this project reads.**
        // CONTENTS_* describe what fills a leaf; SURF_* describe a face. BspLeafTree tests this one
        // bit to decide whether a point is inside the world, which is what stops the sky trace
        // walking out through a wall. Reading the wrong bit would make solid leaves look open — and
        // an object lit as though it were outdoors is a lighting oddity, not an error.
        Declared()["CONTENTS_SOLID"].ShouldBe(BspLeafTree.ContentsSolid);
    }

    [Test]
    public void TheSelfShadowBumpTextureFlagIsTheEngines()
    {
        // TEXTUREFLAGS_SSBUMP, from vtf/vtf.h rather than bspflags.h. It is checked here because it
        // is the last constant in this project citing an engine name without a test: the texture's
        // own flag overrides whatever $detailblendmode asked for, so reading it wrongly silently
        // selects a different blend for every self-shadowing bump map on a map.
        SourceSdk.Constants("src/public/vtf/vtf.h")["TEXTUREFLAGS_SSBUMP"]
            .ShouldBe((int)VtfTexture.SelfShadowBumpFlag);
    }

    [Test]
    public void EveryFlagIsASingleDistinctBit()
    {
        // The control. An enum of flags where two entries share a bit, or one holds two, tests as
        // fine against any individual value and misbehaves only in combination.
        int seen = 0;

        foreach (SurfaceProperties flag in Enum.GetValues<SurfaceProperties>())
        {
            if (flag == SurfaceProperties.None)
            {
                continue;
            }

            int value = (int)flag;

            System.Numerics.BitOperations.PopCount((uint)value)
                .ShouldBe(1, $"{flag} is not a single bit");

            (seen & value).ShouldBe(0, $"{flag} reuses a bit already taken");

            seen |= value;
        }
    }

    /// <summary>Every surface flag the engine declares.</summary>
    private static IReadOnlyDictionary<string, int> Declared()
    {
        IReadOnlyDictionary<string, int> values = SourceSdk.Constants(Flags);

        // The instrument before its answer: an extraction that found nothing would make the
        // assertions above pass by vacuum.
        values.Keys.Count(name => name.StartsWith("SURF_", StringComparison.Ordinal))
            .ShouldBeGreaterThan(12, "no surface flags were extracted from bspflags.h");

        return values;
    }
}
