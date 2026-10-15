using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// One hash over everything a timeline's frames, prop keyframes and soundscape lookups answer, to compare two builds of
/// the decoder.
/// </summary>
/// <remarks>
/// **Written for D217**, which changes how the timeline is STORED and built and must not change a single value read
/// from it. Run it on the commit before and the commit after; the hashes must match. The hashing is
/// <see cref="TimelineFingerprint"/>, which the snapshot-and-resume test uses too. Section hashes are printed, so a
/// mismatch names the part that moved.
///
/// **Its control**: the probe prints the number of values it hashed; a hash over nothing would match anything.
///
/// It asserts nothing — a measurement is not a test (D38).
/// </remarks>
public sealed class TimelineFingerprintProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "timeline-fingerprint";

    /// <inheritdoc/>
    public string Summary =>
        "hash of every value a timeline's frames, prop keyframes and SoundscapeAt answer, to compare decoder builds: timeline-fingerprint <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("timeline-fingerprint <demo>");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        TimelineFingerprint fingerprint = TimelineFingerprint.Of(timeline);

        output.WriteLine($"{Path.GetFileName(path)}: {timeline.Frames.Count} frames, {fingerprint.Values} values");
        output.WriteLine($"  frames      {fingerprint.Frames}");
        output.WriteLine($"  soundscapes {fingerprint.Soundscapes}");
        output.WriteLine($"  props       {fingerprint.Props}");
    }
}
