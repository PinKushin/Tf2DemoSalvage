using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// That a gold or iced corpse's material override survives every hop to the drawn instance (B325).
/// </summary>
/// <remarks>
/// **The three hops between a decoded flag and a painted corpse, none of which a component test
/// watches.** `RagdollAppearanceConformanceTests` proves `MaterialFor` picks the right VMT and says
/// nothing about whether anything asks it; `GoldRagdollSpecimenTests` proves the flags arrive on
/// `SceneRagdoll` and says nothing about what is done with them. Between them is the join this
/// project has shipped three no-ops through — decoded, retained, unit-tested, and read by nothing
/// (`docs/memory/output-level-assertion-or-it-is-not-done.md`).
///
/// The failure would be invisible: a `MaterialOverride` nobody assigns is null, which is the value
/// every ordinary corpse legitimately has, so nothing errors and nothing looks wrong except that
/// Saxxy kills stopped turning people gold.
///
/// **The wearable is in the fixture on purpose, and it is the half most likely to be dropped.** The
/// engine repaints a corpse's cosmetics in a SECOND pass, looping the client entity list for
/// followers (`c_tf_player.cpp:982-993`), because a material override is per renderable rather than
/// per entity. An implementation that painted the body alone would leave a golden corpse in a
/// normal-coloured hat, which is the exact symptom that loop exists to prevent.
/// </remarks>
public sealed class GoldRagdollMaterialWiringTests
{
    [Test]
    public void Instances_ForAGoldCorpse_CarryTheGoldMaterialToTheDrawnBody()
    {
        ModelInstance body = Drawn(Corpse(gold: true, ice: false))[MedicModel];

        body.MaterialOverride.ShouldBe(RagdollAppearance.GoldMaterial);
    }

    [Test]
    public void Instances_ForAGoldCorpse_CarryTheGoldMaterialToEachItemItWears()
    {
        ModelInstance hat = Drawn(Corpse(gold: true, ice: false))[HatModel];

        hat.MaterialOverride.ShouldBe(RagdollAppearance.GoldMaterial);
    }

    /// <remarks>
    /// **Ice rather than a second gold case, because the two are not symmetric in the engine.** Gold
    /// is nested inside `if ( m_bFixedConstraints )` and ice is not, and ice is assigned second, so
    /// it wins outright. A single-material implementation would pass the gold cases above.
    /// </remarks>
    [Test]
    public void Instances_ForAnIcedCorpse_CarryTheIceMaterialToTheBodyAndTheHat()
    {
        Dictionary<string, ModelInstance> drawn = Drawn(Corpse(gold: false, ice: true));

        drawn[MedicModel].MaterialOverride.ShouldBe(RagdollAppearance.IceMaterial);
        drawn[HatModel].MaterialOverride.ShouldBe(RagdollAppearance.IceMaterial);
    }

    /// <remarks>
    /// **The control, and without it the three above are satisfied by a constant.** Every corpse in
    /// this project's corpus is this one — 0 of 566 measured carry either flag — so an override
    /// applied unconditionally would paint the whole match gold and pass every assertion above.
    /// </remarks>
    [Test]
    public void Instances_ForAnOrdinaryCorpse_CarryNoOverrideOnTheBodyOrTheHat()
    {
        Dictionary<string, ModelInstance> drawn = Drawn(Corpse(gold: false, ice: false));

        drawn[MedicModel].MaterialOverride.ShouldBeNull();
        drawn[HatModel].MaterialOverride.ShouldBeNull();
    }

    /// <summary>Everything a corpse draws at its own tick, keyed by model path.</summary>
    /// <remarks>
    /// **The production route, not a hand-built `SceneProp`.** `RagdollProps.Fill` is what the
    /// timeline calls and `EntityModelSet.Instances` is what the renderer consumes; building a prop
    /// here would test that `ModelInstance` can hold a string, which nobody doubts.
    /// </remarks>
    private static Dictionary<string, ModelInstance> Drawn(SceneRagdoll corpse)
    {
        List<SceneProp> props = [];

        RagdollProps.Fill([corpse], tick: 150d, Classes, props).ShouldBe(
            2, "the corpse and the one item it wears");

        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        models.Add(props, Frames);
        models.Instances(props, instances);

        Dictionary<string, ModelInstance> byModel = [];

        foreach (ModelInstance instance in instances)
        {
            byModel[instance.ModelPath] = instance;
        }

        return byModel;
    }

    /// <summary>A corpse wearing one hat, with the two override flags under test.</summary>
    private static SceneRagdoll Corpse(bool gold, bool ice) =>
        new(EntityIndex: 40,
            Serial: 1,
            PlayerClass: 5,
            Team: SceneTeams.Red,
            X: -5446f,
            Y: 4055f,
            Z: 21f,
            Gib: false,
            Burning: false,
            FeignDeath: false,
            WasDisguised: false,
            FirstTick: 100,
            LastTick: 200,
            Yaw: 137f,
            Worn: [new SceneWornItem(HatModel, ItemDefinitionIndex: null)],
            Gold: gold,
            Ice: ice);

    /// <summary>Geometry enough to be drawn — one triangle, no sequences, no bones.</summary>
    private static PropModels.ModelFrames Frames(string path) =>
        new(
            [[
                new PropVertex(0f, 0f, 0f, 0f, 0f, 0),
                new PropVertex(1f, 0f, 0f, 1f, 0f, 0),
                new PropVertex(1f, 1f, 0f, 1f, 1f, 0),
            ]],
            new Dictionary<int, (int, int, float)>(),
            [0, 1],
            [false, false]);

    private static string? Classes(int playerClass) =>
        playerClass == 5 ? MedicModel : null;

    private const string MedicModel = "models/player/medic.mdl";

    private const string HatModel = "models/player/items/medic/medic_wilhelm.mdl";
}
