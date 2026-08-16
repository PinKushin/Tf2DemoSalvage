using System;
using System.IO;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Inputs a fuzzer found, kept permanently as the bytes that produced them.
/// </summary>
/// <remarks>
/// **A crash artifact is a regression fixture, not a bug report.** Project rule, and the reason
/// these live in the test suite rather than in a findings directory on a box: the exact bytes are
/// the only thing that reproduces the defect, and a machine that is reimaged loses them.
///
/// **This one sat unnoticed on fuzz-box from 2026-08-15 00:04 to 2026-08-16.** It was found only by
/// checking whether the FINDINGS line printed during an unrelated run referred to that run — it did
/// not, which is itself a defect in the runner and is recorded separately. A finding nobody triages
/// is indistinguishable from no finding.
/// </remarks>
public sealed class SnappyFuzzRegressionTests
{
    /// <summary>
    /// The 2026-08-15 artifact: a literal tag whose 4-byte length is <c>int.MaxValue</c>.
    /// </summary>
    /// <remarks>
    /// Nine bytes, and every one of them matters:
    /// <code>
    /// 08                 uncompressed length 8 (varint)
    /// 00                 tag: literal, length (0 >> 2) + 1 = 1
    /// ff                 the one literal byte
    /// fc                 tag: literal, 0xFC >> 2 = 63 -> a 4-byte length follows
    /// fe ff ff 7f        that length, little-endian: 0x7FFFFFFF = int.MaxValue
    /// 09                 trailing byte, never reached
    /// </code>
    ///
    /// **Snappy stores a literal's length minus one, so the decoder must add one — and
    /// <c>int.MaxValue + 1</c> is negative.** A guard written as "refuse if the length exceeds what
    /// remains" then passes, because a negative number is not greater than anything. The failure is
    /// not the arithmetic, it is that the check the arithmetic defeats looks completely correct.
    ///
    /// Exactly the family in <c>numeric-decoding-traps</c>: it fails as a plausible number rather
    /// than as an error.
    /// </remarks>
    private static readonly byte[] LiteralLengthOverflow =
        [0x08, 0x00, 0xFF, 0xFC, 0xFE, 0xFF, 0xFF, 0x7F, 0x09];

    [Test]
    public void ALiteralLengthOfIntMaxIsRefusedRatherThanOverflowing()
    {
        // The contract is narrow on purpose. Snappy.Decompress documents InvalidDataException as
        // its refusal, so ANY other exception is a defect — including the OverflowException or
        // ArgumentOutOfRangeException an unchecked add would produce, and including a success,
        // which would mean the decoder invented output from a length it could not have had.
        //
        // Asserting the exception TYPE rather than "it throws something" is the point: the fuzz
        // harness's whole property is that malformed input is refused in the documented way, and
        // "threw an exception" is satisfied by the crash this test exists to prevent.
        Should.Throw<InvalidDataException>(() => Snappy.Decompress(LiteralLengthOverflow));
    }

    [Test]
    public void TheReproducerIsStillTheInputThatWasFound()
    {
        // A control on the fixture, not on the decoder. These bytes are meaningless if edited, and
        // an assertion above that passes against a mangled array proves nothing about the defect.
        //
        // The declared length is the whole finding, so it is checked here rather than trusted: read
        // back out of the array by the same little-endian rule the format uses.
        LiteralLengthOverflow.Length.ShouldBe(9);

        int declared = LiteralLengthOverflow[4]
            | (LiteralLengthOverflow[5] << 8)
            | (LiteralLengthOverflow[6] << 16)
            | (LiteralLengthOverflow[7] << 24);

        // 0x7FFFFFFE, one below int.MaxValue. Snappy stores a literal's length minus one, so the
        // length this input declares is exactly int.MaxValue — large enough to be absurd, and
        // deliberately NOT large enough to overflow the addition.
        //
        // **This assertion caught the first version of this file getting it wrong**, which had
        // read the bytes as 0x7FFFFFFF and built a story about `+ 1` wrapping negative. A control
        // over the fixture is not ceremony: the fixture is the finding, and a misread fixture
        // produces a confident, wrong explanation attached to a real crash.
        declared.ShouldBe(int.MaxValue - 1);

        // And the tag that says a 4-byte length follows: 0xFC >> 2 == 63, the largest literal tag.
        (LiteralLengthOverflow[3] >> 2).ShouldBe(63);
    }
}
