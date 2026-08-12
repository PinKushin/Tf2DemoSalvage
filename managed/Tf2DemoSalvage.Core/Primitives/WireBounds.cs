using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>
/// Checks that a count read off the wire could physically be delivered by what is left.
/// </summary>
/// <remarks>
/// **The decoder reads counts and then trusts them.** <c>svc_ClassInfo</c> takes its count from
/// 16 bits, so a desynchronised stream can hand it 65535 classes to build out of whatever bits
/// follow; the string table, game event and SendTable readers have the same shape. Nothing
/// checked that the message was large enough to contain what it claimed, so the parser would do
/// an enormous amount of work on garbage before failing — and while that is happening it is
/// indistinguishable from a hang.
///
/// This is the mechanism behind the corpus mutation run's 1142 timeouts against 0 survivors. A
/// mutant only has to knock the bit position out of alignment; from there the parser is reading
/// counts out of noise, and a 16-bit field reads large far more often than it reads small. The
/// arithmetic bears it out — the run allowed ~55 s per mutant against a ~25 s suite, so those
/// runs were not sitting marginally over a threshold, they were doing real work on nonsense.
///
/// **It is the same defect a malformed demo triggers**, which is what makes it worth fixing on
/// its own terms. Reading files other parsers reject is this project's entire purpose, so a
/// corrupt count is an expected input, not a hypothetical one.
///
/// The bound is arithmetic, not a tuned limit: N items need at least N × (smallest possible item)
/// bits, so a count needing more bits than remain is impossible. There is no threshold to pick
/// and nothing a legitimate demo can grow into — which matters, because a guess here would either
/// reject real demos or be too loose to help.
/// </remarks>
internal static class WireBounds
{
    /// <summary>Throws unless <paramref name="count"/> items could fit in what remains.</summary>
    /// <param name="what">Message or table being read, named in the error.</param>
    /// <param name="count">Count as read from the stream.</param>
    /// <param name="minBitsPerItem">
    /// A proven lower bound on one item's encoded size. Must be a true minimum — too high rejects
    /// valid demos, so where the real floor is unclear, 1 is still enough to catch the counts
    /// that cause the damage.
    /// </param>
    /// <param name="bitsRemaining">Bits left in the message or stream.</param>
    /// <exception cref="InvalidDataException">The count cannot be delivered by what remains.</exception>
    public static void EnsureCountFits(string what, int count, int minBitsPerItem, int bitsRemaining)
    {
        // Negative arrives when a 32-bit count is read into an int above int.MaxValue. Unchecked
        // it silently skips the loop rather than failing, which hides the corruption instead of
        // reporting it.
        if (count < 0)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"{what} declares {count} items, which is not a count. The stream is corrupt " +
                $"or misaligned."));
        }

        // `long`, because count × minBitsPerItem overflows int for large counts — and an
        // overflowed product can land small and POSITIVE, which would wave through exactly the
        // largest counts this check exists to stop.
        long needed = (long)count * minBitsPerItem;

        if (needed > bitsRemaining)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"{what} declares {count} items needing at least {needed} bits, but only " +
                $"{bitsRemaining} remain. The stream is corrupt or misaligned."));
        }
    }
}
