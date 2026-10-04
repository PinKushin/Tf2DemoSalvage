using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>DT_Beam</c> — what a <c>CBeam</c> says about itself, and where its borrowed base-entity fields live.
/// </summary>
/// <remarks>
/// **The receive table, `beam_shared.cpp:190-229`:**
///
/// <code>
/// RecvPropInt( RECVINFO(m_nBeamType) ), RecvPropInt( RECVINFO(m_nBeamFlags) ), RecvPropInt( RECVINFO(m_nNumBeamEnts) ),
/// RecvPropArray3( RECVINFO_ARRAY( m_hAttachEntity ), … ), RecvPropArray3( RECVINFO_ARRAY( m_nAttachIndex ), … ),
/// RecvPropInt( RECVINFO(m_nHaloIndex) ), RecvPropFloat( RECVINFO(m_fHaloScale) ), RecvPropFloat( RECVINFO(m_fWidth) ),
/// RecvPropFloat( RECVINFO(m_fEndWidth) ), RecvPropFloat( RECVINFO(m_fFadeLength) ), RecvPropFloat( RECVINFO(m_fAmplitude) ),
/// RecvPropFloat( RECVINFO(m_fStartFrame) ), RecvPropFloat( RECVINFO(m_fSpeed), 0, RecvProxy_Beam_ScrollSpeed ),
/// …, RecvPropInt( RECVINFO(m_clrRender) ), RecvPropInt( RECVINFO(m_nRenderFX) ), RecvPropInt( RECVINFO(m_nRenderMode) ),
/// RecvPropFloat( RECVINFO(m_flFrame) ), RecvPropVector( RECVINFO(m_vecEndPos) ), RecvPropInt( RECVINFO(m_nModelIndex) ),
/// RecvPropInt( RECVINFO(m_nMinDXLevel) ), RecvPropVector( RECVINFO_NAME(m_vecNetworkOrigin, m_vecOrigin) ),
/// RecvPropInt( RECVINFO_NAME(m_hNetworkMoveParent, moveparent), 0, RecvProxy_IntToMoveParent ),
/// </code>
///
/// inside <c>BEGIN_NETWORK_TABLE_NOBASE( CBeam, DT_Beam )</c> (`:147`). **NOBASE is the whole story**: the table inherits
/// nothing from <c>DT_BaseEntity</c> and declares its own copies of the model, origin, parent and render state, so every
/// accessor that reads only <c>DT_BaseEntity</c> answered null for a beam — and the timeline drops an entity with no model.
/// </remarks>
public sealed class BeamStateConformanceTests
{
    private const string Beam = "DT_Beam";

    /// <summary>A spotlight as <c>demostf-cp_process_f12-2026-08-08-2207</c>'s entity 416 enters.</summary>
    private static EntityState Spotlight()
    {
        EntityState beam = new(416, 0, 0, "CBeam");

        beam.Set($"{Beam}.m_nBeamType", PropertyValue.FromInt(0));
        beam.Set($"{Beam}.m_nBeamFlags", PropertyValue.FromInt(640));
        beam.Set($"{Beam}.m_nNumBeamEnts", PropertyValue.FromInt(2));
        beam.Set($"{Beam}.m_nHaloIndex", PropertyValue.FromInt(1191));
        beam.Set($"{Beam}.m_fHaloScale", PropertyValue.FromFloat(60f));
        beam.Set($"{Beam}.m_fWidth", PropertyValue.FromFloat(64.037f));
        beam.Set($"{Beam}.m_fEndWidth", PropertyValue.FromFloat(30.47f));
        beam.Set($"{Beam}.m_fFadeLength", PropertyValue.FromFloat(190.423f));
        beam.Set($"{Beam}.m_nMinDXLevel", PropertyValue.FromInt(90));
        beam.Set($"{Beam}.m_nModelIndex", PropertyValue.FromInt(1192));
        beam.Set($"{Beam}.m_nRenderMode", PropertyValue.FromInt(2));
        beam.Set($"{Beam}.m_clrRender", PropertyValue.FromInt(1090519039));
        beam.Set($"{Beam}.m_vecEndPos", PropertyValue.FromVector(-162.125f, 1588.875f, 656.031f));
        beam.Set($"{Beam}.m_vecOrigin", PropertyValue.FromVector(-171.969f, 1579.973f, 845.971f));
        beam.Set($"{Beam}.moveparent", PropertyValue.FromInt(2097151));
        beam.Set("m_hAttachEntity.000", PropertyValue.FromInt(2097151));
        beam.Set("m_nAttachIndex.000", PropertyValue.FromInt(0));

        return beam;
    }

    [Test]
    public void Beam_ForASpotlight_ReadsItsOwnTable()
    {
        SceneBeam beam = Spotlight().Beam()!;

        beam.Type.ShouldBe(0, "BEAM_POINTS");
        beam.Flags.ShouldBe(0x280, "FBEAM_SHADEOUT | FBEAM_NOTILE");
        beam.EntityCount.ShouldBe(2);
        beam.HaloIndex.ShouldBe(1191);
        beam.HaloScale.ShouldBe(60f);
        beam.Width.ShouldBe(64.037f);
        beam.EndWidth.ShouldBe(30.47f);
        beam.FadeLength.ShouldBe(190.423f);
        beam.MinDxLevel.ShouldBe(90);
        beam.EndPosition.ShouldBe((-162.125f, 1588.875f, 656.031f));
        beam.Ends.Count.ShouldBe(SceneBeam.MaximumEnds, "MAX_BEAM_ENTS, sent or not");
        beam.Ends[0].ShouldBe((2097151, 0));
    }

    /// <remarks>
    /// **The scroll speed arrives a tenth of what the wire says** — <c>RecvProxy_Beam_ScrollSpeed</c> stores
    /// <c>val *= 0.1</c> (`beam_shared.cpp:83-96`). A reader taking the wire value would scroll a beam's texture ten
    /// times too fast.
    /// </remarks>
    [Test]
    public void Beam_AScrollSpeedOnTheWire_ArrivesATenthOfIt()
    {
        EntityState beam = Spotlight();

        beam.Set($"{Beam}.m_fSpeed", PropertyValue.FromFloat(25f));

        beam.Beam()!.Speed.ShouldBe(2.5f);
    }

    /// <remarks>
    /// **What <c>C_Beam</c>'s constructor leaves for an unsent field** (`beam_shared.cpp:324-344`): a minimum DX level of
    /// nought and an HDR colour scale of ONE. A missing HDR scale read as zero would draw every beam black on an HDR
    /// client.
    /// </remarks>
    [Test]
    public void Beam_WithOnlyItsType_TakesTheConstructorsDefaults()
    {
        EntityState beam = new(1, 0, 0, "CBeam");

        beam.Set($"{Beam}.m_nBeamType", PropertyValue.FromInt(5));

        SceneBeam read = beam.Beam()!;

        read.Type.ShouldBe(5);
        read.HdrColourScale.ShouldBe(1f);
        read.MinDxLevel.ShouldBe(0);
        read.Ends[9].ShouldBe(((1 << 21) - 1, 0), "an unsent end names nothing");
    }

    /// <remarks>The control: every entity that is not a beam answers null, not a record of zeroes.</remarks>
    [Test]
    public void Beam_ForAnEntityThatIsNotABeam_IsNull()
    {
        new EntityState(1, 0, 0, "CBaseAnimating").Beam().ShouldBeNull();
    }

    /// <remarks>
    /// **The model, the render state and the parent are the ENTITY's members, received through the beam's own table**
    /// — <c>RecvPropInt( RECVINFO(m_nModelIndex) )</c> and its neighbours inside the NOBASE table. Read only from
    /// <c>DT_BaseEntity</c>, a beam has no model, so <c>DemoTimeline</c> refused it a track: 18,942 beams across the
    /// corpus, none drawn.
    /// </remarks>
    [Test]
    public void BaseMembers_ForABeam_AreReadFromItsOwnTable()
    {
        EntityState beam = Spotlight();

        beam.ModelIndex().ShouldBe(1192);
        beam.RenderMode().ShouldBe(2);
        beam.RenderAlpha().ShouldBe((byte)64);
        beam.RenderRgb().ShouldBe(((byte)255, (byte)255, (byte)255));
        beam.Origin().ShouldBe((-171.969f, 1579.973f, 845.971f));
        beam.AttachmentHandle().ShouldBeNull("the invalid handle is no parent");
    }

    /// <remarks>
    /// **A beam with a parent hangs off it** — <c>RecvPropInt( RECVINFO_NAME(m_hNetworkMoveParent, moveparent) )</c> in the
    /// NOBASE table (`beam_shared.cpp:226`), the same member <c>DT_BaseEntity</c> writes for everything else.
    /// </remarks>
    [Test]
    public void AttachmentHandle_ForABeamWithAMoveParent_IsTheParent()
    {
        EntityState beam = Spotlight();

        beam.Set($"{Beam}.moveparent", PropertyValue.FromInt((3 << 11) | 77));

        beam.AttachmentHandle().ShouldBe((3 << 11) | 77);
    }

    /// <remarks>
    /// **The control for the fallback order**: an entity that sends <c>DT_BaseEntity</c>'s model index is read from there,
    /// and a viewmodel — which declares a model index of its own on another NOBASE table — still answers null, as
    /// `ViewmodelStateTests` requires. The beam's table is named, not every table that happens to carry the name.
    /// </remarks>
    [Test]
    public void ModelIndex_ForAViewmodelsOwnTable_IsStillNotABeams()
    {
        EntityState viewmodel = new(1, 0, 0, "CTFViewModel");

        viewmodel.Set("DT_BaseViewModel.m_nModelIndex", PropertyValue.FromInt(42));

        viewmodel.ModelIndex().ShouldBeNull();
    }
}
