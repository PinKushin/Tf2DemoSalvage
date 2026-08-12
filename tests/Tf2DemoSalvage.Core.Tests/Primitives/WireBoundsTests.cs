using System.IO;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Tests the check that a count read off the wire could physically be delivered.
/// </summary>
/// <remarks>
/// The decoder repeatedly reads a count and then loops or allocates that many times —
/// <c>svc_ClassInfo</c> takes 16 bits, so a desynchronised stream hands it up to 65535 classes to
/// build out of whatever bits follow. Nothing checked that the message was big enough to contain
/// them, so the parser would do enormous work before failing, which is indistinguishable from a
/// hang while it is happening.
///
/// The bound is arithmetic rather than a tuned limit: N items need at least N x (smallest
/// possible item) bits, so a count needing more bits than remain is impossible, full stop. No
/// threshold to pick and nothing that a legitimate demo can grow into.
/// </remarks>
public sealed class WireBoundsTests
{
    [Test]
    public void ACountThatFitsIsAccepted()
    {
        Should.NotThrow(() => WireBounds.EnsureCountFits("test", count: 10, minBitsPerItem: 8, bitsRemaining: 80));
    }

    [Test]
    public void ACountNeedingMoreBitsThanRemain_IsRejected()
    {
        Should.Throw<InvalidDataException>(
            () => WireBounds.EnsureCountFits("test", count: 11, minBitsPerItem: 8, bitsRemaining: 80));
    }

    [Test]
    public void TheWireMaximumAgainstAnEmptyRemainder_IsRejected()
    {
        // The case that actually occurs: a 16-bit count field reading 65535 with almost nothing
        // left in the message, which is what a desynchronised stream produces.
        Should.Throw<InvalidDataException>(
            () => WireBounds.EnsureCountFits("svc_classinfo", count: 65535, minBitsPerItem: 1, bitsRemaining: 200));
    }

    [Test]
    public void ANegativeCountIsRejected()
    {
        // A 32-bit count read into an int arrives negative above int.MaxValue. Left unchecked it
        // skips the loop silently rather than failing, which hides the corruption.
        Should.Throw<InvalidDataException>(
            () => WireBounds.EnsureCountFits("test", count: -1, minBitsPerItem: 8, bitsRemaining: 800));
    }

    [Test]
    public void ZeroItemsAreLegal()
    {
        // Empty is a real message, not a malformed one.
        Should.NotThrow(() => WireBounds.EnsureCountFits("test", count: 0, minBitsPerItem: 8, bitsRemaining: 0));
    }

    [Test]
    public void TheProductIsComputedWithoutOverflowing()
    {
        // count x minBitsPerItem overflows int for large counts, and an overflowed product can
        // come out small and POSITIVE - which would let exactly the largest, most damaging counts
        // through the check that exists to stop them.
        Should.Throw<InvalidDataException>(
            () => WireBounds.EnsureCountFits("test", count: 200_000_000, minBitsPerItem: 32, bitsRemaining: 1000));
    }

    [Test]
    public void TheErrorNamesTheMessageAndTheNumbers()
    {
        InvalidDataException error = Should.Throw<InvalidDataException>(
            () => WireBounds.EnsureCountFits("svc_classinfo", count: 65535, minBitsPerItem: 1, bitsRemaining: 200));

        error.Message.ShouldContain("svc_classinfo");
        error.Message.ShouldContain("65535");
    }
}
