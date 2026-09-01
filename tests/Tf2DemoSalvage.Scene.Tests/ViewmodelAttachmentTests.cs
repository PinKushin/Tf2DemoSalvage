using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Which of an item's attachments each VIEW shows — the display-flag mask (B252).
/// </summary>
/// <remarks>
/// **`DrawEconEntityAttachedModels` is called twice with different masks, and the filter is the
/// point:** `kAttachedModelDisplayFlag_WorldModel` from `CEconEntity::DrawModel`
/// (`econ_entity.cpp:1960`) and `kAttachedModelDisplayFlag_ViewModel` from the viewmodel path
/// (`econ_entity.cpp:886`, `tf_viewmodel.cpp:304`), each keeping only entries whose
/// `m_iModelDisplayFlags` intersect its mask:
///
/// <code>
///   if ( attachedModel.m_pModel &amp;&amp; (attachedModel.m_iModelDisplayFlags &amp; iMatchDisplayFlags) )
/// </code>
///
/// **The filter matters in both directions the moment both paths exist.** Until the first-person
/// props carried an item, every attachment was world-drawn and an unfiltered list was
/// indistinguishable from a filtered one — B251 shipped without the mask and nothing could tell.
/// A `model_display_flags 2` entry on a world weapon, or a `1` in first person, is the wrong-view
/// leak this pins.
/// </remarks>
public sealed class ViewmodelAttachmentTests
{
    private const int Item = 700;

    [Test]
    public void AttachmentsFor_UnderTheWorldMask_KeepsWorldAndBothViewEntries()
    {
        // The default-flag entry (MaskAll) shows in both views — nearly every shipped entry omits
        // the key, so a mask that dropped it would empty the world of pilot lights.
        WeaponModels weapons = Weapons();

        weapons.AttachmentsFor(Item, team: null, festivized: false, AttachedModel.WorldModel)
            .ShouldBe([
                "models/weapons/c_models/c_both_views.mdl",
                "models/weapons/c_models/c_world_only.mdl",
            ]);
    }

    [Test]
    public void AttachmentsFor_UnderTheViewmodelMask_KeepsViewmodelAndBothViewEntries()
    {
        WeaponModels weapons = Weapons();

        weapons.AttachmentsFor(Item, team: null, festivized: false, AttachedModel.ViewModel)
            .ShouldBe([
                "models/weapons/c_models/c_both_views.mdl",
                "models/weapons/c_models/c_viewmodel_only.mdl",
            ]);
    }

    private static WeaponModels Weapons() => new(_ => Schema(), new RecordingLogger());

    /// <summary>One item, three attachments — one per flag case.</summary>
    private static byte[] Schema() =>
        Encoding.UTF8.GetBytes(
            $$"""
            "items_game"
            {
                "items"
                {
                    "{{Item}}"
                    {
                        "name" "Flagged"
                        "visuals"
                        {
                            "attached_models"
                            {
                                "0"
                                {
                                    "model" "models/weapons/c_models/c_both_views.mdl"
                                }
                                "1"
                                {
                                    "model" "models/weapons/c_models/c_world_only.mdl"
                                    "model_display_flags" "1"
                                }
                                "2"
                                {
                                    "model" "models/weapons/c_models/c_viewmodel_only.mdl"
                                    "model_display_flags" "2"
                                }
                            }
                        }
                    }
                }
            }
            """);
}
