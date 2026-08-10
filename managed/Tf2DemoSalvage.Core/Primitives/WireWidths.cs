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
    public static int ClassId(int classCount)
    {
        int bits = 0;
        while (classCount > 1)
        {
            // Stryker disable once Assignment: >>> differs from >> only for a negative value,
            // and the loop condition means a negative never reaches here. Equivalent mutant.
            classCount >>= 1;
            bits++;
        }

        return bits + 1;
    }
}
