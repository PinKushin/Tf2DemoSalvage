using System;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// Which list a renderable is drawn from, as <c>RenderGroup_t</c> names them.
/// </summary>
/// <remarks>
/// **Valve's enum, minus the size buckets.** <c>IClientLeafSystem.h:32</c> expands
/// <c>RENDER_GROUP_OPAQUE_STATIC</c> and <c>RENDER_GROUP_OPAQUE_ENTITY</c> into four size buckets
/// each; that expansion lives in <see cref="OpaqueBuckets"/> here, because it is a question about
/// draw ORDER within the opaque list rather than about which list something joins. Keeping both in
/// one enum would mean this type had to change whenever the bucket count did.
///
/// **<see cref="TwoPass"/> is a request, not a state.** The engine never stores it: both
/// <c>CClientLeafSystem::AddRenderable</c> (<c>clientleafsystem.cpp:713</c>) and
/// <c>SetRenderGroup</c> (<c>:1331</c>) immediately rewrite it to
/// <see cref="TranslucentEntity"/> plus a flag bit. <see cref="RenderGroups.Store"/> is that step,
/// and it is separate here for the same reason it is separate there — so nothing downstream has to
/// know the group could have been two-pass.
/// </remarks>
public enum RenderGroup
{
    /// <summary><c>RENDER_GROUP_OPAQUE_ENTITY</c> — the default, and the smallest size bucket.</summary>
    OpaqueEntity,

    /// <summary><c>RENDER_GROUP_OPAQUE_BRUSH</c> — a brush model, drawn before the props.</summary>
    OpaqueBrush,

    /// <summary><c>RENDER_GROUP_TRANSLUCENT_ENTITY</c> — blended, so sorted back to front.</summary>
    TranslucentEntity,

    /// <summary><c>RENDER_GROUP_TWOPASS</c> — *"Implied opaque and translucent in two passes"*.</summary>
    TwoPass,

    /// <summary><c>RENDER_GROUP_OTHER</c> — Valve's own comment: *"Unclassfied. Won't get drawn."*</summary>
    Other,
}

/// <summary>
/// The <c>m_nRenderMode</c> values that change which list an entity joins.
/// </summary>
/// <remarks>
/// **Two of eleven, because two are all that <c>GetRenderGroup</c> tests.** <c>RenderMode_t</c>
/// (<c>public/const.h:351</c>) has eleven members and the grouping decision distinguishes exactly
/// these: anything that is not <see cref="Normal"/> makes the entity transparent, and
/// <see cref="Environmental"/> additionally makes it undrawn. Declaring the other nine would be
/// nine claims that this project handles them — the failure `docs/CONFORMANCE.md` exists to catch.
///
/// **Nothing decodes <c>m_nRenderMode</c> yet**, so every caller here passes <see cref="Normal"/>.
/// That is a named gap rather than a silent one: see <see cref="RenderGroups.For"/>.
/// </remarks>
public static class RenderModes
{
    /// <summary><c>kRenderNormal</c> — the entity's own materials decide, and nothing else.</summary>
    public const int Normal = 0;

    /// <summary><c>kRenderEnvironmental</c> — *"not drawn, used for environmental effects"*.</summary>
    public const int Environmental = 6;
}

/// <summary>
/// Which of the engine's render lists a renderable joins, and whether it joins two.
/// </summary>
/// <remarks>
/// **This is <c>C_BaseEntity::GetRenderGroup</c> and <c>CollateRenderablesInLeaf</c>, transcribed.**
/// It exists because this renderer had the second half of two-pass without the first: every model
/// was drawn twice, once filtered to its opaque materials and once to its blended ones, with nothing
/// asking whether the engine would have drawn it twice at all. The material filter is right — it is
/// <c>STUDIORENDER_DRAW_OPAQUE_ONLY</c> / <c>_TRANSLUCENT_ONLY</c> — and applying it unconditionally
/// is not.
///
/// **What the difference costs, concretely.** A model with any translucent material and no
/// <c>$mostlyopaque</c> belongs wholly to the translucent pass: its solid parts are drawn there too,
/// unsorted against its own blended parts and with depth writes off, so its faces stop occluding
/// each other. Splitting it anyway produces a tidier picture than the engine's, and D89 is explicit
/// that a nicer picture does not buy a departure. The flag is how an author opts into the tidier
/// picture, and the engine honours the author rather than deciding for them.
///
/// **Three steps, deliberately kept apart**, because the engine keeps them apart and each is in a
/// different file: <see cref="For"/> classifies (<c>c_baseentity.cpp:5677</c>),
/// <see cref="Store"/> records (<c>clientleafsystem.cpp:713</c>), <see cref="Lists"/> emits
/// (<c>clientleafsystem.cpp:1701</c>). Collapsing them into one predicate loses the fact that the
/// alpha is tested TWICE, at classification and again at emission, against different thresholds.
/// </remarks>
public static class RenderGroups
{
    /// <summary>The alpha at which an entity is not blended at all.</summary>
    public const int FullyOpaque = 255;

    /// <summary>The alpha at which an entity is not drawn at all.</summary>
    public const int Invisible = 0;

    /// <summary>Which group an entity belongs to — <c>C_BaseEntity::GetRenderGroup</c>.</summary>
    /// <param name="modelIsTranslucent">
    /// Whether any material the model currently shows is blended, additive or modulating —
    /// <c>IVModelInfo::IsTranslucent</c>. ANY, not all: <c>STUDIOHDR_FLAGS_FORCE_OPAQUE</c> exists
    /// precisely to override that answer, which it would not need to if the test were "all".
    /// </param>
    /// <param name="modelIsTwoPass">
    /// Whether the model carries <c>STUDIOHDR_FLAGS_TRANSLUCENT_TWOPASS</c> —
    /// <c>StudioModelInfo.IsTranslucentTwoPass</c>.
    /// </param>
    /// <param name="isBrushModel">Whether it is a brush model rather than a studio one.</param>
    /// <param name="alpha">
    /// <c>GetFxBlend()</c>, nought to 255. **Nothing decodes this yet** — <c>m_clrRender</c> and
    /// <c>m_nRenderFX</c> are not read from the demo, and <c>ComputeFxBlend</c> is a 210-line
    /// time-based switch — so every caller passes <see cref="FullyOpaque"/>. Present as a parameter
    /// rather than assumed, so that wiring it up later is a change at the call sites and not here.
    /// </param>
    /// <param name="renderMode">
    /// <c>m_nRenderMode</c>. Also not decoded yet; see <see cref="RenderModes"/>.
    /// </param>
    /// <returns>The group, which may be <see cref="RenderGroup.TwoPass"/>.</returns>
    /// <remarks>
    /// **The order of the tests is load-bearing and is not the order it reads in.** Invisible is
    /// decided FIRST and answers opaque — Valve's comment is *"Don't need to sort invisible
    /// stuff"* — so an alpha-zero entity never reaches the translucent branch and never gets
    /// promoted to two-pass, however its model is flagged. Then transparency, then the two-pass
    /// promotion, which can only fire from translucent.
    ///
    /// **<c>kRenderEnvironmental</c> beats the promotion** because it lands the entity in
    /// <see cref="RenderGroup.Other"/> rather than in translucent, and the promotion tests for
    /// translucent exactly.
    /// </remarks>
    public static RenderGroup For(
        bool modelIsTranslucent,
        bool modelIsTwoPass,
        bool isBrushModel = false,
        int alpha = FullyOpaque,
        int renderMode = RenderModes.Normal)
    {
        // "Don't need to sort invisible stuff" — c_baseentity.cpp:5677. Opaque, and dropped later
        // by Lists rather than here, which is where the engine drops it.
        if (alpha == Invisible)
        {
            return RenderGroup.OpaqueEntity;
        }

        RenderGroup group = isBrushModel ? RenderGroup.OpaqueBrush : RenderGroup.OpaqueEntity;

        // IsTransparent() — c_baseentity.cpp:1823. The model's materials OR the entity's mode; a
        // model of entirely opaque materials is still transparent if it was spawned with one.
        bool transparent = modelIsTranslucent || renderMode != RenderModes.Normal;

        if (alpha != FullyOpaque || transparent)
        {
            group = renderMode != RenderModes.Environmental
                ? RenderGroup.TranslucentEntity
                : RenderGroup.Other;
        }

        return group == RenderGroup.TranslucentEntity && modelIsTwoPass
            ? RenderGroup.TwoPass
            : group;
    }

    /// <summary>What the leaf system actually records — <c>CClientLeafSystem::SetRenderGroup</c>.</summary>
    /// <param name="requested">What <see cref="For"/> returned.</param>
    /// <returns>The group to store, and whether the two-pass flag is raised.</returns>
    /// <remarks>
    /// **The flag is CLEARED for every other group, not merely left alone**
    /// (<c>clientleafsystem.cpp:1343</c>), which is what stops it surviving a reclassification — a
    /// model that stops being translucent must stop being two-pass in the same step.
    /// </remarks>
    public static (RenderGroup Group, bool TwoPass) Store(RenderGroup requested) =>
        requested == RenderGroup.TwoPass
            ? (RenderGroup.TranslucentEntity, true)
            : (requested, false);

    /// <summary>Which lists it joins — <c>CollateRenderablesInLeaf</c>.</summary>
    /// <param name="stored">The group as <see cref="Store"/> recorded it.</param>
    /// <param name="twoPass">The flag <see cref="Store"/> raised.</param>
    /// <param name="alpha">Its alpha this frame.</param>
    /// <returns>Whether it draws in the opaque pass, and whether in the translucent one.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="stored"/> is <see cref="RenderGroup.TwoPass"/>, which the engine never
    /// stores.
    /// </exception>
    /// <remarks>
    /// **The alpha is tested a second time here, and against a different question.**
    /// <see cref="For"/> asked "is it fully opaque" to decide the group; this asks it again to
    /// decide whether the two-pass split applies at all — <c>bTwoPass = flag &amp;&amp; nAlpha ==
    /// 255</c>. So a two-pass model that fades draws once, wholly, in the translucent pass. One
    /// test standing in for both would be wrong for a faded model.
    ///
    /// **Valve gates the alpha read on <c>m_bDrawTranslucentObjects</c>**, a property of the VIEW
    /// which is false only for the shadow-depth pass. This project has no such pass, so the gate is
    /// not modelled; adding one means adding the parameter here, and this note is where to start.
    /// </remarks>
    public static (bool Opaque, bool Translucent) Lists(
        RenderGroup stored, bool twoPass, int alpha)
    {
        // Refused rather than coped with. The engine cannot produce this, so a caller that does has
        // skipped Store — and a silent fallback would draw the model correctly while leaving the
        // wiring bug in place. See docs/memory/unreachable-can-be-proved-not-just-observed.md.
        if (stored == RenderGroup.TwoPass)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stored),
                "RENDER_GROUP_TWOPASS is never stored on a renderable; call Store first.");
        }

        // "Prevent culling if the renderable is invisible" — clientleafsystem.cpp:1631, which skips
        // before it looks at the group at all. An OPAQUE entity may legitimately be here.
        if (alpha == Invisible || stored == RenderGroup.Other)
        {
            return (false, false);
        }

        if (stored != RenderGroup.TranslucentEntity)
        {
            return (true, false);
        }

        return (twoPass && alpha == FullyOpaque, true);
    }
}
