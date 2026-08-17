using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The entity lump: plain text key/values, one block per entity.
/// </summary>
/// <remarks>
/// **Lump 0 is the one part of a BSP that is not a struct array.** It is the text Hammer wrote,
/// carried through compilation almost unchanged:
///
/// <code>
/// {
/// "origin" "-4374 -3786 229.5"
/// "scale" "16"
/// "classname" "sky_camera"
/// }
/// </code>
///
/// It is read here for one reason — <c>sky_camera</c> marks the 3D skybox room, which is ordinary
/// world geometry sitting far from the map and would otherwise dominate an overhead view's bounds.
/// The entity is an exact marker where a spatial rule is only a guess.
///
/// **This is untrusted text from a downloaded file (D32).** The parser is a hand-written state
/// machine with no regular expressions: a hostile entity lump is a natural place to hide a
/// catastrophic-backtracking input, and nothing here needs the power.
/// </remarks>
public sealed class BspEntitiesTests
{
    [Test]
    public void Parse_TwoEntities_ReadsBoth()
    {
        IReadOnlyList<BspEntity> entities = Parse(
            "{\n\"classname\" \"worldspawn\"\n\"skyname\" \"sky_upward\"\n}\n" +
            "{\n\"classname\" \"sky_camera\"\n\"origin\" \"1 2 3\"\n}\n");

        entities.Count.ShouldBe(2);
        entities[0]["classname"].ShouldBe("worldspawn");
        entities[1]["origin"].ShouldBe("1 2 3");
    }

    [Test]
    public void Parse_ValueContainingBraces_DoesNotEndTheBlock()
    {
        // Real maps carry these: an io output value, or a texture path with punctuation. A parser
        // that scanned for the next '}' without tracking quotes would cut the entity in half.
        IReadOnlyList<BspEntity> entities = Parse(
            "{\n\"classname\" \"logic_relay\"\n\"OnTrigger\" \"door,Open,{a},0,-1\"\n}\n");

        entities.Count.ShouldBe(1);
        entities[0]["OnTrigger"].ShouldBe("door,Open,{a},0,-1");
    }

    [Test]
    public void Parse_TrailingNullTerminator_IsIgnored()
    {
        // The lump is a C string and ends with a NUL, which is not the start of an entity.
        Parse("{\n\"classname\" \"worldspawn\"\n}\n\0").Count.ShouldBe(1);
    }

    [Test]
    public void Parse_UnterminatedBlock_IsDropped()
    {
        // A truncated map. The complete entity before the cut is still usable, which is the same
        // rule the demo command reader follows.
        IReadOnlyList<BspEntity> entities = Parse(
            "{\n\"classname\" \"worldspawn\"\n}\n{\n\"classname\" \"sky_ca");

        entities.Count.ShouldBe(1);
        entities[0]["classname"].ShouldBe("worldspawn");
    }

    [Test]
    public void Parse_RepeatedKey_KeepsTheFirst()
    {
        // **Source has BOTH behaviours, and this is the lookup one.** Audited 2026-08-16 because the
        // original comment here — "Source's own key/value store answers with the first" — asserted a
        // policy with no citation, which is the shape that let a roster test certify a real bug.
        //
        // Looking a key up answers with the first: MapEntity_ExtractValue in
        // game/shared/mapentities_shared.cpp scans forward and returns on the first name match. Same
        // for KeyValues::FindKey, which walks the peer list and returns the first — and loading
        // APPENDS (KeyValues.cpp:1902), so first-in-list is first-in-file rather than the reverse.
        //
        // **Spawning an entity does the opposite.** MapEntity_ParseEntity iterates every key with
        // GetFirstKey/GetNextKey and hands each to KeyValue(), so for a field-backed key the LAST
        // one written wins.
        //
        // This parser produces a dictionary for lookup-style consumers, so first-wins is the correct
        // match. Anyone using it to reproduce spawn behaviour needs the other rule, and would not
        // learn that from a dictionary that had already discarded the duplicate.
        Parse("{\n\"origin\" \"1 2 3\"\n\"origin\" \"9 9 9\"\n}")[0]["origin"].ShouldBe("1 2 3");
    }

    [Test]
    public void Parse_EmptyLump_ReturnsNothing()
    {
        Parse(string.Empty).ShouldBeEmpty();
    }

    [Test]
    public void SkyCameraOrigin_Present_IsRead()
    {
        // The value this whole file exists for, in the exact form a shipped map writes it -
        // including a fractional Z, which an integer parse would reject.
        (float X, float Y, float Z)? origin = BspEntities.SkyCameraOrigin(Parse(
            "{\n\"classname\" \"worldspawn\"\n}\n" +
            "{\n\"origin\" \"-4374 -3786 229.5\"\n\"scale\" \"16\"\n\"classname\" \"sky_camera\"\n}"));

        origin.ShouldNotBeNull();
        origin.Value.X.ShouldBe(-4374f);
        origin.Value.Y.ShouldBe(-3786f);
        origin.Value.Z.ShouldBe(229.5f);
    }

    [Test]
    public void SkyCameraOrigin_Absent_IsNull()
    {
        // Not every map has a 3D skybox. A missing marker means nothing to exclude, not an error.
        BspEntities.SkyCameraOrigin(Parse("{\n\"classname\" \"worldspawn\"\n}")).ShouldBeNull();
    }

    [Test]
    public void SkyCameraOrigin_MalformedOrigin_IsNull()
    {
        // Untrusted text. Two components is not a position, and inventing the third would place
        // the exclusion somewhere arbitrary rather than reporting that there is none.
        BspEntities.SkyCameraOrigin(Parse(
            "{\n\"classname\" \"sky_camera\"\n\"origin\" \"1 2\"\n}")).ShouldBeNull();
    }

    [Test]
    public void SkyCameraOrigin_NonNumericOrigin_IsNull()
    {
        BspEntities.SkyCameraOrigin(Parse(
            "{\n\"classname\" \"sky_camera\"\n\"origin\" \"a b c\"\n}")).ShouldBeNull();
    }

    private static IReadOnlyList<BspEntity> Parse(string text) =>
        BspEntities.Parse(Encoding.UTF8.GetBytes(text));
}
