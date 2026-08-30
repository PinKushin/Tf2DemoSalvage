using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Which classes bone-merge themselves — <c>CEconWearable::Spawn</c>, derived from the schema.
/// </summary>
/// <remarks>
/// **The flag is never on the wire and that is not a gap in the demo.**
/// <c>CEconWearable::Spawn</c> (<c>game/shared/econ/econ_wearable.cpp:112</c>):
///
/// <code>
///   BaseClass::Spawn();
///
///   AddEffects( EF_BONEMERGE );
///   AddEffects( EF_BONEMERGE_FASTCULL );
///
///   #if !defined( CLIENT_DLL )          // &lt;- the guard begins AFTER both AddEffects calls
///       SetCollisionGroup( COLLISION_GROUP_WEAPON );
///       SetBlocksLOS( false );
///   #endif
/// </code>
///
/// `Spawn` runs on the CLIENT for every wearable the client creates — its own player's and every
/// remote player's — so every client already knows, and the server has no reason to send it.
/// Measured on a real match: **26 of 26 `CTFWearable` entities carry no <c>m_fEffects</c> at all**,
/// while weapons such as `CTFRocketLauncher` and `CWeaponMedigun` do carry `EF_BONEMERGE` on the
/// wire.
///
/// **This viewer is the only reader that never runs `Spawn`**, so it is the only one that has to
/// derive the answer — and the thing `Spawn` keys on is the CLASS, which `dem_datatables` carries.
/// A wearable's properties are declared by `DT_TFWearable`, which descends from `DT_WearableItem`,
/// `CEconWearable`'s own table (<c>econ_wearable.cpp:31</c>).
///
/// **Derived from the table chain rather than a list of class names**, because a name list is a
/// guess about a hierarchy. Measured: `CTFPowerupBottle` is parented, carries no flag, and is a
/// `CEconWearable` descendant — a hardcoded check for `CTFWearable` would have missed it and put
/// three bottles on the floor.
///
/// **Why it matters:** treating every parented entity as bone-merged is right for wearables and
/// wrong for a `prop_dynamic` hung on a `func_door`; treating none of them as bone-merged is the
/// reverse, and it put every hat and weapon in the game somewhere it should not be. The class is
/// what tells the two apart, exactly as it does in the engine (B231).
/// </remarks>
public sealed class WearableClassConformanceTests
{
    /// <summary><c>CEconWearable</c>'s network table — <c>econ_wearable.cpp:31</c>.</summary>
    private const string WearableTable = "DT_WearableItem";

    [Test]
    public void BoneMergedByClass_AWearablesOwnTable_IsRecognised()
    {
        // The simplest case: the class's table IS the wearable table.
        DemoSchema schema = Schema(
            Table(WearableTable),
            Table("DT_BaseEntity"));

        SchemaClasses.BoneMergesItself(schema, "DT_WearableItem").ShouldBeTrue();
    }

    [Test]
    public void BoneMergedByClass_ATableDescendingFromTheWearable_IsRecognised()
    {
        // **The real shape.** TF2's wearables are `DT_TFWearable`, which embeds `DT_WearableItem`
        // as a DataTable property — that is how the send-table format expresses inheritance. A
        // check on the class's own table name alone answers false here and loses every wearable in
        // the game.
        DemoSchema schema = Schema(
            Table("DT_TFWearable", Inherits(WearableTable)),
            Table(WearableTable),
            Table("DT_BaseEntity"));

        SchemaClasses.BoneMergesItself(schema, "DT_TFWearable").ShouldBeTrue();
    }

    [Test]
    public void BoneMergedByClass_ADescendantSeveralLevelsDown_IsRecognised()
    {
        // `CTFPowerupBottle` is measured as parented, unflagged, and a `CEconWearable` descendant,
        // so the walk has to be transitive rather than one level deep.
        DemoSchema schema = Schema(
            Table("DT_TFPowerupBottle", Inherits("DT_TFWearable")),
            Table("DT_TFWearable", Inherits(WearableTable)),
            Table(WearableTable));

        SchemaClasses.BoneMergesItself(schema, "DT_TFPowerupBottle").ShouldBeTrue();
    }

    [Test]
    public void BoneMergedByClass_AParentedPropThatIsNotAWearable_IsNot()
    {
        // **The control, and it is the whole reason this exists.** `CDynamicProp` is measured as
        // parented and unflagged too — seven of them on `cp_fulgur`, which are the gate grates —
        // and it must fall on the OTHER side. A predicate that answered true for anything parented
        // is the bug this replaces.
        DemoSchema schema = Schema(
            Table("DT_DynamicProp", Inherits("DT_BaseAnimating")),
            Table("DT_BaseAnimating", Inherits("DT_BaseEntity")),
            Table("DT_BaseEntity"),
            Table(WearableTable));

        SchemaClasses.BoneMergesItself(schema, "DT_DynamicProp").ShouldBeFalse();
    }

    [Test]
    public void BoneMergedByClass_ATableTheSchemaDoesNotHave_IsNotAndDoesNotThrow()
    {
        // A demo is untrusted input (D32) and its tables may name one the schema never defines.
        // Answering false is the safe reading — an entity we cannot classify is placed by its own
        // transform, which is where an unparented entity goes anyway.
        DemoSchema schema = Schema(Table("DT_BaseEntity"));

        SchemaClasses.BoneMergesItself(schema, "DT_Missing").ShouldBeFalse();
    }

    [Test]
    public void BoneMergedByClass_ATableThatRefersToItself_TerminatesRatherThanLooping()
    {
        // **A malformed schema must not hang the load.** Send tables come out of a downloaded demo,
        // so a cycle is input rather than an impossibility, and a naive recursive walk over one
        // never returns.
        DemoSchema schema = Schema(
            Table("DT_Loop", Inherits("DT_Loop")),
            Table(WearableTable));

        Should.CompleteIn(
            () => SchemaClasses.BoneMergesItself(schema, "DT_Loop").ShouldBeFalse(),
            System.TimeSpan.FromSeconds(5));
    }

    /// <summary>A schema of the given tables and no server classes.</summary>
    private static DemoSchema Schema(params SendTable[] tables) => new(tables, []);

    /// <summary>A table with the given properties.</summary>
    private static SendTable Table(string name, params SendProperty[] properties) =>
        new(name, NeedsDecoder: false, properties);

    /// <summary>How the send-table format expresses inheritance: an embedded DataTable.</summary>
    private static SendProperty Inherits(string table) =>
        new(SendPropType.DataTable, "baseclass", 0, table, 0f, 0f, 0, 0);
}
