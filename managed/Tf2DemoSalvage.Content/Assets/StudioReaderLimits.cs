namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// How much of a studio model this project will read before deciding the header is corrupt.
/// </summary>
/// <remarks>
/// **These are sanity bounds, not the format's limits, and the difference matters in one
/// direction.** A model from a downloaded map is untrusted input (D32): a count read from a
/// malformed header can ask for a billion bones, and allocating on it is the whole attack. So every
/// count is capped before anything is sized from it.
///
/// **The failure mode of a cap is refusing a file the game plays**, which is why they sit well above
/// Valve's own limits rather than at them. <c>MAXSTUDIOBONES</c> is 128 and this caps at 1024 — the
/// bound exists to catch a number in the millions, not to enforce a schema, and a cap set at the
/// engine's exact limit would turn every future format revision into a rejection.
///
/// <c>CapacityGuardTests</c> asserts the one-directional claim: no cap here may be BELOW the
/// engine's declared limit. It deliberately does not check the other side, because how far above is
/// a judgement about allocation rather than about the format.
/// </remarks>
internal static class StudioReaderLimits
{
    /// <summary>Bones one model may declare. <c>MAXSTUDIOBONES</c> is 128.</summary>
    public const int Bones = 1024;

    /// <summary>Entries in the skin table: references times families.</summary>
    /// <remarks>
    /// Not a count of skins but of the flattened table, so it is compared against
    /// <c>MAXSTUDIOSKINS</c> only as a lower bound — the table is at least as large as the number of
    /// materials, and usually several times it.
    /// </remarks>
    public const int SkinTableEntries = 65_536;

    /// <summary>Sequences one model may declare. TF2's classes are in the low hundreds.</summary>
    public const int Sequences = 4096;

    /// <summary>Pose parameters one model may declare. TF2's classes declare about two dozen.</summary>
    public const int PoseParameters = 256;

    /// <summary>Models one may include for its animations.</summary>
    public const int IncludedModels = 64;
}
