using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The string-table field widths, and a place where Valve's own comment is wrong.
/// </summary>
/// <remarks>
/// **`networkstringtabledefs.h:20` reads `#define MAX_TABLES 32  // Table id is 4 bits`, and those
/// two halves of one line contradict each other.** Thirty-two distinct identifiers need five bits;
/// four bits address sixteen. The constant and its comment cannot both be describing the wire.
///
/// **Arithmetic settles it without needing any new evidence**, which is the general lesson worth
/// keeping: a field's width can exclude a candidate outright, and checking that is cheaper than
/// gathering more data. Five bits is what this project reads, and it decodes every era in the corpus
/// — protocols 11 through 24 — which would be impossible if the field were four bits wide, since
/// every subsequent field in the message would be shifted by one.
///
/// The most likely history is that `MAX_TABLES` was raised and the comment was not, but that is an
/// inference and is flagged as one. What is measured is the width; what is inferred is why the
/// comment disagrees.
///
/// **Recorded because a stale comment in a header is more dangerous than no comment at all.** It
/// reads as documentation of the wire format, it is adjacent to a real constant, and it is wrong.
/// Anyone implementing from that line alone gets a decoder that fails on nothing in particular.
/// </remarks>
public sealed class StringTableWidthConformanceTests
{
    /// <summary>Where the table limit is declared.</summary>
    private const string Header = "src/public/networkstringtabledefs.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void TheTableIdIsWideEnoughForEveryTableTheSdkAllows()
    {
        IReadOnlyDictionary<string, int> tables = SourceSdk.Constants(Header);

        int limit = tables["MAX_TABLES"];

        // Derived rather than asserted as 5: the width has to address every allowed table, so it is
        // a property of MAX_TABLES rather than an independent number. If Valve raises the limit to
        // 64 this fails, which is the correct outcome — the wire field would have changed.
        int required = 0;
        while (1 << required < limit)
        {
            required++;
        }

        StringTableCodec.TableIdBits.ShouldBe(required);
    }

    [Test]
    public void TheCommentBesideThatConstantSaysFourBitsAndIsWrong()
    {
        // The control on the claim above. Without this the class reads as an ordinary width check,
        // and the finding — that a published comment contradicts the published constant — is not
        // recorded anywhere a reader would meet it.
        //
        // If Valve ever fixes the comment, this test fails and the remarks above should be rewritten
        // in the past tense rather than deleted: the fact that it WAS wrong is why this project's
        // width was worth checking, and a corrected upstream does not undo that.
        string header = SourceSdk.Text(Header).ShouldNotBeNull();

        header.ShouldContain("#define MAX_TABLES\t32  // Table id is 4 bits");
    }
}
