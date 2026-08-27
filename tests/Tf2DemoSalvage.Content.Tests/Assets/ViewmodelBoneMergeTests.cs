using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Whether a weapon's first-person model can actually bone-merge onto the arms it hangs from.
/// </summary>
/// <remarks>
/// **The attachment has no transform of its own.** TF2 parents the weapon's <c>c_</c> model to the
/// viewmodel with <c>SetLocalOrigin( vec3_origin )</c> and blends it through
/// <c>C_ViewmodelAttachmentModel::StandardBlendingRules</c>, so it takes the arms' bone matrices
/// **by name** — exactly as a hat takes a player's. A bone the arms do not provide has nothing to
/// take, and the failure is not an error: the bone stays at identity, which puts that geometry at
/// the model's origin. On a viewmodel the origin is the camera, so the symptom is a weapon drawn
/// enormous and centred rather than a weapon drawn missing.
///
/// **Written before any fix, because the last one that was not cost a day.** The owner reports the
/// Original ("the quake launcher") drawing far too high and filling the screen, on any demo from
/// when that weapon shipped — Pyromania, June 2012. It uses <c>c_bet_rocketlauncher.mdl</c>, and the
/// question this suite exists to answer is whether that model's bones are ones the soldier's arms
/// can supply. If they are, the cause is elsewhere and this says so; if they are not, the identity
/// fallback explains the size, the position and why only this weapon shows it.
///
/// The stock rocket launcher is the control. It is the same class, the same slot and the same era
/// of model, and the owner reports it drawing correctly — so a difference between the two is a
/// difference that matters, and agreement rules the whole theory out.
/// </remarks>
public sealed class ViewmodelBoneMergeTests
{
    private static string Game => GameInstall.Require();

    private const string SoldierArms = "models/weapons/c_models/c_soldier_arms.mdl";

    /// <summary>The Original, whose viewmodel the owner reports as far too large.</summary>
    private const string Original =
        "models/weapons/c_models/c_bet_rocketlauncher/c_bet_rocketlauncher.mdl";

    /// <summary>The stock rocket launcher, which the owner reports as correct.</summary>
    private const string Stock =
        "models/weapons/c_models/c_rocketlauncher/c_rocketlauncher.mdl";

    private static IReadOnlyList<StudioBone>? Bones(string path)
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("Team Fortress 2 is not installed");
            return null;
        }

        byte[]? file = GameArchives.Open(Game).Read(path);

        if (file is null)
        {
            Assert.Ignore($"{path} is not in this install");
            return null;
        }

        return StudioBones.Read(file);
    }

    /// <summary>Reports which of a weapon's bones the arms can supply, and which they cannot.</summary>
    private static void Report(string weapon, IReadOnlyList<StudioBone> arms)
    {
        if (Bones(weapon) is not { } bones)
        {
            return;
        }

        HashSet<string> provided = new(arms.Select(bone => bone.Name), StringComparer.OrdinalIgnoreCase);

        List<string> unmatched =
        [
            .. bones.Select(bone => bone.Name).Where(name => !provided.Contains(name))
        ];

        TestContext.Out.WriteLine(
            $"{Path.GetFileName(weapon)}: {bones.Count} bones, " +
            $"{bones.Count - unmatched.Count} merge onto the arms, {unmatched.Count} do not");
        TestContext.Out.WriteLine($"  bones: {string.Join(", ", bones.Select(bone => bone.Name))}");

        if (unmatched.Count > 0)
        {
            TestContext.Out.WriteLine($"  UNMATCHED: {string.Join(", ", unmatched)}");
        }
    }

    [Test]
    public void Bones_TheOriginalAgainstTheStockLauncher_AreReported()
    {
        if (Bones(SoldierArms) is not { } arms)
        {
            return;
        }

        TestContext.Out.WriteLine(
            $"{Path.GetFileName(SoldierArms)}: {arms.Count} bones — " +
            string.Join(", ", arms.Select(bone => bone.Name)));

        Report(Stock, arms);
        Report(Original, arms);

        // Reported rather than asserted on purpose: this is the first look, and a bound invented
        // before the numbers are known is the mistake that has been made repeatedly today. The
        // assertion goes in once the answer is on the screen — and it goes in as an exact count.
        Assert.Pass("reported; see output");
    }
}
