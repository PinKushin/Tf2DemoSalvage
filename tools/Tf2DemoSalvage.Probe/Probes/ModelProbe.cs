using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What a model is made of: its meshes, its body parts and its skin families.
/// </summary>
/// <remarks>
/// **Written to settle a question reading could not.** A spy's mask appeared in the viewer log as
/// <c>[4] part 1 alt 1 … mask_spy</c>, and "which alternative is the default" decides whether the
/// mask can be drawn at all — <c>StudioModelInfo.Shows</c> is
/// <c>mesh.BodyModel == ( body / place ) % count</c>, so a mesh at alternative 1 needs a body number
/// that selects it and never draws at <c>m_nBody = 0</c>.
///
/// <code>
///   model models/player/spy.mdl
///   model models/player/spy.mdl 9        # what skin family 9 paints each mesh with
/// </code>
///
/// The skin row is the interesting half for a disguise: `C_TFPlayer::GetSkin` adds
/// <c>4 + ( ( disguiseClass - TF_FIRST_NORMAL_CLASS ) * 2 )</c>, so a friendly spy disguised as a
/// soldier draws family 9 — and whether that family paints the mask mesh with a soldier mask is a
/// fact about the shipped model rather than about this project.
/// </remarks>
public sealed class ModelProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "model";

    /// <inheritdoc/>
    public string Summary => "a model's meshes, body parts and skin families: model <path> [skin]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("model <path> [skin] — for example: model models/player/spy.mdl 9");
            return;
        }

        string path = arguments[0];
        int skin = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 0;

        string? folder = new MapLocator(
            MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();

        if (folder is null)
        {
            output.WriteLine("The game is not installed, so no model can be read.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        if (game.Archives.Read(path) is not { } bytes)
        {
            output.WriteLine($"'{path}' is not in the game's content.");
            return;
        }

        StudioModelInfo model = StudioModel.Read(bytes);
        short[] table = StudioSkins.Read(bytes);
        int references = StudioSkins.References(bytes);
        int families = StudioSkins.Families(bytes);

        output.WriteLine(
            $"{path}: {model.Meshes.Count.ToString(CultureInfo.InvariantCulture)} meshes, "
            + $"{model.BodyParts.Count.ToString(CultureInfo.InvariantCulture)} body parts, "
            + $"{families.ToString(CultureInfo.InvariantCulture)} skin families over "
            + $"{references.ToString(CultureInfo.InvariantCulture)} references");

        // **The models this one includes**, because a sequence list read from the root alone is
        // only half the answer for anything that animates — and "the root has no events" reads
        // identically to "this model has no events" until the include list is on screen (B275).
        foreach (string included in StudioModelGroups.Read(bytes))
        {
            output.WriteLine($"INCLUDE '{included}'");
        }

        // **The pose parameters, with their RANGE**, because the range decides what a missing value
        // looks like. The wire sends `m_flPoseParameter` normalised 0..1
        // (`baseanimating.cpp:243`); this project fills an uncomputed one with a raw zero and
        // normalises afterwards, so a symmetric range like a sentry's −180..180 `aim_yaw` puts it
        // dead centre — plausible, and never what the entity was actually doing.
        foreach ((StudioPoseParameter parameter, int at) in StudioSequences.PoseParameters(bytes)
            .Select((parameter, at) => (parameter, at)))
        {
            output.WriteLine(
                $"POSE {at.ToString(CultureInfo.InvariantCulture),2} "
                + $"'{parameter.Name}' {parameter.Start:0.##} to {parameter.End:0.##}"
                + (parameter.Loop != 0f ? $" loops at {parameter.Loop:0.##}" : string.Empty));
        }

        // **The bone controllers, for the same reason as the pose parameters above**: the wire
        // sends `m_flEncodedController` as a fraction over 0..1 and the model says what the
        // fraction spans, so neither half means anything alone (`CalcBoneAdj`). Printed because the
        // claim "TF2 models declare none" was measured on three PLAYER models, and a player is not
        // the denominator that decides whether the mechanism matters.
        foreach ((StudioBoneController controller, int at) in StudioBoneControllers.Read(bytes)
            .Select((controller, at) => (controller, at)))
        {
            output.WriteLine(
                $"CTRL {at.ToString(CultureInfo.InvariantCulture),2} "
                + $"bone {controller.Bone.ToString(CultureInfo.InvariantCulture)} "
                + $"type 0x{controller.Type:X} "
                + $"{controller.Start:0.##} to {controller.End:0.##}");
        }

        // **Sequences with their LOOP flag**, because that flag decides what a finished animation
        // holds. `ClampCycle` (`c_baseanimating.cpp:1431`) wraps a looping sequence and clamps a
        // one-shot to 0.999, so a `close` marked looping never stops closing — it reopens.
        foreach ((StudioSequence sequence, int at) in StudioSequences.Read(bytes)
            .Select((sequence, at) => (sequence, at)))
        {
            output.WriteLine(
                $"SEQ {at.ToString(CultureInfo.InvariantCulture),3} "
                + $"'{sequence.Label}' "
                + $"{(sequence.Loops ? "LOOPS" : "one-shot")} "
                + $"flags 0x{sequence.Flags:X}"
                + (sequence.Activity.Length > 0 ? $" act {sequence.Activity}" : string.Empty)
                + (sequence.FiredEvents.Count > 0
                    ? $" events {sequence.FiredEvents.Count}"
                    : string.Empty));

            // **The events, and WHICH SIDE fires each**, because the filter is the whole question:
            // `DoAnimationEvents` skips anything that is not the client's, so a sequence full of
            // server events gives a demo viewer nothing to do. Printed with the cycle so the
            // firing arithmetic can be checked against a real model rather than a synthetic one.
            foreach (StudioEvent fired in sequence.FiredEvents)
            {
                output.WriteLine(
                    $"      event {fired.Id,5} at cycle {fired.Cycle:0.###} "
                    + $"type 0x{fired.Type:X} "
                    + $"{(fired.FiresOnTheClient() ? "CLIENT" : "server")}"
                    + (fired.Options.Length > 0 ? $" '{fired.Options}'" : string.Empty));
            }
        }

        for (int part = 0; part < model.BodyParts.Count; part++)
        {
            (int place, int count) = model.BodyParts[part];

            output.WriteLine(
                $"PART {part.ToString(CultureInfo.InvariantCulture)} "
                + $"'{(part < model.BodyPartNames.Count ? model.BodyPartNames[part] : "?")}': "
                + $"place {place.ToString(CultureInfo.InvariantCulture)}, "
                + $"{count.ToString(CultureInfo.InvariantCulture)} alternatives");
        }

        foreach (StudioMesh mesh in model.Meshes)
        {
            // **Both the family asked for and family zero**, because the interesting answer is
            // whether they differ: a mesh painted identically in every family is not what a skin
            // is for, and a mesh that changes is how a mask or a team colour is done.
            output.WriteLine(
                $"MESH part {mesh.BodyPart.ToString(CultureInfo.InvariantCulture)} "
                + $"alt {mesh.BodyModel.ToString(CultureInfo.InvariantCulture)} "
                + $"skinref {mesh.MaterialIndex.ToString(CultureInfo.InvariantCulture)} "
                + $"-> skin0 '{Material(model, table, references, families, mesh.MaterialIndex, 0)}' "
                + $"skin{skin.ToString(CultureInfo.InvariantCulture)} "
                + $"'{Material(model, table, references, families, mesh.MaterialIndex, skin)}' "
                + $"shownAtBody0 {model.Shows(mesh, 0)}");
        }
    }

    /// <summary>Which material a family paints one skinref with.</summary>
    private static string Material(
        StudioModelInfo model,
        short[] table,
        int references,
        int families,
        int skinref,
        int skin)
    {
        if (references <= 0 || families <= 0 || skinref < 0 || skinref >= references)
        {
            return "?";
        }

        // `g_skinref[skin][skinref]`, a row-major table of shorts — the same arithmetic
        // `StudioSkins.TextureFor` does, spelled out here so the probe reports the raw answer
        // rather than a helper's interpretation of it.
        int row = skin >= 0 && skin < families ? skin : 0;
        int at = (row * references) + skinref;

        if (at < 0 || at >= table.Length)
        {
            return "?";
        }

        int texture = table[at];

        return texture >= 0 && texture < model.Materials.Count
            ? model.Materials[texture]
            : $"#{texture.ToString(CultureInfo.InvariantCulture)}";
    }
}
