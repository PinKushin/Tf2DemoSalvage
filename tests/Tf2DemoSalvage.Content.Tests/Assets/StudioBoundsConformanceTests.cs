using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What a studio model's render bounds ARE, which is not its vertices.
/// </summary>
/// <remarks>
/// **Written because the shortcut was nearly taken.** Valve buckets opaque models by size and draws
/// the biggest first; the plan here was to take each model's vertex extent once at pack time and
/// call that the size. The owner stopped it — *"what does valve do, do not simplify valve unless i
/// give you permission and you explain why"* — and the substitution was wrong three ways over.
///
/// **`C_BaseAnimating::GetRenderBounds` (`c_baseanimating.cpp:4533`) reads AUTHORED header data:**
///
/// <code>
/// if (!VectorCompare( vec3_origin, view_bbmin() ) || !VectorCompare( vec3_origin, view_bbmax() ))
///     theMins = view_bbmin(); theMaxs = view_bbmax();   // clipping bounding box
/// else
///     theMins = hull_min();  theMaxs = hull_max();      // movement bounding box
///
/// mstudioseqdesc_t &amp;seqdesc = pStudioHdr-&gt;pSeqdesc( GetSequence() );
/// VectorMin( seqdesc.bbmin, theMins, theMins );
/// VectorMax( seqdesc.bbmax, theMaxs, theMaxs );
/// </code>
///
/// So: the clipping box when the modeller authored one, the movement hull otherwise, **unioned with
/// the box of the sequence currently playing**. A running player therefore has different bounds
/// from a crouched one, which a single pack-time number cannot express.
///
/// **And none of it is the vertices.** The three sources are fields in the file; a vertex extent
/// is a fourth number that happens to be nearby and is not what the engine asks.
/// </remarks>
public sealed class StudioBoundsConformanceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>That the header's bounds sit where this project reads them.</summary>
    /// <remarks>
    /// **Cross-checked against a field this project already reads.** `numbones` is at 156 and has
    /// been decoded correctly for months, so a layout that puts `hull_min` at 104 and reaches 156
    /// for `numbones` is consistent with working code rather than with arithmetic alone — see
    /// `docs/memory/struct-padding-is-on-disk.md` for why the sum of the fields is not the answer.
    /// </remarks>
    [Test]
    public void StudioLayout_ForTheHeadersBounds_MatchesTheSdksFieldOrder()
    {
        string header = Sdk();

        // id, version, checksum, name[64], length, eyeposition, illumposition, then the two boxes.
        Order(header, "hull_min").ShouldBeLessThan(Order(header, "hull_max"));
        Order(header, "hull_max").ShouldBeLessThan(Order(header, "view_bbmin"));
        Order(header, "view_bbmin").ShouldBeLessThan(Order(header, "view_bbmax"));
        Order(header, "view_bbmax").ShouldBeLessThan(Order(header, "numbones"));

        StudioLayout.HeaderHullMinOffset.ShouldBe(104);
        StudioLayout.HeaderHullMaxOffset.ShouldBe(116);
        StudioLayout.HeaderViewBoundsMinOffset.ShouldBe(128);
        StudioLayout.HeaderViewBoundsMaxOffset.ShouldBe(140);

        // The anchor: a field already decoded correctly, four Vectors after hull_min.
        StudioLayout.HeaderBoneCountOffset.ShouldBe(
            StudioLayout.HeaderHullMinOffset + (4 * 12) + 4,
            "flags sits between view_bbmax and numbones");
    }

    /// <summary>That a sequence's own box sits after its event index.</summary>
    [Test]
    public void StudioLayout_ForASequencesBounds_MatchesTheSdksFieldOrder()
    {
        string header = Body("mstudioseqdesc_t");

        Order(header, "eventindex").ShouldBeLessThan(Order(header, "bbmin"));
        Order(header, "bbmin").ShouldBeLessThan(Order(header, "bbmax"));
        Order(header, "bbmax").ShouldBeLessThan(Order(header, "numblends"));

        // baseptr, szlabelindex, szactivitynameindex, flags, activity, actweight, numevents,
        // eventindex — eight ints — then the two Vectors.
        StudioLayout.SequenceBoundsMinOffset.ShouldBe(32);
        StudioLayout.SequenceBoundsMaxOffset.ShouldBe(44);
    }

    /// <summary>That the clipping box wins when it is authored, and the hull otherwise.</summary>
    /// <remarks>
    /// **The test is `!= origin` on EITHER corner, not on both.** A model whose `view_bbmin` is
    /// zero and whose `view_bbmax` is not still uses the clipping box — Valve's condition is an OR
    /// of two inequalities. Reading it as an AND would silently fall through to the hull for any
    /// model whose clipping box happens to start at the origin, which is a common authoring.
    /// </remarks>
    [Test]
    public void Sdk_TheBoundsSource_IsTheClippingBoxWhenEitherCornerIsSet()
    {
        string source = Flat(
            Skip.Unless(
                SourceSdk.Text("src/game/client/c_baseanimating.cpp"), SourceSdk.Missing));

        source.ShouldContain(
            "if (!VectorCompare( vec3_origin, pStudioHdr->view_bbmin() ) || " +
            "!VectorCompare( vec3_origin, pStudioHdr->view_bbmax() ))");
    }

    /// <summary>That the sequence's box is UNIONED in rather than replacing the header's.</summary>
    [Test]
    public void Sdk_TheSequencesBounds_AreUnionedWithTheHeaders()
    {
        string source = Flat(
            Skip.Unless(
                SourceSdk.Text("src/game/client/c_baseanimating.cpp"), SourceSdk.Missing));

        source.ShouldContain("VectorMin( seqdesc.bbmin, theMins, theMins );");
        source.ShouldContain("VectorMax( seqdesc.bbmax, theMaxs, theMaxs );");
    }

    private static string Sdk() => Body("studiohdr_t");

    /// <summary>One struct's body, so field order is measured inside it and not across the file.</summary>
    /// <remarks>
    /// **The first version of this searched the whole header and both tests failed on it.**
    /// `bbmin`, `numbones` and `eventindex` are declared in several structs in `studio.h`, so
    /// "first occurrence" found whichever struct came first and reported an order that is true of
    /// the file and meaningless about the layout. The failure was luck: the names happened to
    /// appear in an order that broke the assertion rather than one that passed it by accident.
    ///
    /// Anchored on the declaration followed by a brace, because `struct studiohdr_t;` is
    /// forward-declared earlier and would otherwise match.
    /// </remarks>
    private static string Body(string name)
    {
        string header = Skip.Unless(SourceSdk.Text("src/public/studio.h"), SourceSdk.Missing);

        Match found = Regex.Match(
            header,
            $@"struct {Regex.Escape(name)}\s*\r?\n\{{(.*?)\r?\n\}};",
            RegexOptions.Singleline,
            Limit);

        found.Success.ShouldBeTrue($"studio.h defines {name}");

        return found.Groups[1].Value;
    }

    private static int Order(string body, string field)
    {
        Match found = Regex.Match(
            body, $@"\b{Regex.Escape(field)}\b", RegexOptions.None, Limit);

        found.Success.ShouldBeTrue($"the struct declares {field}");

        return found.Index;
    }

    private static string Flat(string source) =>
        Regex.Replace(source, @"[ \t]+", " ", RegexOptions.None, Limit);
}
