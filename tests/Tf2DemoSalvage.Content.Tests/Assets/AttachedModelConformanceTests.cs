using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The extra models an item hangs on itself, and the rules that decide which of them draw.
/// </summary>
/// <remarks>
/// **Written before the implementation, from the engine rather than from our data** — the whole
/// mechanism, both halves, because reading one half and reasoning across the gap produced three
/// wrong answers in the session before this one.
///
/// **Build**, `CEconEntity::UpdateAttachmentModels` (`econ_entity.cpp:1078`):
///
/// <code>
///   int iAttachedModels = pItemDef->GetNumAttachedModels( iTeamNumber );
///   for ( int i = 0; i &lt; iAttachedModels; i++ ) {
///       attachedmodel_t *pModel = pItemDef->GetAttachedModelData( iTeamNumber, i );
///       ...
///       m_vecAttachedModels.AddToTail( attachedModelData );
///   }
/// </code>
///
/// followed by a second loop over `GetNumAttachedModelsFestivized( iTeamNumber )`, entered only
/// when `CALL_ATTRIB_HOOK_INT( iFestivized, is_festivized )` comes back non-zero.
///
/// **Draw**, `DrawEconEntityAttachedModels` (`econ_entity.cpp:103`):
///
/// <code>
///   if ( attachedModel.m_pModel &amp;&amp; (attachedModel.m_iModelDisplayFlags &amp; iMatchDisplayFlags) )
/// </code>
///
/// called with `kAttachedModelDisplayFlag_WorldModel` from `CEconEntity::DrawModel`
/// (`econ_entity.cpp:1960`) and `kAttachedModelDisplayFlag_ViewModel` from the viewmodel path
/// (`econ_entity.cpp:886`, `tf_viewmodel.cpp:304`).
///
/// **The attachment is not a separate entity and has no attachment point.** That draw copies the
/// parent's `ClientModelRenderInfo_t` — origin, angles and the bone-to-world array from
/// `DrawModelSetup` — and only swaps `pModel`. So an attachment rides the item's own transform,
/// which is why the Degreaser's pilot light sits where the Degreaser does without the schema
/// naming a bone.
///
/// **What the shipped file contains**, counted rather than assumed: 29 `attached_models` blocks,
/// **310** `attached_models_festive`, and one item with `visuals_red` / `visuals_blu`. The ratio is
/// the trap — treating the festive block like the plain one decorates every ordinary weapon.
/// </remarks>
public sealed class AttachedModelConformanceTests
{
    /// <summary>A schema shaped like the real one, with each case the engine distinguishes.</summary>
    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "base_flamethrower"
                {
                    "model_player" "models/weapons/c_models/c_flamethrower.mdl"
                    "visuals"
                    {
                        "attached_models"
                        {
                            "0"
                            {
                                "model" "models/weapons/c_models/c_inherited_pilot.mdl"
                            }
                        }
                    }
                }
            }
            "items"
            {
                "215"
                {
                    "prefab" "base_flamethrower"
                    "visuals"
                    {
                        "attached_models"
                        {
                            "0"
                            {
                                "model" "models/weapons/c_models/c_degreaser_pilotlight.mdl"
                            }
                        }
                        "attached_models_festive"
                        {
                            "0"
                            {
                                "model" "models/weapons/c_models/c_festivizer.mdl"
                            }
                        }
                    }
                }
                "300"
                {
                    "visuals"
                    {
                        "attached_models"
                        {
                            "0"
                            {
                                "model" "models/weapons/c_models/c_viewmodel_only.mdl"
                                "model_display_flags" "2"
                            }
                            "1"
                            {
                                "model" "models/weapons/c_models/c_world_only.mdl"
                                "model_display_flags" "1"
                            }
                        }
                    }
                }
                "400"
                {
                    "visuals_red"
                    {
                        "attached_models"
                        {
                            "0"
                            {
                                "model" "models/weapons/c_models/c_red_only.mdl"
                            }
                        }
                    }
                    "visuals_blu"
                    {
                        "attached_models"
                        {
                            "0"
                            {
                                "model" "models/weapons/c_models/c_blu_only.mdl"
                            }
                        }
                    }
                }
            }
        }
        """;

    /// <summary><c>TF_TEAM_RED</c>.</summary>
    private const int Red = 2;

    /// <summary><c>TF_TEAM_BLUE</c>.</summary>
    private const int Blu = 3;

    [Test]
    public void DisplayFlags_MatchTheEngineConstants_ForWorldAndViewModel()
    {
        // `kAttachedModelDisplayFlag_WorldModel = 0x01`, `kAttachedModelDisplayFlag_ViewModel =
        // 0x02`, `kAttachedModelDisplayFlag_MaskAll` their union — econ_item_schema.h:881.
        // Asserted by value because the whole draw is a bitwise AND against these, and a
        // transposed pair would put every viewmodel attachment on the world model instead.
        AttachedModel.WorldModel.ShouldBe(1);
        AttachedModel.ViewModel.ShouldBe(2);
        AttachedModel.MaskAll.ShouldBe(3);
    }

    [Test]
    public void AttachedModels_WhenTheSchemaOmitsDisplayFlags_ShowInBothViews()
    {
        // `GetInt( "model_display_flags", kAttachedModelDisplayFlag_MaskAll )` —
        // econ_item_schema.cpp:2503. The DEFAULT is the interesting half: nearly every shipped
        // entry omits the key, so a reader defaulting to zero would hide all 29 of them and the
        // symptom would be an item quietly missing a piece rather than an error.
        Read().AttachedModelsFor(215, Red, festivized: false)
            .First(attached => attached.Model.Contains("degreaser_pilotlight", StringComparison.Ordinal))
            .DisplayFlags.ShouldBe(AttachedModel.MaskAll);
    }

    [Test]
    public void AttachedModels_WithExplicitDisplayFlags_KeepThem()
    {
        // The control for the default above: an entry that DOES say keeps what it said, so the two
        // cases are told apart rather than both landing on 3.
        IReadOnlyList<AttachedModel> found = Read().AttachedModelsFor(300, Red, festivized: false);

        found.Single(attached => attached.Model.Contains("viewmodel_only", StringComparison.Ordinal))
            .DisplayFlags.ShouldBe(AttachedModel.ViewModel);

        found.Single(attached => attached.Model.Contains("world_only", StringComparison.Ordinal))
            .DisplayFlags.ShouldBe(AttachedModel.WorldModel);
    }

    [Test]
    public void AttachedModels_ForAnItemThatIsNotFestivized_LeaveOutTheFestiveBlock()
    {
        // The second loop runs only under `CALL_ATTRIB_HOOK_INT( iFestivized, is_festivized )`
        // (econ_entity.cpp:1109). There are 310 festive blocks in the shipped schema against 29
        // plain ones, so a reader that ignored the gate would hang a festivizer on most of the
        // game's weapons.
        Read().AttachedModelsFor(215, Red, festivized: false)
            .ShouldNotContain(attached => attached.Festive);
    }

    [Test]
    public void AttachedModels_ForAFestivizedItem_IncludeBothBlocks()
    {
        // **The control, and it is what stops the gate becoming "never festive".** The festive
        // entry is ADDED to the plain one rather than replacing it — the engine appends to the
        // same `m_vecAttachedModels` — so a festivized Degreaser keeps its pilot light.
        IReadOnlyList<AttachedModel> found = Read().AttachedModelsFor(215, Red, festivized: true);

        found.ShouldContain(attached => attached.Model.Contains("festivizer", StringComparison.Ordinal));
        found.ShouldContain(
            attached => attached.Model.Contains("degreaser_pilotlight", StringComparison.Ordinal));
    }

    [Test]
    public void AttachedModels_ForAPerTeamBlock_BelongToThatTeamAlone()
    {
        // `GetNumAttachedModels( iTeamNumber )` takes the team, and the schema expresses it as
        // sibling `visuals_red` / `visuals_blu` blocks. Both directions are asserted because a
        // reader that ignored the suffix would return both models to both teams, which shows as a
        // BLU attachment on a RED weapon rather than as a missing one.
        ItemSchema schema = Read();

        schema.AttachedModelsFor(400, Red, festivized: false)
            .Select(attached => attached.Model)
            .ShouldBe(["models/weapons/c_models/c_red_only.mdl"]);

        schema.AttachedModelsFor(400, Blu, festivized: false)
            .Select(attached => attached.Model)
            .ShouldBe(["models/weapons/c_models/c_blu_only.mdl"]);
    }

    [Test]
    public void AttachedModels_AreInheritedFromPrefabsAndAccumulate()
    {
        // **Attachments are a LIST, so inheritance adds rather than shadows** — unlike
        // `model_player`, where the nearest definition wins and the search stops. An item that
        // hangs its own attachment does not thereby discard the one its prefab hangs, and the
        // engine never chooses between them: both are in the item definition's arrays by the time
        // `UpdateAttachmentModels` walks them.
        Read().AttachedModelsFor(215, Red, festivized: false)
            .Select(attached => attached.Model)
            .ShouldContain("models/weapons/c_models/c_inherited_pilot.mdl");
    }

    [Test]
    public void AttachedModels_ForAnItemThatDeclaresNone_AreEmpty()
    {
        // The control for the whole file: an item with no visuals block answers nothing, so a
        // reader that leaked another item's attachments would be caught here rather than by a
        // viewer drawing a pilot light on a bat.
        Read().AttachedModelsFor(999, Red, festivized: false).ShouldBeEmpty();
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));
}
