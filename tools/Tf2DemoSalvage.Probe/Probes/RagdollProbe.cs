using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The ragdoll a model's <c>.phy</c> and skeleton build between them (B58).
/// </summary>
/// <remarks>
/// **This is the route check, not a feature demo.** `RagdollBody.Build` transcribes
/// `RagdollCreateObjects` and its two helpers, and every one of its conformance tests is synthetic
/// — a two-bone skeleton this project wrote. That proves the arithmetic and says nothing about
/// whether a REAL `.phy` maps onto a REAL skeleton: the solids are matched to bones **by name**, and
/// a single name that does not resolve refuses the whole body.
///
/// So the question this answers is the one no unit test can: does every solid in every class
/// model's `.phy` name a bone that model actually has?
///
/// <code>
///   ragdoll models/player/soldier.mdl
///   ragdoll models/player/            (every model under a prefix)
/// </code>
/// </remarks>
public sealed class RagdollProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "ragdoll";

    /// <inheritdoc/>
    public string Summary => "the bodies and joints a model's .phy builds: ragdoll <model path>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine("ragdoll <model path>");
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
            .FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed, so no model can be read.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        string wanted = arguments[0];

        if (wanted.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            Report(output, game, wanted);
            return;
        }

        // A prefix asks the same question of every model under it, which is how "do ALL nine
        // classes resolve" gets answered in one call rather than nine.
        foreach (string path in game.Archives.Paths()
            .Where(entry => entry.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            .Where(entry => entry.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)))
        {
            Report(output, game, path);
        }
    }

    private static void Report(TextWriter output, GameContent game, string model)
    {
        string physicsPath = string.Concat(model.AsSpan(0, model.Length - 4), ".phy");

        if (game.Archives.Read(model) is not { } modelBytes)
        {
            output.WriteLine($"{model}: not in the game's content");
            return;
        }

        if (game.Archives.Read(physicsPath) is not { } physicsBytes)
        {
            // **Ordinary rather than a fault.** Most models have no `.phy` at all; only something
            // the engine simulates does.
            return;
        }

        PhysicsModel physics;

        try
        {
            physics = PhysicsModel.Read(physicsBytes);
        }
        catch (InvalidOperationException failure)
        {
            output.WriteLine($"{model}: {failure.Message}");
            return;
        }

        if (physics.Constraints.Count == 0)
        {
            // A single-solid `.phy` is a physics prop, not a ragdoll — 326 of the 327 matching
            // `soldier` are gibs. Skipped rather than reported, so the ragdolls stand out.
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(modelBytes);

        RagdollBody? body = RagdollBody.Build(physics, bones);

        if (body is null)
        {
            // **The refusal is the interesting outcome**, because it means a solid named a bone the
            // model does not have — which in the engine desynchronises every constraint index
            // rather than failing. Named here so it can be chased rather than counted.
            output.WriteLine(
                $"{model}: REFUSED — {physics.Solids.Count} solids, " +
                $"{bones.Count} bones, and at least one solid names no bone:");

            foreach (PhysicsSolid solid in physics.Solids.Where(solid => !Names(bones, solid.Name)))
            {
                output.WriteLine($"    solid {solid.Index} '{solid.Name}' matches no bone");
            }

            return;
        }

        output.WriteLine(
            $"{model}: {body.Elements.Count} bodies, {body.Constraints.Count} joints, " +
            $"{bones.Count} bones, {physics.BreakPieces.Count} gibs");

        // **The gibs this model comes apart into** (B371). `InitPlayerGibs` builds exactly this
        // list and `CreatePlayerGibs` spawns from it, so a class with none here can never gib —
        // which is worth seeing beside the ragdoll rather than inferred from its absence.
        foreach (PhysicsBreakPiece piece in physics.BreakPieces)
        {
            output.WriteLine(
                $"    gib {PhysicsModel.GibPath(piece.Model)} fades after {piece.FadeTime:0.#}s");
        }

        // **The hulls, because a body with no hull cannot land on anything** (B58). Printed per
        // solid rather than totalled: a reader that finds the tree but walks one branch reports a
        // plausible total, and only the per-solid column shows the shape of the failure.
        for (int solid = 0; solid < physics.Hulls.Count; solid++)
        {
            int ledges = physics.Hulls[solid].Count;
            int triangles = 0;
            int points = 0;

            foreach (PhysicsLedge ledge in physics.Hulls[solid])
            {
                triangles += ledge.Triangles.Count;
                points += ledge.Points.Count;
            }

            float extent = 0f;

            foreach (PhysicsLedge ledge in physics.Hulls[solid])
            {
                extent = Math.Max(extent, ledge.Radius);
            }

            output.WriteLine(
                $"    hull {solid}: {ledges} ledges, {triangles} triangles, {points} points, " +
                $"radius {extent:0.###} centre {Centre(physics.Hulls[solid])}");
        }

        foreach (RagdollElement element in body.Elements)
        {
            string bone = element.BoneIndex >= 0 && element.BoneIndex < bones.Count
                ? bones[element.BoneIndex].Name
                : "?";

            string parent = element.ParentIndex >= 0
                ? element.ParentIndex.ToString(CultureInfo.InvariantCulture)
                : "root";

            output.WriteLine(
                $"    bone {element.BoneIndex.ToString(CultureInfo.InvariantCulture)} '{bone}'" +
                $" parent {parent}" +
                $" mass {element.Mass.ToString("F2", CultureInfo.InvariantCulture)}" +
                $" inertia {element.Inertia.ToString("F2", CultureInfo.InvariantCulture)}" +
                $" damping {element.Damping.ToString("F2", CultureInfo.InvariantCulture)}/" +
                element.RotationDamping.ToString("F2", CultureInfo.InvariantCulture) +
                $" offset ({element.OriginParentSpace.X.ToString("F2", CultureInfo.InvariantCulture)}, " +
                $"{element.OriginParentSpace.Y.ToString("F2", CultureInfo.InvariantCulture)}, " +
                $"{element.OriginParentSpace.Z.ToString("F2", CultureInfo.InvariantCulture)})");
        }

        // **A root count other than one is worth seeing.** The engine gives every element a parent
        // except the one no constraint names as a child; two roots would mean two disconnected
        // bodies sharing a skeleton, and none would mean a cycle.
        int roots = body.Elements.Count(element => element.ParentIndex < 0);

        output.WriteLine(
            $"    {roots.ToString(CultureInfo.InvariantCulture)} root " +
            (roots == 1 ? "(one, as a jointed body should have)" : "— NOT one"));
    }

    /// <summary>A hull's first ledge centre, in Source units — the question is which SPACE it is in.</summary>
    /// <remarks>
    /// **A centre near zero means the hull is bone-local; one tens of units out means model space.**
    /// The difference decides whether a body's hull needs the bone's bind transform applied to it,
    /// and getting it wrong displaces every limb's collision by that bone's bind offset — which
    /// looks like a corpse colliding with geometry that is not there.
    /// </remarks>
    private static string Centre(IReadOnlyList<PhysicsLedge> hull)
    {
        if (hull.Count == 0)
        {
            return "none";
        }

        const float SourceUnitsPerMetre = 1f / 0.0254f;

        System.Numerics.Vector3 centre = hull[0].Center * SourceUnitsPerMetre;

        return $"({centre.X:0.#} {centre.Y:0.#} {centre.Z:0.#})";
    }

    private static bool Names(IReadOnlyList<StudioBone> bones, string name) =>
        bones.Any(bone => string.Equals(bone.Name, name, StringComparison.OrdinalIgnoreCase));
}
