using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Which models the engine draws twice, and — the half that matters more — which it does not.
/// </summary>
/// <remarks>
/// **Written to settle a claim this repository had already made and never checked.** The handoff of
/// 2026-08-28 filed two-pass as missing: *"This project has no two-pass concept and draws every
/// model once."* Both halves are false. `Device3D.RenderFrame` draws every model twice, and
/// `WorldRenderer.DrawModel` filters each pass by material — which IS
/// <c>STUDIORENDER_DRAW_OPAQUE_ONLY</c> / <c>_TRANSLUCENT_ONLY</c>, already implemented.
///
/// So the divergence runs the OTHER way: this renderer two-passes everything, and the engine
/// two-passes only what asked for it. That is the thing to fix, and it is not the thing that was
/// filed.
///
/// **The engine's decision, in three steps, each in a different file.**
///
/// 1. <c>C_BaseEntity::GetRenderGroup</c> (<c>c_baseentity.cpp:5677-5701</c>) classifies the
///    ENTITY. Two-pass is reachable only from translucent:
///
/// <code>
/// if ( nFXBlend == 0 ) return RENDER_GROUP_OPAQUE_ENTITY;   // Don't need to sort invisible stuff
/// RenderGroup_t renderGroup = (modelType == mod_brush) ? RENDER_GROUP_OPAQUE_BRUSH : RENDER_GROUP_OPAQUE_ENTITY;
/// if ( ( nFXBlend != 255 ) || IsTransparent() )
///     renderGroup = ( m_nRenderMode != kRenderEnvironmental ) ? RENDER_GROUP_TRANSLUCENT_ENTITY : RENDER_GROUP_OTHER;
/// if ( ( renderGroup == RENDER_GROUP_TRANSLUCENT_ENTITY ) &amp;&amp; ( modelinfo-&gt;IsTranslucentTwoPass( model ) ) )
///     renderGroup = RENDER_GROUP_TWOPASS;
/// </code>
///
/// 2. <c>CClientLeafSystem</c> stores it — and <c>RENDER_GROUP_TWOPASS</c> is a REQUEST, never a
///    stored state. Both <c>AddRenderable</c> (<c>:713</c>) and <c>SetRenderGroup</c>
///    (<c>:1331</c>) turn it into <c>RENDER_GROUP_TRANSLUCENT_ENTITY</c> plus a flag bit.
///
/// 3. <c>CollateRenderablesInLeaf</c> (<c>:1701-1714</c>) turns the pair into list entries, and
///    re-tests the alpha:
///
/// <code>
/// bool bTwoPass = ((renderable.m_Flags &amp; RENDER_FLAGS_TWOPASS) != 0) &amp;&amp; ( nAlpha == 255 );
/// if ( info.m_bDrawTranslucentObjects ) AddRenderableToRenderList( … renderable.m_RenderGroup … );
/// if ( bTwoPass ) AddRenderableToRenderList( … RENDER_GROUP_OPAQUE_ENTITY … );
/// </code>
///
/// **What authors it is <c>$mostlyopaque</c> in the QC**, which becomes
/// <c>STUDIOHDR_FLAGS_TRANSLUCENT_TWOPASS</c>. TF2's own workshop importer states the rule as a
/// to-do: *"QC with any $translucent 1 VMT should have $mostlyopaque"*
/// (<c>tf/workshop/item_import.cpp:10</c>) — which says plainly that a translucent TF2 model
/// WITHOUT it is a content mistake the engine does not paper over.
///
/// **The consequence is the whole point of the flag**, and it is why copying it matters: a mixed
/// model that is not flagged goes wholly into the translucent pass, its solid parts included, drawn
/// without depth writes. Splitting it anyway looks better and is not what the engine does — and
/// "looks better" is exactly the argument D89 refuses.
///
/// Evidence class: read from published source throughout. Nothing here is interpolated.
/// </remarks>
public sealed class TwoPassConformanceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>That two-pass is reachable only from translucent, never from opaque.</summary>
    [Test]
    public void Sdk_TheTwoPassGroup_IsReachableOnlyFromTranslucent()
    {
        string source = Flat(Sdk("src/game/client/c_baseentity.cpp"));

        source.ShouldContain("if ( ( renderGroup == RENDER_GROUP_TRANSLUCENT_ENTITY ) &&");
        source.ShouldContain("( modelinfo->IsTranslucentTwoPass( model ) ) )");
        source.ShouldContain("renderGroup = RENDER_GROUP_TWOPASS;");

        // And the leaf system asks the same question the same way round: the two-pass question is
        // only put to a renderable already classified translucent.
        string leaf = Flat(Sdk("src/game/client/clientleafsystem.cpp"));

        leaf.ShouldContain("if ( group == RENDER_GROUP_TRANSLUCENT_ENTITY )");
        leaf.ShouldContain("bTwoPass = pRenderable->IsTwoPass( );");
    }

    /// <summary>That being two-pass is a property of the MODEL, and being translucent is not.</summary>
    /// <remarks>
    /// **The asymmetry is the load-bearing part.** <c>IsTwoPass</c> asks the model alone;
    /// <c>IsTransparent</c> ORs the model's materials with the ENTITY's render mode. So a model can
    /// be two-pass-capable and still be drawn in one pass, and an entity with an opaque model can be
    /// translucent because of how it was spawned.
    /// </remarks>
    [Test]
    public void Sdk_IsTwoPass_AsksTheModelWhileIsTransparentAlsoAsksTheEntity()
    {
        string source = Flat(Sdk("src/game/client/c_baseentity.cpp"));

        source.ShouldContain("return modelinfo->IsTranslucentTwoPass( GetModel() );");
        source.ShouldContain("return modelIsTransparent || (m_nRenderMode != kRenderNormal);");
    }

    /// <summary>That a two-pass renderable joins both lists, and only at full alpha.</summary>
    [Test]
    public void Sdk_ATwoPassRenderable_JoinsBothListsOnlyAtFullAlpha()
    {
        string source = Flat(Sdk("src/game/client/clientleafsystem.cpp"));

        source.ShouldContain(
            "bool bTwoPass = ((renderable.m_Flags & RENDER_FLAGS_TWOPASS) != 0) && ( nAlpha == 255 );");

        source.ShouldContain("if ( info.m_bDrawTranslucentObjects )");
        source.ShouldContain("worldListLeafIndex, RENDER_GROUP_OPAQUE_ENTITY, handle, bTwoPass );");
    }

    /// <summary>That an invisible renderable joins neither list.</summary>
    /// <remarks>
    /// Alpha zero is skipped outright before any grouping — <c>if ( nAlpha == 0 ) continue;</c>
    /// (<c>:1631</c>) — with Valve's note that an OPAQUE object may legitimately carry alpha zero
    /// *"because they don't have to be sorted"*. So zero is neither "opaque" nor "translucent"; it
    /// is "not drawn", and a renderer that folded it into either would draw an invisible entity.
    /// </remarks>
    [Test]
    public void Sdk_AnInvisibleRenderable_IsSkippedBeforeAnyGrouping()
    {
        string source = Flat(Sdk("src/game/client/clientleafsystem.cpp"));

        source.ShouldContain("nAlpha = renderable.m_pRenderable->GetFxBlend();");
        source.ShouldContain("if ( nAlpha == 0 )");
        source.ShouldContain("// NOTE: OPAQUE objects can have alpha == 0.");
    }

    /// <summary>That the opaque half of a two-pass renderable is NOT size bucketed.</summary>
    /// <remarks>
    /// **A detail worth one assertion because it is invisible in prose.** The other branch of the
    /// same `if` runs `DetectBucketedRenderGroup` and lands the renderable in one of four buckets by
    /// size; the two-pass branch writes the literal <c>RENDER_GROUP_OPAQUE_ENTITY</c>, which is the
    /// SMALLEST bucket and the default. So a huge two-pass model draws with the crates, not with the
    /// trees — and a renderer that bucketed it "for consistency" would draw it earlier than the
    /// engine does.
    /// </remarks>
    [Test]
    public void Sdk_TheOpaqueHalfOfATwoPassRenderable_TakesTheDefaultGroupRatherThanASizeBucket()
    {
        string source = Flat(Sdk("src/game/client/clientleafsystem.cpp"));

        // The bucketing lives in the NOT-translucent branch only.
        source.ShouldContain("group = DetectBucketedRenderGroup( group, fDimension );");

        // The two-pass branch names the default group outright.
        source.ShouldContain("worldListLeafIndex, RENDER_GROUP_OPAQUE_ENTITY, handle, bTwoPass );");

        // And that constant is the smallest bucket, not a separate one.
        Flat(Sdk("src/public/engine/IClientLeafSystem.h"))
            .ShouldContain("RENDER_GROUP_OPAQUE_ENTITY, // Opaque entity (smallest size, or default)");
    }

    /// <summary>That <c>RENDER_GROUP_TWOPASS</c> is never stored on a renderable.</summary>
    [Test]
    public void Sdk_TheTwoPassGroup_IsConvertedToTranslucentPlusAFlagWhenStored()
    {
        string source = Flat(Sdk("src/game/client/clientleafsystem.cpp"));

        source.ShouldContain("if ( group == RENDER_GROUP_TWOPASS )");
        source.ShouldContain("group = RENDER_GROUP_TRANSLUCENT_ENTITY;");
        source.ShouldContain("flags |= RENDER_FLAGS_TWOPASS;");

        // SetRenderGroup does the same, and CLEARS the bit when the group is anything else — so the
        // flag cannot survive a reclassification.
        source.ShouldContain("pInfo->m_Flags &= ~RENDER_FLAGS_TWOPASS;");
    }

    /// <summary>That the draw flags say which half of a two-pass model to draw.</summary>
    /// <remarks>
    /// <c>STUDIO_TWOPASS</c> says "draw half of me"; <c>STUDIO_TRANSPARENCY</c> says which half. The
    /// pair reaches the studio renderer as <c>STUDIORENDER_DRAW_OPAQUE_ONLY</c> or
    /// <c>_TRANSLUCENT_ONLY</c> and a brush model as <c>DBM_DRAW_OPAQUE_ONLY</c> /
    /// <c>_TRANSLUCENT_ONLY</c>.
    /// </remarks>
    [Test]
    public void Sdk_TheDrawFlags_CarryTwoPassOnBothPassesAndTransparencyOnOne()
    {
        string view = Flat(Sdk("src/game/client/viewrender.cpp"));

        // The opaque pass: STUDIO_RENDER, plus STUDIO_TWOPASS, and no STUDIO_TRANSPARENCY.
        view.ShouldContain("int flags = nDefaultFlags | STUDIO_RENDER;");

        // The translucent pass: the same plus STUDIO_TRANSPARENCY.
        view.ShouldContain("int flags = STUDIO_RENDER | STUDIO_TRANSPARENCY;");
        view.ShouldContain("flags |= STUDIO_TWOPASS;");

        // And what the pair means where it is read.
        Flat(Sdk("src/game/client/c_func_areaportalwindow.cpp")).ShouldContain(
            "mode = ( flags & STUDIO_TRANSPARENCY ) ? DBM_DRAW_TRANSLUCENT_ONLY : DBM_DRAW_OPAQUE_ONLY;");
    }

    /// <summary>That a model WITHOUT the flag draws entirely in whichever one pass it belongs to.</summary>
    /// <remarks>
    /// **This is the assertion the whole change turns on**, and the only one that describes what
    /// this renderer currently gets wrong. `DBM_DRAW_ALL` is the default and the two-pass branch is
    /// the exception; `STUDIORENDER_DRAW_ENTIRE_MODEL` is literally zero. So a translucent model
    /// with no <c>$mostlyopaque</c> draws ALL of itself in the translucent pass — opaque meshes
    /// included, unsorted against its own translucent ones, and without depth writes.
    ///
    /// Splitting it by material regardless, which is what this project does today, is a departure
    /// even though it produces a tidier picture. That is precisely the trade D89 says is not ours to
    /// make.
    /// </remarks>
    [Test]
    public void Sdk_AModelWithoutTheTwoPassFlag_DrawsEveryMeshInOnePass()
    {
        string entity = Flat(Sdk("src/game/client/c_baseentity.cpp"));

        entity.ShouldContain("DrawBrushModelMode_t mode = DBM_DRAW_ALL;");
        entity.ShouldContain("if ( bTwoPass )");
        entity.ShouldContain(
            "mode = bDrawingTranslucency ? DBM_DRAW_TRANSLUCENT_ONLY : DBM_DRAW_OPAQUE_ONLY;");

        Flat(Sdk("src/public/istudiorender.h"))
            .ShouldContain("STUDIORENDER_DRAW_ENTIRE_MODEL = 0,");
    }

    /// <summary>That TF2's own tooling treats the flag as required for a translucent model.</summary>
    /// <remarks>
    /// Not engine behaviour, and marked as such: it is TF2 content policy, and it is the reason a
    /// shipped TF2 model with translucent materials can be expected to carry the flag. It bounds how
    /// much of the corpus the change below can affect.
    /// </remarks>
    [Test]
    public void Sdk_Tf2sWorkshopImporter_TreatsMostlyOpaqueAsRequiredForATranslucentModel()
    {
        Flat(Sdk("src/game/client/tf/workshop/item_import.cpp"))
            .ShouldContain("QC with any $translucent 1 VMT should have $mostlyopaque");
    }

    /// <summary>That this project's classification reproduces <c>GetRenderGroup</c>.</summary>
    /// <remarks>
    /// **The cases are chosen so a wrong implementation disagrees**, which is the part a table of
    /// plausible inputs usually gets wrong. Each row differs from its neighbour in ONE input, so a
    /// rule that ignored an input entirely would collapse two rows onto one answer.
    /// </remarks>
    [TestCase(false, false, false, 255, RenderModes.Normal, RenderGroup.OpaqueEntity)]
    [TestCase(false, false, true, 255, RenderModes.Normal, RenderGroup.OpaqueBrush)]
    [TestCase(true, false, false, 255, RenderModes.Normal, RenderGroup.TranslucentEntity)]
    [TestCase(true, true, false, 255, RenderModes.Normal, RenderGroup.TwoPass)]

    // Two-pass capable, but the model is opaque, so it never reaches the translucent branch.
    [TestCase(false, true, false, 255, RenderModes.Normal, RenderGroup.OpaqueEntity)]

    // A partly-faded entity is translucent whatever its materials say...
    [TestCase(false, false, false, 128, RenderModes.Normal, RenderGroup.TranslucentEntity)]

    // ...and reaches two-pass through the same door.
    [TestCase(false, true, false, 128, RenderModes.Normal, RenderGroup.TwoPass)]

    // Invisible is classified opaque so nothing sorts it, ahead of the skip at collate time.
    [TestCase(true, true, false, 0, RenderModes.Normal, RenderGroup.OpaqueEntity)]

    // Environmental never draws, and takes precedence over the two-pass promotion.
    [TestCase(true, true, false, 255, RenderModes.Environmental, RenderGroup.Other)]
    public void For_ForEachOfValvesCases_MatchesGetRenderGroup(
        bool modelIsTranslucent,
        bool modelIsTwoPass,
        bool isBrushModel,
        int alpha,
        int renderMode,
        RenderGroup expected) =>
        RenderGroups.For(modelIsTranslucent, modelIsTwoPass, isBrushModel, alpha, renderMode)
            .ShouldBe(expected);

    /// <summary>That storing a two-pass request keeps translucent plus a flag, as the engine does.</summary>
    [Test]
    public void Store_ForATwoPassRequest_KeepsTranslucentAndRaisesTheFlag()
    {
        RenderGroups.Store(RenderGroup.TwoPass)
            .ShouldBe((RenderGroup.TranslucentEntity, true));

        // Every other group stores itself, with the flag down — SetRenderGroup's else branch.
        RenderGroups.Store(RenderGroup.TranslucentEntity)
            .ShouldBe((RenderGroup.TranslucentEntity, false));

        RenderGroups.Store(RenderGroup.OpaqueEntity).ShouldBe((RenderGroup.OpaqueEntity, false));
    }

    /// <summary>That the lists a renderable joins follow <c>CollateRenderablesInLeaf</c>.</summary>
    /// <remarks>
    /// The control is the third row: the SAME renderable that joins both lists at full alpha joins
    /// only the translucent one below it. Without that row the alpha test could be missing entirely
    /// and every row would still pass.
    /// </remarks>
    [TestCase(RenderGroup.OpaqueEntity, false, 255, true, false)]
    [TestCase(RenderGroup.OpaqueBrush, false, 255, true, false)]
    [TestCase(RenderGroup.TranslucentEntity, false, 255, false, true)]
    [TestCase(RenderGroup.TranslucentEntity, true, 255, true, true)]
    [TestCase(RenderGroup.TranslucentEntity, true, 254, false, true)]
    [TestCase(RenderGroup.TranslucentEntity, true, 0, false, false)]
    [TestCase(RenderGroup.OpaqueEntity, false, 0, false, false)]
    [TestCase(RenderGroup.Other, false, 255, false, false)]
    public void Lists_ForEachStoredGroup_MatchesCollateRenderablesInLeaf(
        RenderGroup stored, bool twoPass, int alpha, bool opaque, bool translucent) =>
        RenderGroups.Lists(stored, twoPass, alpha).ShouldBe((opaque, translucent));

    /// <summary>That asking for the lists of an unstored two-pass group is refused.</summary>
    /// <remarks>
    /// **The executable half of "TwoPass is never stored".** The engine's guarantee is structural —
    /// nothing can hold that value — and the only way to keep the guarantee here is to refuse the
    /// input rather than quietly treat it as translucent. A fallback would draw the model correctly
    /// and leave the caller's missing <c>Store</c> in place, which is the shape of no-op this
    /// project has shipped before with a green suite.
    /// </remarks>
    [Test]
    public void Lists_ForATwoPassGroupThatWasNeverStored_IsRefused() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => RenderGroups.Lists(RenderGroup.TwoPass, twoPass: true, alpha: 255));

    private static string Sdk(string path) =>
        Skip.Unless(SourceSdk.Text(path), SourceSdk.Missing);

    private static string Flat(string source) =>
        Regex.Replace(source, @"[ \t]+", " ", RegexOptions.None, Limit);
}
