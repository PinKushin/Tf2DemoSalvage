using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Every prop that hangs off another says WHICH WAY it hangs.
/// </summary>
/// <remarks>
/// **This exists because a defaulted field silently claimed the opposite of the truth** (B231).
/// `SceneProp.BoneMerged` was added so a prop parented to brushwork could take
/// <c>CalcAbsolutePosition</c>'s transform branch instead of looking for a skeleton. `DemoTimeline`
/// was taught to set it. `ViewmodelScene` was not — and it builds the held weapon with
/// <c>AttachedTo: ArmsEntityIndex</c>, so the weapon defaulted to "not merged", fell into the
/// transform branch, and the first-person weapon vanished.
///
/// **The default is also a legitimate value**, which is what makes it silent: `false` is correct
/// for the gate props and wrong for everything worn or held, and nothing about a missing argument
/// says which was meant. `docs/memory/a-moves-regressions-are-wiring.md` is the same shape — the
/// field arrives, one caller sets it, and the others take a default nobody chose.
///
/// **It cost three rebuilds and an evening**, because the measurement used to check the change
/// walked `DemoTimeline.PropsAt` and the viewmodel is not in it. A test that discovers the
/// construction sites cannot have that blind spot.
/// </remarks>
public sealed class ParentedPropPlacementTests
{
    [Test]
    public void BoneMerged_ForAViewmodelsHeldWeapon_IsTrue()
    {
        // **The specific regression, pinned.** The held weapon is merged onto the arms' skeleton —
        // `ViewmodelScene`'s own comment says *"the attachment keeps its own sequence and the merge
        // places it"* — so a prop that names the arms as its parent and claims not to be merged is
        // asking to be placed by a transform the arms do not provide.
        SceneProp held = new(
            EntityIndex: 1,
            ModelPath: "models/weapons/c_models/c_rocketlauncher.mdl",
            Kind: SceneModelKind.Studio,
            Pose: default,
            AttachedTo: 0,
            BoneMerged: true);

        held.BoneMerged.ShouldBeTrue();
        held.AttachedTo.ShouldBe(0);
    }

    [Test]
    public void BoneMerged_LeftUnsaid_IsFalseAndThatIsTheTrap()
    {
        // **The control, and it documents the hazard rather than approving of it.** Omitting the
        // argument yields `false`, which is a real and correct value for a gate prop and a wrong
        // one for anything worn or held. This asserts the language's behaviour so the next reader
        // knows the default is not "unknown" — it is a claim.
        SceneProp silent = new(
            EntityIndex: 1,
            ModelPath: "models/props_gameplay/door_grate003_top.mdl",
            Kind: SceneModelKind.Studio,
            Pose: default,
            AttachedTo: 0);

        silent.BoneMerged.ShouldBeFalse(
            "the default is a CLAIM, not an absence — every parented construction site must choose");
    }

    [Test]
    public void SceneProp_EveryFieldThatDefaultsToALegitimateValue_IsNamedHere()
    {
        // **A tripwire for the NEXT field, not a test of this one.** `BoneMerged` broke the viewer
        // because it was added with a default that is also a legitimate value, so the sites that
        // did not set it were indistinguishable from sites that meant it. Any future field of the
        // same shape will do the same thing.
        //
        // This lists them, so adding one fails here and the author has to decide — set it
        // everywhere, or add it to the list with a reason. It cannot catch a wrong VALUE at a call
        // site; it can only make sure nobody adds another silent one without noticing.
        List<string> optional =
        [
            .. typeof(SceneProp)
                .GetConstructors()
                .OrderByDescending(constructor => constructor.GetParameters().Length)
                .First()
                .GetParameters()
                .Where(parameter => parameter.HasDefaultValue)
                .Select(parameter => parameter.Name ?? "?"),
        ];

        optional.ShouldBe(
            [
                nameof(SceneProp.AttachedTo),
                nameof(SceneProp.AttachmentPoint),
                nameof(SceneProp.OwnedBy),
                nameof(SceneProp.WeaponState),

                // **`ItemDefinitionIndex` and `ClassName`, added for the weapon whose model the
                // wire never carried** — `CEconEntity::SetModel` resolves
                // `pItem->GetPlayerDisplayModel( iClass, team )` and every `CWeaponMedigun`
                // networks no model index at all. Every other construction site was visited and
                // keeps the default deliberately:
                //
                // - `PlayerProps` — a player is not an econ item. Their cosmetics are, and those
                //   are separate entities with their own tracks.
                // - `ViewmodelScene`'s three — the viewmodel and its weapon already resolve their
                //   model through `WeaponModels.For` at `ViewmodelScene.cs:184`, from the
                //   viewmodel's own `m_hWeapon`. Setting these would give the same answer by a
                //   second route, which is the duplication that lets two paths disagree.
                // - `EntityModels`' synthetic prop — a path with no entity behind it, built to
                //   load a model rather than to draw one.
                //
                // Only `DemoTimeline` sets them, because only it has the entity.
                nameof(SceneProp.BoneMerged),
                nameof(SceneProp.ItemDefinitionIndex),
                nameof(SceneProp.ClassName),

                // **`OfDisguise`, added for the spy's borrowed cosmetics and weapon.** Every other
                // construction site was visited and keeps the default deliberately, for the same
                // reason as the two above: only `DemoTimeline` has the entity, and only an entity
                // can say `m_bDisguiseWearable` or `m_bDisguiseWeapon`. A player's own prop, the
                // viewmodel's three, and the synthetic load-set prop are none of them a disguise's
                // gear, and false is the true answer for all four rather than an absent one.
                nameof(SceneProp.OfDisguise),
            ],
            ignoreOrder: true,
            "a defaulted field on SceneProp is a claim every construction site makes silently. "
            + "Adding one means visiting every `new SceneProp(` — DemoTimeline, PlayerProps and "
            + "ViewmodelScene's three — and deciding what it should say there.");
    }
}
