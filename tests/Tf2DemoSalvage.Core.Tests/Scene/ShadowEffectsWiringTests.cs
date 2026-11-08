using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>EF_NOSHADOW</c> and <c>EF_NORECEIVESHADOW</c> (`const.h:288-290`) read off <c>m_fEffects</c> and carried to the pose
/// the shadow pass reads (B702, finding 107).
/// </summary>
/// <remarks>
/// <c>C_BaseEntity::ShadowCastType</c> and <c>C_TFPlayer::ShadowCastType</c> cast none under <c>EF_NODRAW | EF_NOSHADOW</c>
/// (`c_baseentity.cpp:1504`, `c_tf_player.cpp:9264`); <c>ShouldReceiveProjectedTextures</c> refuses a shadow's receiver
/// under <c>EF_NORECEIVESHADOW</c> (`c_baseentity.cpp:1583`). Both are bits of the same flag word as <c>EF_NODRAW</c>, so
/// each is tested by mask with its neighbours set and clear.
/// </remarks>
public sealed class ShadowEffectsWiringTests
{
    private const string BaseEntity = "DT_BaseEntity";
    private const string ViewModel = "DT_BaseViewModel";

    private static EntityState WithEffects(string table, int effects)
    {
        EntityState entity = new(1, 0, 0, "CBaseAnimating");
        entity.Set($"{table}.m_fEffects", PropertyValue.FromInt(effects));
        return entity;
    }

    [Test]
    public void CastsNoShadow_TheNoShadowBitAmongOthers_IsTrueAndItsNeighboursAloneAreNot()
    {
        WithEffects(BaseEntity, 0x010 | 0x001).CastsNoShadow.ShouldBeTrue();
        WithEffects(BaseEntity, 0x020 | 0x040 | 0x008).CastsNoShadow.ShouldBeFalse();
        new EntityState(1, 0, 0, "CBaseAnimating").CastsNoShadow.ShouldBeFalse("no effects sent");
    }

    [Test]
    public void ReceivesNoShadow_TheNoReceiveShadowBitAmongOthers_IsTrueAndItsNeighboursAloneAreNot()
    {
        WithEffects(BaseEntity, 0x040 | 0x001).ReceivesNoShadow.ShouldBeTrue();
        WithEffects(BaseEntity, 0x010 | 0x020 | 0x080).ReceivesNoShadow.ShouldBeFalse();
        new EntityState(1, 0, 0, "CBaseAnimating").ReceivesNoShadow.ShouldBeFalse("no effects sent");
    }

    /// <remarks>A viewmodel declares its own <c>m_fEffects</c> (`baseviewmodel_shared.cpp:565`), as <c>IsDrawn</c> reads it.</remarks>
    [Test]
    public void CastsNoShadow_OnAViewmodel_IsReadFromItsOwnTable() =>
        WithEffects(ViewModel, 0x010).CastsNoShadow.ShouldBeTrue();

    /// <remarks>The rebuild between keyframes, where a discrete field gets dropped (`RenderColorWiringTests`' two paths).</remarks>
    [Test]
    public void PropsAt_AtTheLaterKeyframe_CarriesBothShadowEffectsThroughTheRebuild()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            (Tick: 0, Sequence: 0, Parity: 0),
            (Tick: 660, Sequence: 0, Parity: 0)));

        ScenePose pose = SyntheticProp.PoseAt(timeline, 660);

        (pose.NoShadow, pose.NoReceiveShadow).ShouldBe((true, true), "the fixture sends EF_NOSHADOW | EF_NORECEIVESHADOW");
    }

    [Test]
    public void PropsAt_BeforeTheLaterKeyframeArrives_CarriesBothShadowEffectsFromTheEarlierPose()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            (Tick: 0, Sequence: 0, Parity: 0),
            (Tick: 660, Sequence: 1, Parity: 1)));

        ScenePose pose = SyntheticProp.PoseAt(timeline, 330);

        (pose.NoShadow, pose.NoReceiveShadow).ShouldBe((true, true));
    }
}
