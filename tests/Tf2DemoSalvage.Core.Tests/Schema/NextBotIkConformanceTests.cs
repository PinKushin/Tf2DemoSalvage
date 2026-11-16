using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>Which classes the client gives no IK, from the demo's own send tables (B739).</summary>
/// <remarks>
/// <c>C_NextBotCombatCharacter</c>'s constructor sets <c>m_EntClientFlags |= ENTCLIENTFLAG_DONTUSEIK</c>
/// (<c>C_NextBot.cpp:28</c>, "Left4Dead have surfaces too steep for IK to work properly"), and <c>SetupBones</c> then never
/// allocates the entity's <c>m_pIk</c> (<c>c_baseanimating.cpp:2950</c>). The flag is client-side, so it never travels; the
/// class is <c>IMPLEMENT_SERVERCLASS_ST( NextBotCombatCharacter, DT_NextBot )</c> (<c>NextBot.cpp:72</c>), and TF2's
/// bosses, skeletons and tank inherit it. In TF2 it is the only class that sets the flag.
/// </remarks>
public sealed class NextBotIkConformanceTests
{
    [Test]
    public void UsesNoIk_AHalloweenBossDescendingFromTheNextBot_IsRecognised()
    {
        DemoSchema schema = Schema(
            Table("DT_HeadlessHatman", Inherits(SchemaClasses.NextBotTable)),
            Table(SchemaClasses.NextBotTable, Inherits("DT_BaseCombatCharacter")),
            Table("DT_BaseCombatCharacter", Inherits("DT_BaseAnimating")),
            Table("DT_BaseAnimating"));

        SchemaClasses.UsesNoIk(schema, "DT_HeadlessHatman").ShouldBeTrue();
    }

    [Test]
    public void UsesNoIk_TheTankTwoLevelsDown_IsRecognised()
    {
        // `C_TFTankBoss : C_TFBaseBoss : C_NextBotCombatCharacter`.
        DemoSchema schema = Schema(
            Table("DT_TFTankBoss", Inherits("DT_TFBaseBoss")),
            Table("DT_TFBaseBoss", Inherits(SchemaClasses.NextBotTable)),
            Table(SchemaClasses.NextBotTable));

        SchemaClasses.UsesNoIk(schema, "DT_TFTankBoss").ShouldBeTrue();
    }

    [Test]
    public void UsesNoIk_APlayerOrAProp_IsNot()
    {
        // The control: an animating entity that is not a NextBot keeps its IK.
        DemoSchema schema = Schema(
            Table("DT_TFPlayer", Inherits("DT_BaseCombatCharacter")),
            Table("DT_BaseCombatCharacter", Inherits("DT_BaseAnimating")),
            Table("DT_DynamicProp", Inherits("DT_BaseAnimating")),
            Table("DT_BaseAnimating"),
            Table(SchemaClasses.NextBotTable, Inherits("DT_BaseCombatCharacter")));

        SchemaClasses.UsesNoIk(schema, "DT_TFPlayer").ShouldBeFalse();
        SchemaClasses.UsesNoIk(schema, "DT_DynamicProp").ShouldBeFalse();
    }

    [Test]
    public void UsesNoIk_AClassThatEmbedsAMemberTableReachingTheNextBot_IsNot()
    {
        // The flag is the CLASS's: only its `baseclass` chain counts, not a member data table (B739 review).
        DemoSchema schema = Schema(
            Table("DT_Holder", Inherits("DT_BaseAnimating"), new SendProperty(
                SendPropType.DataTable, "m_Boss", 0, "DT_HeadlessHatman", 0f, 0f, 0, 0)),
            Table("DT_HeadlessHatman", Inherits(SchemaClasses.NextBotTable)),
            Table(SchemaClasses.NextBotTable),
            Table("DT_BaseAnimating"));

        SchemaClasses.UsesNoIk(schema, "DT_Holder").ShouldBeFalse();
    }

    [Test]
    public void UsesNoIk_RobotDestructionsRobot_KeepsItsIkAsItsClientClassIsNoNextBot()
    {
        // A NextBot on the server, a `CBaseCombatCharacter` on the client (`tf_robot_destruction_robot.h:267-271`): its
        // client never sets the flag, though its table reaches `DT_NextBot`. Found on a live demo's tables.
        DemoSchema schema = Schema(
            Table(SchemaClasses.RobotDestructionRobotTable, Inherits(SchemaClasses.NextBotTable)),
            Table(SchemaClasses.NextBotTable));

        SchemaClasses.UsesNoIk(schema, SchemaClasses.RobotDestructionRobotTable).ShouldBeFalse();
    }

    private static DemoSchema Schema(params SendTable[] tables) => new(tables, []);

    private static SendTable Table(string name, params SendProperty[] properties) =>
        new(name, NeedsDecoder: false, properties);

    private static SendProperty Inherits(string table) =>
        new(SendPropType.DataTable, "baseclass", 0, table, 0f, 0f, 0, 0);
}
