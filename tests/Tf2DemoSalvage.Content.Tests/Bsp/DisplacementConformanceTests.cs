using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The displacement structures, which were written off as underivable and are not.
/// </summary>
/// <remarks>
/// **<c>ddispinfo_t</c> was excluded from the layout test for a reason that turned out to be a
/// parser limitation rather than a property of the format.** It embeds <c>CDispNeighbor</c> and
/// <c>CDispCornerNeighbors</c>, which are declared with <c>class</c> instead of <c>struct</c> — and
/// C++ makes those identical for layout, differing only in default access. Teaching the reader that
/// one word closed the gap.
///
/// It is worth deriving because terrain is where a stride error hides best. A displacement is a grid
/// of heights over a face, so a misread <c>power</c> or <c>m_iDispVertStart</c> produces terrain —
/// just terrain belonging to a different face, at a different subdivision. Nothing about that looks
/// like a failure.
///
/// **The size is a chain of four declarations**: a sub-neighbour is 6 bytes with its padding, a
/// neighbour is two of those, a corner neighbour is four indices and a count, and the whole thing
/// ends with an array whose bound comes from the maximum displacement power. Each is derived from
/// its own declaration rather than stated, so the 176 this project uses is the sum of things that
/// were each checked.
/// </remarks>
public sealed class DisplacementConformanceTests
{
    /// <summary>Where the engine declares them.</summary>
    private const string BspFile = "src/public/bspfile.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Displacement_TheNeighbourStructures_MatchTheEngine()
    {
        // A sub-neighbour is an index and three single bytes: five bytes of content in a structure
        // aligned to two, so it occupies six. That trailing pad is the kind of thing hand-counting
        // drops, and it multiplies — a neighbour holds two of them and a displacement holds four
        // neighbours, so one missed byte moves the end of the record by eight.
        Layout("CDispSubNeighbor").Size.ShouldBe(6);
        Layout("CDispNeighbor").Size.ShouldBe(12);
        Layout("CDispCornerNeighbors").Size.ShouldBe(10);
    }

    [Test]
    public void Displacement_TheVertexStruct_MatchesWhatTheReaderReads()
    {
        // CDispVert is a class too, so this was never checked either. A vector, a distance and an
        // alpha: twenty bytes, which is the stride BspTerrain walks.
        CLayout vertex = Layout("CDispVert");

        // **Against the reader's own stride, not against 20.** Deriving the size from the SDK and
        // then comparing it to a number typed into the test proves the parser can add up; the
        // question that matters is whether BspTerrain steps by the same amount the engine does.
        vertex.Size.ShouldBe(BspStructLayout.DispVertStride);

        vertex.Offset("m_vVector").ShouldBe(0);
        vertex.Offset("m_flDist").ShouldBe(12);
        vertex.Offset("m_flAlpha").ShouldBe(16);
    }

    [Test]
    public void Displacement_TheRecordStruct_MatchesWhatTheReaderSteps()
    {
        CLayout info = Layout("ddispinfo_t", Composites());

        // Every one against the constant the reader actually uses. These four are what
        // BspTerrain slices with, so a disagreement here is a misread record rather than a
        // bookkeeping error in a test.
        info.Size.ShouldBe(BspStructLayout.DispInfoStride);
        info.Offset("startPosition").ShouldBe(BspStructLayout.DispStartPositionOffset);
        info.Offset("m_iDispVertStart").ShouldBe(BspStructLayout.DispVertexStartOffset);
        info.Offset("power").ShouldBe(BspStructLayout.DispPowerOffset);
    }

    [Test]
    public void Displacement_TheAllowedVertexArray_IsSizedByTheMaximumPower()
    {
        // **The bound is an expression, and the arithmetic is worth stating because it is not
        // obvious.** MAX_DISPVERTS is (2^4 + 1)^2 = 289 vertices at the highest allowed power;
        // PAD_NUMBER rounds that to 320 bits, and the array holds one bit per vertex packed into
        // 32-bit words, so ALLOWEDVERTS_SIZE is 10. Forty bytes, and the last forty of the record.
        //
        // Supplied to the parser rather than computed by it, so the number appears here with its
        // derivation instead of inside a general expression evaluator that would be wrong in
        // quieter ways.
        IReadOnlyDictionary<string, int> map = SourceSdk.Constants(BspFile);

        int perSide = (1 << map["MAX_MAP_DISP_POWER"]) + 1;

        (perSide * perSide).ShouldBe(289);
        AllowedVertexWords.ShouldBe(10);

        // And the array really is the tail: the record ends where it does because of this.
        CLayout info = Layout("ddispinfo_t", Composites());

        info.Offset("m_AllowedVerts")
            .ShouldBe(BspStructLayout.DispInfoStride - (AllowedVertexWords * 4));
    }

    /// <summary>Words in <c>m_AllowedVerts</c>: 289 vertices padded to 320 bits, over 32.</summary>
    private const int AllowedVertexWords = 10;

    /// <summary>The nested types, each derived from its own declaration.</summary>
    private static Dictionary<string, CTypeSize> Composites() =>
        new(StringComparer.Ordinal)
        {
            ["Vector"] = new(12, 4),
            ["CDispNeighbor"] = new(Layout("CDispNeighbor").Size, 2),
            ["CDispCornerNeighbors"] = new(Layout("CDispCornerNeighbors").Size, 2),
            ["uint32"] = new(4, 4),
        };

    /// <summary>Reads one structure, failing rather than skipping when it cannot.</summary>
    private static CLayout Layout(string name, Dictionary<string, CTypeSize>? extra = null)
    {
        string text = SourceSdk.Text(BspFile)
            ?? throw new InvalidOperationException($"{BspFile} is missing from the SDK checkout");

        Dictionary<string, CTypeSize> composites = extra ?? new(StringComparer.Ordinal)
        {
            ["Vector"] = new(12, 4),
            ["CDispSubNeighbor"] = new(6, 2),
        };

        composites.TryAdd("CDispSubNeighbor", new CTypeSize(6, 2));

        Dictionary<string, int> constants =
            new(SourceSdk.Constants(BspFile), StringComparer.Ordinal)
            {
                // The nested enum's value, with its arithmetic stated in the test above.
                ["ALLOWEDVERTS_SIZE"] = AllowedVertexWords,
                ["MAX_DISP_CORNER_NEIGHBORS"] = 4,
            };

        CLayoutAttempt attempt = CStruct.Attempt(
            text,
            name,
            constants,
            composites,
            pointerBytes: null,
            defined: new HashSet<string>(StringComparer.Ordinal) { "VALVE_LITTLE_ENDIAN" });

        return attempt.Layout
            ?? throw new InvalidOperationException(
                $"the layout of {name} could not be derived. Stopped at: {attempt.Refused}");
    }
}
