using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// TF2's item system, which decides what a weapon or cosmetic actually looks like.
/// </summary>
/// <remarks>
/// **Ninth batch, and it exists because an assumption turned out to be false.** TF2's game code is
/// closed, and this project has repeatedly recorded that its item behaviour "cannot be cited the way
/// a shader parameter can" — the material-override entry in the entity batch says exactly that. But
/// <c>src/game/shared/econ/</c> is in the published SDK: the item view, the schema, the attribute
/// system and the style system are all there.
///
/// So an entire area was treated as unknowable when it is documented. **That is the more useful half
/// of this batch**, and the scope of the error is larger than the econ headers: `source-sdk-2013`
/// carries **1,318 files** under <c>game/shared/tf</c>, <c>game/client/tf</c> and
/// <c>game/server/tf</c> — all 125 HUD sources, the full player-condition enumeration, the übercharge
/// material names. **TF2's game code is published.** The belief that it is closed appeared in
/// <c>docs/CONFORMANCE.md</c>, in the client-system batch and in the entity batch, and was never
/// checked in any of them.
///
/// **What the item system does, in one sentence:** a networked entity carries an item definition
/// index and a list of attributes, and everything visible about it — which model, which textures,
/// which particles, which team variant — is looked up from those rather than sent. A parser that
/// reads the model index and stops has read the least specific thing about the item.
///
/// **A measurement note worth keeping.** The check that established this area was unimplemented
/// first reported 405 matches for "Econ" across the managed tree, which would have been a wall of
/// existing code. They were substring hits inside ordinary words — "second" contains it. The
/// specific mechanisms below all measure zero. A pattern that matches inside words gives a confident
/// wrong answer in whichever direction it happens to fall, which is the same defect as the
/// level-name filter recorded in <c>24-reference-capture.md</c>.
/// </remarks>
public sealed class UnimplementedItemConformanceTests
{
    /// <summary>Where the item schema lives.</summary>
    private const string Schema = "src/game/shared/econ/econ_item_schema.h";

    /// <summary>Where the networked item view lives.</summary>
    private const string View = "src/game/shared/econ/econ_item_view.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void AnItemsAppearanceIsAnAttributeListRatherThanAModelIndex()
    {
        // econ_item_view.h:157,160 — CNetworkVar( attrib_definition_index_t,
        // m_iAttributeDefinitionIndex ) and CNetworkVar( float, m_flValue ). An attribute is a pair:
        // which attribute, and one float.
        //
        // **That pair is the entire extension mechanism**, and it is why the item system cannot be
        // approximated by a lookup table of models. Paint, unusual effects, killstreak sheens, stat
        // clocks and every balance change are all attributes, distinguished only by definition index
        // and interpreted by the schema.
        //
        // For a demo parser the consequence is concrete: the attributes ARE in the stream, on the
        // entity, and this project walks past them. Whatever a player was wearing is recoverable and
        // is currently discarded.
        string view = SourceSdk.Text(View).ShouldNotBeNull();

        view.ShouldContain("CNetworkVar( attrib_definition_index_t, m_iAttributeDefinitionIndex )");
        view.ShouldContain("CNetworkVar( float,\tm_flValue )");

        Assert.Ignore(
            "item attributes are not decoded. They are networked as (definition index, float) pairs " +
            "on the entity (econ_item_view.h:157) and carry paint, unusual effects, killstreaks and " +
            "every balance change — all of it present in the demo and discarded here.");
    }

    [Test]
    public void AnUnusualIsAnAttributeEffectTypeRatherThanAnItem()
    {
        // econ_item_schema.h:694-700 declares attrib_effect_types_t with ATTRIB_EFFECT_UNUSUAL = 0,
        // then STRANGE, NEUTRAL, POSITIVE and NEGATIVE.
        //
        // **Unusual is a category of attribute, not a property of a hat.** The particle above a hat
        // comes from an attribute whose effect type is UNUSUAL; the same hat with and without one is
        // the same item definition with a different attribute list. An implementation that models
        // "unusual" as a variant of an item gets the data model wrong in a way that then makes
        // everything else awkward.
        //
        // Pinned as an enumeration rather than as a single value so a reordering shows up. The order
        // matters: these are stored as an index.
        IReadOnlyDictionary<string, int> effects =
            SourceSdk.Enumerators(Schema, "attrib_effect_types_t");

        effects["ATTRIB_EFFECT_UNUSUAL"].ShouldBe(0);

        // The enumeration ends in a NUM_EFFECT_TYPES sentinel, so the real count is one less than the
        // number of enumerators — and the sentinel's value must equal that count. Checking the two
        // against each other tests the reading; asserting a bare 5 would only restate it.
        effects["NUM_EFFECT_TYPES"].ShouldBe(effects.Count - 1);

        Assert.Ignore(
            "attribute effect types are not read. An unusual is an attribute whose effect type is " +
            "ATTRIB_EFFECT_UNUSUAL (econ_item_schema.h:696), not a variant of the item — modelling " +
            "it the other way makes the whole item model awkward.");
    }

    [Test]
    public void AStyleSelectsADifferentModelForTheSameItemDefinition()
    {
        // CEconStyleInfo (econ_item_schema.h:959) and its
        // GeneratePrecacheModelStringsForStyle at 991: one item definition, several styles, each
        // naming its own models to precache.
        //
        // **This is a direct cause of drawing the wrong thing**, not a cosmetic subtlety. An item
        // with styles has no single correct model, so a viewer that resolves the definition to one
        // model draws the default style for everyone regardless of what they chose — and it does so
        // silently, because a model was found and drawn.
        //
        // Distinct from bodygroups, which are already implemented here: a bodygroup selects parts
        // within one model, a style can substitute the model outright.
        string schema = SourceSdk.Text(Schema).ShouldNotBeNull();

        schema.ShouldContain("class CEconStyleInfo");
        schema.ShouldContain("GeneratePrecacheModelStringsForStyle");

        Assert.Ignore(
            "item styles are not implemented, so every styled item draws its default. A style can " +
            "substitute the MODEL (CEconStyleInfo, econ_item_schema.h:959) — unlike a bodygroup, " +
            "which selects parts within one model and is already handled.");
    }

    [Test]
    public void AnAttachedModelIsChosenPerTeamAndAgainForFestivized()
    {
        // econ_item_schema.h:1384,1387 — GetAttachedModelData( iTeam, iIdx ) and a separate
        // GetAttachedModelDataFestivized( iTeam, iIdx ).
        //
        // **The team parameter is the finding.** An item's attached models are not one list with a
        // colour applied; they are indexed BY TEAM, so RED and BLU can carry genuinely different
        // geometry rather than the same geometry tinted. Anything that implements team appearance as
        // a tint will be right often enough to look correct and wrong wherever Valve authored two
        // models.
        //
        // The festivized variant is a second, parallel lookup rather than an attribute on the first,
        // which is worth knowing before designing this: it is not a flag to apply, it is a different
        // accessor.
        string schema = SourceSdk.Text(Schema).ShouldNotBeNull();

        schema.ShouldContain("GetAttachedModelData( int iTeam, int iIdx )");
        schema.ShouldContain("GetAttachedModelDataFestivized( int iTeam, int iIdx )");

        Assert.Ignore(
            "attached models are not implemented, and they are indexed BY TEAM " +
            "(econ_item_schema.h:1384) — RED and BLU can carry different geometry, so implementing " +
            "team appearance as a tint is right only by coincidence.");
    }

    [Test]
    public void APaintKitIsItsOwnDefinitionSpaceNotAColour()
    {
        // econ_item_schema.h:2649-2651 — PaintKitItemDefinitionMap_t maps a paint kit definition
        // index to an item definition, with GetPaintKitItemDefinition and a collection lookup beside
        // it.
        //
        // **A paint kit is a whole retexture with its own definition index**, not a tint value. It
        // has a definition space of its own, and a mapping in both directions between kits and the
        // items they apply to. "Paint" in TF2 means two unrelated things — the hat paint that IS a
        // colour, and the weapon paint kit that is a texture set — and conflating them produces a
        // renderer that colours a war paint instead of retexturing it.
        string schema = SourceSdk.Text(Schema).ShouldNotBeNull();

        schema.ShouldContain("PaintKitItemDefinitionMap_t");
        schema.ShouldContain("GetPaintKitItemDefinition");

        Assert.Ignore(
            "paint kits are not implemented. A weapon paint kit is a retexture with its own " +
            "definition index (econ_item_schema.h:2649), not a colour — unlike hat paint, which is " +
            "a colour, and the two share a word.");
    }

    [Test]
    public void TheEconHeadersArePublishedAtAllWhichHadBeenAssumedOtherwise()
    {
        // **The control for this whole batch, and the reason it exists.**
        //
        // Every test above cites src/game/shared/econ/. This project had recorded, more than once,
        // that TF2's item behaviour is not citable because the game code is closed. It is not: Valve
        // shipped the game code with the SDK.
        //
        // Asserting the directories are substantial — rather than that one file exists — because the
        // claim being corrected is about coverage, not about a single lookup. A one-file check would
        // pass against a stub, and a stub is exactly what someone would expect to find if they still
        // half-believed the old claim.
        SourceSdk.Files("src/game/shared/econ", "*.h").Count().ShouldBeGreaterThan(20);
        SourceSdk.Files("src/game/client/tf", "tf_hud_*.cpp").Count().ShouldBeGreaterThan(50);

        // The two identifiers docs/CONFORMANCE.md called "a decompile target … not a gap that can be
        // closed from source". They are ordinary defines, one of them expressed in terms of the
        // other, which is the cheapest possible refutation of that claim.
        IReadOnlyDictionary<string, int> classes =
            SourceSdk.Constants("src/game/shared/tf/tf_shareddefs.h");

        classes["TF_CLASS_UNDEFINED"].ShouldBe(0);
    }
}
