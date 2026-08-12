using System.IO;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Tests the guard that stops a decode loop that has stopped consuming.
/// </summary>
/// <remarks>
/// Every loop in this parser that walks a buffer exits on a position reaching a length, so all of
/// them share one failure: if an iteration consumes nothing, the condition never changes and the
/// loop runs forever. It is not a hypothetical shape — mutation testing produces it on demand by
/// turning a <c>read++</c> into a <c>read--</c>, and a truncated or hostile demo can reach the
/// same state without anyone's help.
///
/// The cost of not having this is measured. A corpus mutation run took **18 hours** and reported
/// 1142 timeouts; a hang costs the full per-mutant timeout every time it happens, and a run that
/// has to wait out its own infinite loops cannot be scheduled.
/// </remarks>
public sealed class DecodeProgressTests
{
    [Test]
    public void AnAdvancingPositionIsAccepted()
    {
        DecodeProgress progress = new("test", 0);

        Should.NotThrow(() =>
        {
            progress.Advanced(1);
            progress.Advanced(2);
            progress.Advanced(99);
        });
    }

    [Test]
    public void APositionThatDidNotMove_IsRejected()
    {
        DecodeProgress progress = new("test", 5);

        Should.Throw<InvalidDataException>(() => progress.Advanced(5));
    }

    [Test]
    public void APositionThatWentBackwards_IsRejected()
    {
        // Backwards is the mutation-testing case specifically, where an increment operator is
        // flipped to a decrement. It has to fail as corrupt input rather than by walking off the
        // front of the buffer.
        DecodeProgress progress = new("test", 5);

        Should.Throw<InvalidDataException>(() => progress.Advanced(4));
    }

    [Test]
    public void TheErrorNamesWhatStalled()
    {
        // A decoder that stalls without saying which loop it was leaves the reader to guess
        // between every buffer-walking loop in the parser.
        DecodeProgress progress = new("svc_Sounds", 12);

        InvalidDataException error =
            Should.Throw<InvalidDataException>(() => progress.Advanced(12));

        error.Message.ShouldContain("svc_Sounds");
        error.Message.ShouldContain("12");
    }

    [Test]
    public void EachAcceptedPositionBecomesTheNewFloor()
    {
        // The check is against the previous iteration, not against the start. Comparing to the
        // start would accept a loop that advanced once and then stalled forever after.
        DecodeProgress progress = new("test", 0);
        progress.Advanced(10);

        Should.Throw<InvalidDataException>(() => progress.Advanced(10));
    }
}
