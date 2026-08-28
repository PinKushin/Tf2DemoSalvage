namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// What a model as a whole declares about itself, from <c>studiohdr_t.flags</c>.
/// </summary>
/// <remarks>
/// **Separate from <see cref="StudioBoneFlags"/> and <c>StudioFlags</c> on purpose.** Three unrelated
/// flag families share <c>studio.h</c> and overlap in bit position: these are the MODEL's
/// (<c>STUDIOHDR_FLAGS_*</c>), <see cref="StudioBoneFlags"/> holds a bone's <c>BONE_USED_BY_*</c>,
/// and <c>StudioFlags</c> holds an animation's storage bits. <c>0x08</c> means "render me in two
/// passes" here, "always procedural"-adjacent there, and something else again in the third.
///
/// **Only the flags something reads are declared.** The header defines about twenty; a constant
/// nothing consults is a claim that this project handles it, which is the failure
/// `docs/CONFORMANCE.md` exists to avoid. Add one when a reader wants it, and add the conformance
/// assertion in the same change.
///
/// Values are checked against <c>public/studio.h</c> by <c>StudioHeaderFlagsConformanceTests</c>, so
/// none of them is a remembered number.
/// </remarks>
public static class StudioModelFlags
{
    /// <summary>
    /// <c>STUDIOHDR_FLAGS_FORCE_OPAQUE</c> — draw it opaque even though parts of it are not.
    /// </summary>
    /// <remarks>
    /// **The flag that reveals what the engine means by a translucent MODEL.** Valve's comment is
    /// *"Use this when there are translucent parts to the model but we're not going to sort it"* —
    /// so <c>IVModelInfo::IsTranslucent</c> answers yes when ANY material is translucent, and this
    /// bit is how an author says "yes, and draw it opaque anyway". A flag whose only job is to
    /// suppress an answer would be meaningless if the answer were "all materials".
    ///
    /// Nothing reads it yet; it is declared beside <see cref="TranslucentTwoPass"/> because the two
    /// are the same decision seen from opposite sides, and because the reasoning above is the
    /// evidence for how <see cref="TranslucentTwoPass"/> is used.
    /// </remarks>
    public const int ForceOpaque = 0x00000004;

    /// <summary>
    /// <c>STUDIOHDR_FLAGS_TRANSLUCENT_TWOPASS</c> — draw the solid half in the opaque pass and the
    /// blended half in the translucent one.
    /// </summary>
    /// <remarks>
    /// **The only thing that entitles a model to be drawn twice**, and Valve's comment says exactly
    /// what it buys: *"Use this when we want to render the opaque parts during the opaque pass and
    /// the translucent parts during the translucent pass"*.
    ///
    /// **Authored as <c>$mostlyopaque</c> in the QC**, which is the name worth knowing because it is
    /// what an artist types and what TF2's own workshop importer tells them to type: *"QC with any
    /// $translucent 1 VMT should have $mostlyopaque"* (<c>tf/workshop/item_import.cpp:10</c>).
    ///
    /// **Without it, a model with any translucent material draws ENTIRELY in the translucent pass** —
    /// its solid parts included, with no depth writes, so its own faces stop occluding each other.
    /// That is not a bug in the engine; it is the cost the flag exists to let an author avoid. See
    /// <c>RenderGroups</c>.
    /// </remarks>
    public const int TranslucentTwoPass = 0x00000008;

    /// <summary>
    /// <c>STUDIOHDR_FLAGS_STATIC_PROP</c> — compiled with <c>$staticprop</c>.
    /// </summary>
    /// <remarks>
    /// *"Means there's no bones and no transforms"*, which makes it the one bit of this word whose
    /// value can be predicted for a given model without measuring it — and therefore the one that
    /// can prove the word is being read from the right offset at all. That is what
    /// <c>StudioHeaderFlagsConformanceTests</c> uses it for.
    /// </remarks>
    public const int StaticProp = 0x00000010;
}
