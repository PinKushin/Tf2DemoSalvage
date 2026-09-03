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

    /// <summary>Bone controllers one model may declare. <c>MAXSTUDIOBONECTRLS</c> is 4.</summary>
    /// <remarks>
    /// **Four, and the gap to this cap is the largest in the file proportionally.** That is
    /// deliberate rather than sloppy: the engine's limit is on how many a RUNTIME entity can be
    /// driven by, and a model file's table is not obliged to match it. Capping at 4 would refuse a
    /// model over a number that describes something else.
    /// </remarks>
    public const int BoneControllers = 256;

    /// <summary>IK chains one model may declare. The engine names no limit.</summary>
    /// <remarks>
    /// **<c>studio.h</c> declares no <c>MAXSTUDIOIKCHAINS</c>**, so this cap answers to nothing but
    /// plausibility — a humanoid has two or four, and a number in the thousands is a corrupt
    /// header. Stated because a cap with no engine constant behind it cannot be checked by
    /// <c>CapacityGuardTests</c> the way the others are, and a reader that pretended otherwise would
    /// be inventing a reference.
    /// </remarks>
    public const int IkChains = 256;

    /// <summary>Links in one IK chain. Three is the usual shape: hip, knee, foot.</summary>
    public const int IkLinks = 64;

    /// <summary>Sequences one sequence may automatically layer over itself.</summary>
    /// <remarks>
    /// **Plausibility rather than an engine constant**, like <see cref="IkChains"/>: Valve caps
    /// nothing here, and `numautolayers` is a number from a file that indexes 24-byte entries. A
    /// sequence layering more than a few dozen others is a corrupt header, and measured TF2 content
    /// is far below it — 1 of 76 sequences on one map and 6 of 142 on another declare ANY.
    /// </remarks>
    public const int MaximumAutoLayers = 256;

    /// <summary>IK rules one animation may declare.</summary>
    /// <remarks>
    /// **Plausibility, and generous next to what TF2 ships.** Measured across the scout's 1012
    /// animations: 2035 rules over 705 animations, so under three each. A cap of 256 is two orders
    /// above that and still bounds a corrupt <c>numikrules</c> before it indexes 152-byte entries
    /// off the end of a model.
    /// </remarks>
    public const int MaximumIkRules = 256;
}
