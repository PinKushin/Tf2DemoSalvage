namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>
/// Field widths the wire derives from a count rather than transmitting.
/// </summary>
/// <remarks>
/// **One implementation per derived width, in the layer both callers can see.** These are the
/// widths that fail silently: nothing on the wire states them, so a decoder using the wrong one
/// stays aligned for a while and then reads the next field from the wrong bit. There is no error
/// to catch, only values that get progressively less plausible.
///
/// Kept here because the alternative was demonstrated: <c>svc_ClassInfo</c> and the entity decoder
/// each grew their own class-id width, the entity decoder's was corrected against a real demo and
/// the message's was not, and the two then disagreed for every count that is an exact power of
/// two — which includes 256, protocol 15's <c>max_classes</c>.
/// </remarks>
public static class WireWidths
{
    /// <summary>Bits a class id occupies, given how many classes the server declared.</summary>
    /// <param name="classCount">Number of networked classes.</param>
    /// <returns>The width used for class ids on the wire.</returns>
    /// <remarks>
    /// The engine's <c>GetServerClassBits</c>: <c>floor(log2(count)) + 1</c>, which is the count's
    /// width in binary. Not <c>ceil(log2(count))</c> — the two agree everywhere except exact
    /// powers of two, which is why a fixture with a handful of classes cannot tell them apart.
    /// TF2's counts include both shapes: 256 in protocol 15, 362 in protocol 24.
    /// </remarks>
    public static int ClassId(int classCount) => Log2Floor(classCount) + 1;

    /// <summary>Bits an explicit string table entry index occupies.</summary>
    /// <param name="maxEntries">The table's declared capacity.</param>
    /// <returns>The width used for entry indices on the wire.</returns>
    /// <remarks>
    /// <c>floor(log2(capacity))</c>, with no <c>+ 1</c> — an index addresses the capacity rather
    /// than counting it. Every capacity observed in the corpus is a power of two, where floor and
    /// ceiling agree, so this is one of the widths no demo held here can adjudicate.
    /// </remarks>
    public static int StringTableIndex(int maxEntries) => Log2Floor(maxEntries);

    /// <summary>Bits the entry count of a <c>svc_CreateStringTable</c> occupies.</summary>
    /// <param name="maxEntries">The table's declared capacity.</param>
    /// <returns>The width used for the count on the wire.</returns>
    /// <remarks>
    /// One wider than an index, because a full table's count is the capacity itself and does not
    /// fit in the width that addresses it.
    /// </remarks>
    public static int StringTableEntryCount(int maxEntries) => Log2Floor(maxEntries) + 1;

    /// <summary>
    /// <c>floor(log2(value))</c> — the position of the highest set bit, and 0 at or below 1.
    /// </summary>
    /// <remarks>
    /// The engine's <c>Q_log2</c>. Every derived width in this file is this plus a constant, which
    /// is the whole reason they belong together: the constant is what differs between them, and a
    /// second hand-rolled log2 beside one of them is how they drift.
    /// </remarks>
    private static int Log2Floor(int value)
    {
        int bits = 0;
        while (value > 1)
        {
            // Stryker disable once Assignment: >>> differs from >> only for a negative value,
            // and the loop condition means a negative never reaches here. Equivalent mutant.
            value >>= 1;
            bits++;
        }

        return bits;
    }
}
