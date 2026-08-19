using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The send-prop flag word, which carries two traps Valve documents only in comments.
/// </summary>
/// <remarks>
/// **Both traps are in the same seventeen bits, and both fail as plausible data rather than as an
/// error.** A decoder that gets either one wrong produces numbers, not exceptions, and the numbers
/// are wrong in a way that only shows up in whatever the property happened to mean.
///
/// The first is a deliberate collision: one bit has two meanings and which one applies depends on
/// the property's type, not on the flag word. The second is an off-by-one in how many of the bits
/// are actually transmitted — the constant that looks like the flag count is not the constant that
/// says how many are sent.
///
/// **This project has both right already**, and these tests exist to keep it that way against a
/// source that states the rule nowhere except in an end-of-line comment. Written as conformance
/// rather than as a regression check: the assertion derives our constant from Valve's declaration,
/// so it fails if either side moves.
/// </remarks>
public sealed class SendPropFlagConformanceTests
{
    /// <summary>Where the flag word is declared.</summary>
    private const string Header = "src/public/dt_common.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void SendPropFlags_VarintAndNormal_ShareOneBitWithDifferentMeanings()
    {
        // dt_common.h:78 — `#define SPROP_VARINT SPROP_NORMAL`, with Valve's own reason attached:
        // "reuse existing flag so we don't break demo".
        //
        // **That comment is the whole finding.** Adding a new flag bit would have changed the
        // meaning of the flag word in every previously recorded demo, so instead bit 5 was given a
        // second meaning, disambiguated by the property's TYPE: on a vector it means the value is a
        // normal, on an integer it means the value is varint-encoded.
        //
        // A decoder that treats the flag word as a set of independent booleans reads a varint
        // integer as a fixed-width field, which does not throw — it consumes the wrong number of
        // BITS, so every property after it in the same delta is misaligned. That is the failure mode
        // that looks like a corrupt demo.
        IReadOnlyDictionary<string, int> flags = SourceSdk.Constants(Header);

        flags["SPROP_VARINT"].ShouldBe(flags["SPROP_NORMAL"]);

        // Chained to our own constant rather than to a typed number, so the test fails if either
        // this project or the SDK moves. Transcribing 32 here would pass against a wrong reading.
        SendPropDecoder.VarIntFlag.ShouldBe(flags["SPROP_VARINT"]);
    }

    [Test]
    public void SendPropFlags_OneOfTheSeventeenBits_IsNeverSent()
    {
        // Two constants that look interchangeable and are not: SPROP_NUMFLAGBITS is 17 and
        // SPROP_NUMFLAGBITS_NETWORKED is 16. The seventeenth bit,
        // SPROP_ENCODED_AGAINST_TICKCOUNT (1<<16), is set locally by the engine and never
        // transmitted.
        //
        // **Reading 17 bits therefore steals one bit from whatever follows**, and — like the
        // collision above — misaligns the rest of the table rather than failing. Picking the
        // wrong-looking constant of two adjacent ones is exactly the mistake that gets made by
        // reading the header quickly.
        IReadOnlyDictionary<string, int> flags = SourceSdk.Constants(Header);

        flags["SPROP_NUMFLAGBITS_NETWORKED"].ShouldBe(16);
        flags["SPROP_NUMFLAGBITS"].ShouldBe(17);

        // The arithmetic that makes the pair make sense, rather than two remembered numbers: the
        // unsent flag is exactly the bit above the networked width.
        flags["SPROP_ENCODED_AGAINST_TICKCOUNT"]
            .ShouldBe(1 << flags["SPROP_NUMFLAGBITS_NETWORKED"]);

        SendTableParser.FlagBits.ShouldBe(flags["SPROP_NUMFLAGBITS_NETWORKED"]);
    }
}
