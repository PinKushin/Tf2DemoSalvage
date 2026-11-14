using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// How many of a demo's sounds carry `SND_SPEAKER`, a speaker entity, or a compatibility-mode soundlevel (256 and up),
/// through the timeline the viewer plays from (B724).
/// </summary>
/// <remarks>
/// The three are the inputs of rules read out of engine.dll that no held demo may ever exercise: a channel with
/// `SND_SPEAKER` is not summed and does not play a looping wave, and a soundlevel of 256 or more takes the goldsrc gain
/// branch. A count of 0 needs a control, so each line also gives the sounds read at all and those with
/// `SND_CHANGE_VOL`, a flag from the same field that real matches do carry.
/// </remarks>
public sealed class SoundFlagsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "sound-flags";

    /// <inheritdoc/>
    public string Summary =>
        "sounds with SND_SPEAKER, a speaker entity or a soundlevel of 256 and up, per demo: sound-flags <demo or folder>...";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("usage: sound-flags <demo or folder>...");
            return;
        }

        // One demo held at a time (B439).
        foreach (string demo in arguments.SelectMany(static path =>
            Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.dem", SearchOption.AllDirectories) : [path]))
        {
            IReadOnlyList<SceneSound> sounds;

            try
            {
                sounds = DemoTimeline.Build(File.ReadAllBytes(demo)).Sounds;
            }
            catch (InvalidDataException unreadable)
            {
                // Said, not skipped: a Git LFS pointer in a worktree reads as no demo, which is not "no speaker sounds".
                output.WriteLine($"{Path.GetFileName(demo)}: NOT READ: {unreadable.Message}");
                continue;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(demo)}: {sounds.Count} sounds; SND_SPEAKER {sounds.Count(static s => s.ThroughSpeaker)}; " +
                $"speaker entity {sounds.Count(static s => s.SpeakerEntity != -1)}; " +
                $"soundlevel >= 256 {sounds.Count(static s => s.SoundLevel >= 256)}; " +
                $"control SND_CHANGE_VOL {sounds.Count(static s => s.ChangesVolume)}"));
        }
    }
}
