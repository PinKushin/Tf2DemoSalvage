using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What a first-person arms model actually contains, measured against the shipped files.
/// </summary>
/// <remarks>
/// **This exists because five aimed changes at B160 fixed nothing.** The bug is that a player in
/// first person sees two weapons overlapping — "thats a demo, and thats 2 sticky launchers
/// overlapping each other" — and every theory about it so far was formed by looking at a
/// screenshot and reasoning about what the system must be doing. A picture supports a claim about
/// what it shows; each time it was promoted to a claim about the code, and each time it was wrong.
///
/// The theory this suite was written to test came from a production log line, which is better
/// evidence than a screenshot but still not a measurement: <c>c_soldier_arms.mdl</c> was reported
/// pairing as <c>soldier_hands</c>, <c>soldier_sleeves_red</c> and
/// <c>models/weapons/w_rocketlauncher/w_rocket01</c>. If an arms model really does carry weapon
/// geometry, then drawing the arms and the weapon as two props draws the weapon twice, and that is
/// the whole bug.
///
/// **It reads Valve's files rather than ours**, which is the point: a fixture authored from this
/// project's own understanding could only confirm that understanding. These assertions can fail.
///
/// Skips rather than fails without the game installed, in the house pattern — the claim is about
/// shipped content, so absent content means the question was not asked, not that it was answered.
/// </remarks>
public sealed class ViewmodelArmsContentTests
{
    private static string Game => GameInstall.Require();

    /// <summary>The demoman's first-person arms, which is the model in the reported capture.</summary>
    private const string DemoArms = "models/weapons/c_models/c_demo_arms.mdl";

    /// <summary>The soldier's, which is the one the production log named.</summary>
    private const string SoldierArms = "models/weapons/c_models/c_soldier_arms.mdl";

    private static StudioModelInfo? Arms(string path)
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

        return StudioModel.Read(file);
    }

    /// <summary>Whether a material name is a world weapon's rather than an arm's.</summary>
    /// <remarks>
    /// **<c>w_</c> is Valve's own prefix for the world model of a weapon**, as against <c>c_</c>
    /// for the shared first-person one — <c>models/weapons/w_rocketlauncher/</c> beside
    /// <c>models/weapons/c_models/</c>. So a material under a <c>w_</c> folder inside an ARMS model
    /// is the signature being looked for: the arms carrying a gun.
    /// </remarks>
    private static bool IsWeaponMaterial(string name) =>
        name.Replace('\\', '/').Contains("/w_", StringComparison.OrdinalIgnoreCase) ||
        name.Replace('\\', '/').StartsWith("w_", StringComparison.OrdinalIgnoreCase);

    [Test]
    public void Read_TheDemomansArms_ContainNoWeaponMaterial()
    {
        if (Arms(DemoArms) is not { } arms)
        {
            return;
        }

        // Reported whatever the outcome, because a bare pass here says nothing about what WAS
        // found, and the material list is the evidence either way.
        TestContext.Out.WriteLine(
            $"{DemoArms}: {arms.Materials.Count} materials — {string.Join(", ", arms.Materials)}");
        TestContext.Out.WriteLine(
            $"  {arms.BodyParts.Count} body parts: " +
            string.Join(", ", arms.BodyParts.Select(part => $"base {part.Base} x{part.Count}")));

        List<string> weapons = [.. arms.Materials.Where(IsWeaponMaterial)];

        weapons.ShouldBeEmpty(
            "an arms model that carries a weapon material draws the weapon a second time, on top " +
            "of the weapon prop posed beside it");
    }

    /// <summary>The soldier's arms carry a rocket that no body number can hide.</summary>
    /// <remarks>
    /// **Measured, and it is Valve's design rather than a defect.** The file declares ONE body part
    /// offering ONE alternative, and all three of its meshes are shown at body zero:
    ///
    /// <code>
    /// part 0 alt 0 'models/player/soldier/soldier_hands'
    /// part 0 alt 0 'models/player/soldier/soldier_sleeves_red'
    /// part 0 alt 0 'models/weapons/w_rocketlauncher/w_rocket01'
    /// </code>
    ///
    /// So there is no bodygroup that could remove the rocket, and the engine does not remove it:
    /// the loaded round is part of the arms because the reload animation has to hold it, and every
    /// other animation moves it out of the frustum instead of hiding it. **A mesh parked off-screen
    /// by its bones is only off-screen while the bones are right** — at a rest pose it sits in the
    /// middle of the view, which is what the owner reported as "soldier has a weird glitch no idea
    /// what it is".
    ///
    /// **This started as the opposite claim**, asserting no weapon mesh should be shown, on the
    /// strength of a production log line naming the same material. That log prints
    /// <c>model.Meshes</c> unfiltered, so it could not distinguish a mesh that is drawn from an
    /// alternative that is merely present — which is why it was worth measuring rather than acting
    /// on. The answer happened to agree here, and would not have for a model with real bodygroups.
    /// </remarks>
    [Test]
    public void Shows_TheSoldiersArmsAtBodyZero_IncludeTheLoadedRocket()
    {
        if (Arms(SoldierArms) is not { } arms)
        {
            return;
        }

        List<StudioMesh> shown = [.. arms.Meshes.Where(mesh => arms.Shows(mesh, body: 0))];

        TestContext.Out.WriteLine(
            $"{SoldierArms}: {arms.BodyParts.Count} parts " +
            string.Join(", ", arms.BodyParts.Select(part => $"(base {part.Base} x{part.Count})")) +
            $"; {arms.Meshes.Count} meshes, {shown.Count} shown at body 0");

        foreach (StudioMesh mesh in arms.Meshes)
        {
            TestContext.Out.WriteLine(
                $"  part {mesh.BodyPart} alt {mesh.BodyModel} " +
                $"'{arms.Materials[mesh.MaterialIndex]}' " +
                $"{(arms.Shows(mesh, 0) ? "SHOWN" : "hidden")} at body 0");
        }

        arms.BodyParts.Count.ShouldBe(
            1, "a second body part would give the engine a way to hide the rocket, and it has none");
        arms.BodyParts[0].Count.ShouldBe(
            1, "one alternative means m_nBody cannot remove any mesh from this model");

        shown
            .Count(mesh => IsWeaponMaterial(arms.Materials[mesh.MaterialIndex]))
            .ShouldBe(
                1,
                "the loaded rocket is part of the arms and is always drawn, so whether it is " +
                "visible is decided by the animation alone");
    }

    /// <summary>What the demoman's own weapon models contain, since his arms contain no weapon.</summary>
    /// <param name="weapon">A first-person weapon model the demoman can hold.</param>
    /// <remarks>
    /// **The reported bug is his, and the arms explanation does not reach it.**
    /// <c>c_demo_arms.mdl</c> carries no weapon material at all, so "two sticky launchers
    /// overlapping each other" cannot be the arms drawing one of them. That leaves the weapon model
    /// itself: a model containing two copies of the launcher, or one offering alternatives that are
    /// all being drawn, would look identical on screen.
    ///
    /// Reported rather than bounded, because the useful output here is the shape of the file. An
    /// assertion that guessed a mesh count would be this project's recurring mistake of writing
    /// down a number nobody measured.
    /// </remarks>
    [TestCase("models/weapons/c_models/c_grenadelauncher/c_grenadelauncher.mdl")]
    [TestCase("models/weapons/c_models/c_stickybomb_launcher/c_stickybomb_launcher.mdl")]
    public void Shows_ADemomanWeaponAtBodyZero_DrawsEachMeshOnce(string weapon)
    {
        if (Arms(weapon) is not { } model)
        {
            return;
        }

        List<StudioMesh> shown = [.. model.Meshes.Where(mesh => model.Shows(mesh, body: 0))];

        TestContext.Out.WriteLine(
            $"{weapon}: {model.BodyParts.Count} parts " +
            string.Join(", ", model.BodyParts.Select(part => $"(base {part.Base} x{part.Count})")) +
            $"; {model.Meshes.Count} meshes, {shown.Count} shown at body 0");

        foreach (StudioMesh mesh in model.Meshes)
        {
            TestContext.Out.WriteLine(
                $"  part {mesh.BodyPart} alt {mesh.BodyModel} " +
                $"'{model.Materials[mesh.MaterialIndex]}' {mesh.VertexCount}v " +
                $"{(model.Shows(mesh, 0) ? "SHOWN" : "hidden")}");
        }

        // **A material drawn twice at body zero is the signature being hunted.** Two meshes sharing
        // one material across DIFFERENT body parts is legitimate — a part is a separate piece of
        // the object — so this counts repeats within the shown set and reports them, which is the
        // fact wanted rather than a verdict on it.
        List<string> repeated =
        [
            .. shown
                .GroupBy(mesh => model.Materials[mesh.MaterialIndex])
                .Where(group => group.Count() > 1)
                .Select(group => $"{group.Key} x{group.Count()}")
        ];

        TestContext.Out.WriteLine(
            repeated.Count == 0
                ? "  no material is drawn twice at body 0"
                : "  drawn twice at body 0: " + string.Join(", ", repeated));
    }

    /// <summary>
    /// Which meshes an arms model shows at body zero, which is the number the viewmodel passes.
    /// </summary>
    /// <remarks>
    /// **The alternative explanation for the same picture**, and it has to be separated from the
    /// first. If the arms carry no weapon but the model offers several alternatives per part, then
    /// drawing every alternative — which this renderer does whenever a model's body-part table is
    /// missing (<c>Device3D.ReportBodySelection</c> keeps every batch when
    /// <c>instance.BodyParts</c> is empty) — puts two variants of the same object in the same
    /// place. That is also "two overlapping", and no screenshot can tell the two causes apart.
    ///
    /// So this measures the count of alternatives rather than asserting a bound on it: whether a
    /// duplicate is even POSSIBLE from this model is the fact wanted, and it is wanted before any
    /// more code is changed.
    /// </remarks>
    [Test]
    public void Shows_TheDemomansArmsAtBodyZero_KeepsOneMeshPerBodyPart()
    {
        if (Arms(DemoArms) is not { } arms)
        {
            return;
        }

        List<StudioMesh> shown = [.. arms.Meshes.Where(mesh => arms.Shows(mesh, body: 0))];

        TestContext.Out.WriteLine(
            $"{DemoArms}: {arms.Meshes.Count} meshes, {shown.Count} shown at body 0 — " +
            string.Join(
                ", ",
                shown.Select(mesh =>
                    $"part {mesh.BodyPart} alt {mesh.BodyModel} " +
                    $"'{(mesh.MaterialIndex >= 0 && mesh.MaterialIndex < arms.Materials.Count ? arms.Materials[mesh.MaterialIndex] : "?")}'")));

        // **One alternative per part is what m_nBody MEANS**, so this is arithmetic rather than a
        // guess: a part contributes exactly the meshes of the alternative selected for it. Two
        // meshes of the SAME part surviving would be a defect in `Shows`, and that is a different
        // bug from the one being hunted — worth separating here rather than discovering later.
        foreach (IGrouping<int, StudioMesh> part in shown.GroupBy(mesh => mesh.BodyPart))
        {
            part.Select(mesh => mesh.BodyModel).Distinct().Count().ShouldBe(
                1,
                $"body part {part.Key} shows more than one alternative at body 0, so every model " +
                "with alternatives draws them stacked");
        }
    }
}
