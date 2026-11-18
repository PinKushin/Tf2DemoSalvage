using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`CTFPlayerShared`'s disguise getters, which every client reader goes through (B767).</summary>
/// <remarks>
/// <code>
/// // tf_player_shared.h:373, :375
/// int GetDisguiseClass() const { return InCond( TF_COND_DISGUISED_AS_DISPENSER ) ? (int)TF_CLASS_ENGINEER : m_nDisguiseClass; }
/// int GetDisguiseMask()  const { return InCond( TF_COND_DISGUISED_AS_DISPENSER ) ? (int)TF_CLASS_ENGINEER : m_nMaskClass; }
/// // tf_player_shared.cpp:8292-8295
/// return InCond( TF_COND_DISGUISED_AS_DISPENSER ) ? ( GetTeamNumber() == TF_TEAM_RED ? TF_TEAM_BLUE : TF_TEAM_RED ) : m_nDisguiseTeam;
/// </code>
/// A spy whose fields say demoman, his own team and a medic mask. Out of the dispenser condition is the control.
/// </remarks>
public sealed class DisguiseGetterConformanceTests
{
    private const int Demoman = 4;
    private const int Medic = 5;
    private const int Engineer = 9;

    [TestCase(SceneTeams.Blu, false, Demoman, SceneTeams.Blu, Medic, TestName = "DisguiseGetters_OutOfTheDispenserCondition_AreTheFields")]
    [TestCase(SceneTeams.Blu, true, Engineer, SceneTeams.Red, Engineer, TestName = "DisguiseGetters_ABluDispenser_IsARedEngineer")]
    [TestCase(SceneTeams.Red, true, Engineer, SceneTeams.Blu, Engineer, TestName = "DisguiseGetters_ARedDispenser_IsABluEngineer")]
    public void DisguiseGetters_ASpy_AnswerAsTheEngineDoes(
        int team, bool dispenser, int disguiseClass, int disguiseTeam, int mask)
    {
        EntityState spy = new(1, 0, 0, "CTFPlayer");

        spy.Set("DT_BaseEntity.m_iTeamNum", PropertyValue.FromInt(team));
        spy.Set("DT_TFPlayerShared.m_nPlayerCond", PropertyValue.FromInt(1 << PlayerConditions.Disguised));
        spy.Set(
            "DT_TFPlayerShared.m_nPlayerCondEx",
            PropertyValue.FromInt(dispenser ? 1 << (PlayerConditions.DisguisedAsDispenser - 32) : 0));
        spy.Set("DT_TFPlayerShared.m_nDisguiseClass", PropertyValue.FromInt(Demoman));
        spy.Set("DT_TFPlayerShared.m_nDisguiseTeam", PropertyValue.FromInt(team));
        spy.Set("DT_TFPlayerShared.m_nMaskClass", PropertyValue.FromInt(Medic));

        (spy.DisguiseClass(), spy.DisguiseTeam(), spy.DisguiseMaskClass()).ShouldBe((disguiseClass, disguiseTeam, mask));
    }
}
