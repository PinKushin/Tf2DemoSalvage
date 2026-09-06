using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What a model's <c>.phy</c> file holds, and how much of it is readable without Havok.
/// </summary>
/// <remarks>
/// **The route measurement for B58**, taken before any solver exists — `measure-the-route-before-
/// building-on-it`. A corpse stands upright because nothing simulates it, and the question that
/// decides whether that is even approachable is what a `.phy` actually contains:
///
/// <code>
/// typedef struct phyheader_s
/// {
///     int    size;
///     int    id;
///     int    solidCount;
///     int32  checkSum;   // checksum of source .mdl file
/// } phyheader_t;
/// </code>
///
/// `phyfile.h:14-21`. Sixteen bytes, then `solidCount` collision solids in Havok's closed `IVPS`
/// format, and then — the part that matters — **a plain-text KeyValues block** carrying the ragdoll
/// joints: which bone hangs off which, and the limits of each axis.
///
/// **So the two halves have completely different prospects.** The collision hulls would need a
/// closed format reverse-engineered; the constraint graph is text at the end of the file. This
/// probe reports both so the answer is a measurement rather than an expectation.
/// </remarks>
public sealed class RagdollConstraintProbe : IProbe
{
    /// <inheritdoc />
    public string Name => "ragdoll-constraints";

    /// <inheritdoc />
    public string Summary =>
        "what a model's .phy holds — solids, and the text ragdoll joints: " +
        "ragdoll-constraints [model substring]";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
                .FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game folder could not be found.");
            return;
        }

        string archivePath = Path.Combine(folder, "tf2_misc_dir.vpk");

        if (!File.Exists(archivePath))
        {
            output.WriteLine($"No archive at {archivePath}.");
            return;
        }

        string wanted = arguments.Count > 0 ? arguments[0] : "models/player/";

        VpkArchive archive = VpkArchive.Open(archivePath);

        List<string> paths = [.. archive.Paths
            .Where(entry => entry.EndsWith(".phy", StringComparison.OrdinalIgnoreCase))
            .Where(entry => entry.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry, StringComparer.Ordinal)];

        output.WriteLine(
            $"{paths.Count} .phy files matching '{wanted}' in {Path.GetFileName(archivePath)}");

        int shown = 0;

        // **The census that decides whether two filed divergences are urgent or dead.** Valve's
        // ragdoll code reads FIVE block names out of this text and this project reads two, so the
        // question is which of the other three any TF2 model actually ships. `solid` and
        // `ragdollconstraint` are in the list as the CONTROL: they must be found in every file that
        // parses, and their counts are cross-checked against the production reader below. A census
        // whose control comes back zero is a broken search, not an absent feature.
        Dictionary<string, int> blocks = new(StringComparer.Ordinal)
        {
            ["solid"] = 0,
            ["ragdollconstraint"] = 0,
            ["collisionrules"] = 0,
            ["animatedfriction"] = 0,
            ["editparams"] = 0,
        };

        int filesRead = 0;
        int scanDisagreed = 0;
        List<string> jointsWithoutRules = [];

        // **The census that decides whether half of vphysics' constraint solve runs at all.** Both
        // solve routines are dominated by a spring/friction branch gated on a byte that
        // `FUN_18000eac0` sets from `(virtualCall * torque) != 0`, and `SetAxisFriction( rmin, rmax,
        // friction )` puts this number straight into `torque` with the angular velocity left at
        // zero (`constraints.h:68-74`). So a corpus of zeroes means that branch never executes for
        // a TF2 corpse and the whole solve is a limit clamp — which is what got transcribed.
        //
        // **Counted rather than assumed from one file.** The first constraint of the first model
        // reads zero on all three axes, and one constraint is not a census: `physics_prop_ragdoll`
        // ships `SetAxisFriction( -2, 2, 20 )` in Valve's own code, so a nonzero is entirely
        // possible and would make the transcription wrong.
        int axesCounted = 0;
        int axesWithFriction = 0;
        float largestFriction = 0f;
        string largestFrictionModel = string.Empty;

        foreach (string path in paths)
        {
            if (archive.ReadFile(path) is not { Length: >= HeaderSize } bytes)
            {
                continue;
            }

            // **The production reader, not a second one** (B58). This probe counted markers in the
            // raw text before `PhysicsModel` existed, which was the right way to establish what a
            // `.phy` holds and the wrong way to keep reporting it: a probe that reimplements the
            // thing it measures agrees with whoever wrote the probe. Now the numbers below come
            // from the same code the ragdoll work will use, so the two cannot disagree.
            PhysicsModel physics = PhysicsModel.Read(bytes);

            int size = BitConverter.ToInt32(bytes, 0);
            int solids = physics.DeclaredSolidCount;

            // **The text block is found by looking for it, not by arithmetic**, because the solids
            // between the header and it are Havok's format and this project cannot walk them. A
            // `.phy`'s KeyValues section is plain ASCII and always contains `ragdollconstraint` when
            // the model has joints, so searching the tail is both simpler and honest about what is
            // understood.
            string tail = Encoding.ASCII.GetString(bytes);

            int text = tail.IndexOf("solid {", StringComparison.Ordinal);

            int joints = physics.Constraints.Count;
            int solidBlocks = physics.Solids.Count;

            filesRead++;

            // **The pairing is the interesting number, not either count alone.** A model with
            // joints and no collision rules has to be handled by whatever reads the block, and one
            // outlier is enough to make the absence a real branch rather than a theoretical one.
            if (joints > 0 && Occurrences(tail, "collisionrules {") == 0)
            {
                jointsWithoutRules.Add(Path.GetFileName(path));
            }

            foreach (float friction in physics.Constraints
                .SelectMany(joint => new[] { joint.X.Friction, joint.Y.Friction, joint.Z.Friction }))
            {
                axesCounted++;

                if (friction == 0f)
                {
                    continue;
                }

                axesWithFriction++;

                if (Math.Abs(friction) > Math.Abs(largestFriction))
                {
                    largestFriction = friction;
                    largestFrictionModel = Path.GetFileName(path);
                }
            }

            foreach (string block in blocks.Keys.ToList())
            {
                if (Occurrences(tail, block + " {") is var count && count > 0)
                {
                    blocks[block]++;
                }

                // The control, checked per file rather than only in aggregate: the raw scan and the
                // production reader must agree about the two blocks both of them understand.
                if ((block == "solid" && count != solidBlocks) ||
                    (block == "ragdollconstraint" && count != joints))
                {
                    scanDisagreed++;
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {Path.GetFileName(path)}: header size {size}, {solids} solids; " +
                $"text at {(text < 0 ? "NOT FOUND" : text.ToString(CultureInfo.InvariantCulture))}, " +
                $"{solidBlocks} solid blocks, {joints} ragdoll constraints"));

            if (shown < 1 && text >= 0)
            {
                shown++;

                // **One constraint verbatim, because a count says nothing about whether the fields
                // are the ones a solver needs.** A joint that names its two bones and its three
                // axis limits is usable; a count of 20 is compatible with the block being
                // unparseable.
                int joint = tail.IndexOf("ragdollconstraint", StringComparison.Ordinal);

                if (joint >= 0)
                {
                    int end = Math.Min(joint + 260, tail.Length);

                    output.WriteLine("  --- the first constraint, verbatim ---");
                    output.WriteLine(tail[joint..end].ReplaceLineEndings("\n    "));
                    output.WriteLine("  --- ends ---");
                }

                // **A solid block too, because the constraints name bones by INDEX and an index
                // without a name is not usable.** `"parent" "0"` means nothing until something says
                // which bone solid 0 is. If the solids carry a `name`, the whole joint graph maps
                // onto the skeleton; if they do not, the constraint text is a set of numbers about
                // an unknown ordering.
                output.WriteLine("  --- the first solid, verbatim ---");
                output.WriteLine(tail[text..Math.Min(text + 260, tail.Length)]
                    .ReplaceLineEndings("\n    "));
                output.WriteLine("  --- ends ---");
            }
        }

        output.WriteLine();
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Block census over {filesRead} readable .phy files — how many DECLARE each block:"));

        foreach ((string block, int count) in blocks)
        {
            string note = block switch
            {
                "solid" or "ragdollconstraint" => "  (control — this project parses it)",
                "collisionrules" => "  (which bodies may touch; ragdoll_shared.cpp:296)",
                "animatedfriction" => "  (the friction ramp; ragdoll_shared.cpp:147)",
                _ => string.Empty,
            };

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {block,-18} {count,5} of {filesRead}{note}"));
        }

        output.WriteLine(
            jointsWithoutRules.Count == 0
                ? "  Every model with ragdoll joints also declares collisionrules."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {jointsWithoutRules.Count} model(s) have joints and NO collisionrules: " +
                    $"{string.Join(", ", jointsWithoutRules)}"));

        string frictionNote = axesWithFriction == 0
            ? " — so vphysics' spring/friction branch never runs on these models."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"; largest {largestFriction} on {largestFrictionModel}.");

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {axesWithFriction} of {axesCounted} joint axes declare a nonzero friction{frictionNote}"));

        output.WriteLine(
            scanDisagreed == 0
                ? "  The raw scan and PhysicsModel agree on both control blocks in every file."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"  WARNING: the scan and PhysicsModel disagreed {scanDisagreed} times — " +
                    $"treat every number above as suspect, the instrument is broken."));
    }

    /// <summary>How many times a block opener appears in the text.</summary>
    /// <param name="text">The <c>.phy</c>'s KeyValues tail, as ASCII.</param>
    /// <param name="opener">The block name followed by its brace.</param>
    /// <returns>The count.</returns>
    /// <remarks>
    /// **Counted rather than tested for presence**, because the count is what the control needs: a
    /// search that finds `solid {` once in a file with seventeen solids is finding something else.
    /// </remarks>
    private static int Occurrences(string text, string opener)
    {
        int count = 0;
        int at = text.IndexOf(opener, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(opener, at + opener.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>Bytes of <c>phyheader_t</c>.</summary>
    private const int HeaderSize = 16;

}
