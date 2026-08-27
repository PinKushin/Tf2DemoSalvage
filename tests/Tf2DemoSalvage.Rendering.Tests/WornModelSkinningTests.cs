using System;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A bone-merged model is skinned however cheap it is, including when it has one bone.
/// </summary>
/// <remarks>
/// **Baking discards the thing a merged model is positioned by.** It pre-transforms the vertices by
/// one pose and throws the bone indices away, which is right for a model drawn at its own transform
/// and useless for one whose entire position comes from somebody else's skeleton. With no bones to
/// pose, <c>EntityModels.Merge</c> returns early and the model is drawn at the wearer's origin —
/// on a player, their feet; on a viewmodel, the camera lens.
///
/// `PropModels` already knows this and says so in a comment: "A worn model is skinned however cheap
/// it is, and this is not an optimisation choice." The predicate underneath it did not agree:
///
/// <code>
/// bool skin = (mustSkin || wantedFrames > affordable) &amp;&amp; bones.Count > 1;
/// </code>
///
/// **The trailing guard overrides <c>mustSkin</c>**, so a model with exactly one bone could never be
/// skinned however loudly the caller asked. That is not a hypothetical shape: the Original
/// (<c>c_bet_rocketlauncher.mdl</c>) declares exactly one bone, <c>weapon_bone</c>, and the soldier's
/// arms provide it — measured in <c>ViewmodelBoneMergeTests</c>. So it merged perfectly in principle
/// and never merged at all in practice, and was drawn at the camera: the owner's "the original being
/// way too high and taking up all the screen", on every demo since that weapon shipped in June 2012.
///
/// **The stock rocket launcher is the control and is why this hid.** It has four bones, clears the
/// guard, skins, merges, and sits in the hand correctly — so the mechanism looked like it worked.
/// One weapon in the game was wrong, and it was wrong for a reason that had nothing to do with it.
/// </remarks>
public sealed class WornModelSkinningTests
{
    private const string Game = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    /// <summary>The Original, one bone, and the model the owner reported.</summary>
    private const string Original =
        "models/weapons/c_models/c_bet_rocketlauncher/c_bet_rocketlauncher.mdl";

    /// <summary>The stock launcher, four bones, reported as drawing correctly.</summary>
    private const string Stock =
        "models/weapons/c_models/c_rocketlauncher/c_rocketlauncher.mdl";

    private static MapAssets? Load(string model)
    {
        string map = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

        if (!Directory.Exists(Game) || !File.Exists(map))
        {
            Assert.Ignore("the map or the game is not installed");
            return null;
        }

        return MapAssets.Load(
            File.ReadAllBytes(map),
            GameArchives.Open(Game),
            maximumTextureSize: 256,
            entityModels: [model],
            wornModels: [model]);
    }

    [Test]
    public void Load_AOneBoneModelAskedToSkin_IsSkinned()
    {
        if (Load(Original) is not { } assets)
        {
            return;
        }

        assets.EntityModels.ShouldContainKey(Original, "the Original should load from the game");

        PropModels.ModelFrames frames = assets.EntityModels[Original];

        TestContext.Out.WriteLine(
            $"{Path.GetFileName(Original)}: skinned {frames.IsSkinned}, " +
            $"{frames.Geometry.Count} baked frame sets");

        // **The whole experiment.** Asked to skin, one bone, and the answer must be yes — because
        // the caller asking is the caller that will bone-merge it, and a baked model cannot be
        // merged onto anything.
        frames.IsSkinned.ShouldBeTrue(
            "a model flagged as worn must be skinned however few bones it has; baked, it cannot " +
            "bone-merge and is drawn at the wearer's origin, which for a viewmodel is the camera");
    }

    [Test]
    public void Load_AMultiBoneModelAskedToSkin_IsAlsoSkinned()
    {
        if (Load(Stock) is not { } assets)
        {
            return;
        }

        // **The control, and it is what says the fix changed only the case it meant to.** The stock
        // launcher already skinned before this, because four bones cleared the guard. If it ever
        // stops, the change went too wide.
        assets.EntityModels[Stock].IsSkinned.ShouldBeTrue(
            "the stock launcher has four bones and has always skinned; it is the control");
    }
}
