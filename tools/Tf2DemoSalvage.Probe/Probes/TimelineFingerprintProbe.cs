using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// One hash over everything a timeline's frames and soundscape lookups answer, to compare two builds of the decoder.
/// </summary>
/// <remarks>
/// **Written for D217**, which changes how frames are STORED and must not change a single value read from them. Run
/// it on the commit before and the commit after; the two hashes must match. Every public property of every frame and
/// every player is read by reflection — lists element by element, never by reference — and `SoundscapeAt` is asked at
/// every frame's tick, so a storage change that alters any answer alters the hash. Section hashes are printed too, so a
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
        "hash of every value a timeline's frames and SoundscapeAt answer, to compare decoder builds: timeline-fingerprint <demo>";

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
        long values = 0;

        using IncrementalHash frames = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using IncrementalHash soundscapes = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (TimelineFrame frame in timeline.Frames)
        {
            Append(frames, frame, ref values);
            Append(soundscapes, timeline.SoundscapeAt(frame.Tick), ref values);
        }

        output.WriteLine($"{Path.GetFileName(path)}: {timeline.Frames.Count} frames, {values} values");
        output.WriteLine($"  frames      {Convert.ToHexString(frames.GetHashAndReset())}");
        output.WriteLine($"  soundscapes {Convert.ToHexString(soundscapes.GetHashAndReset())}");
    }

    private static void Append(IncrementalHash hash, object? value, ref long values)
    {
        switch (value)
        {
            case null:
                Write(hash, "null", ref values);
                return;
            case string text:
                Write(hash, text, ref values);
                return;
            case IFormattable formattable when value.GetType().IsPrimitive || value is Enum:
                Write(hash, formattable.ToString(null, CultureInfo.InvariantCulture), ref values);
                return;
            case IEnumerable sequence:
                Write(hash, "[", ref values);
                foreach (object? item in sequence)
                {
                    Append(hash, item, ref values);
                }

                Write(hash, "]", ref values);
                return;
        }

        Type type = value.GetType();

        if (type.IsGenericType && type.FullName!.StartsWith("System.ValueTuple", StringComparison.Ordinal))
        {
            foreach (FieldInfo field in type.GetFields())
            {
                Append(hash, field.GetValue(value), ref values);
            }

            return;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.GetIndexParameters().Length == 0)
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            Write(hash, property.Name, ref values);
            Append(hash, property.GetValue(value), ref values);
        }
    }

    private static void Write(IncrementalHash hash, string text, ref long values)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(text + "\u001f"));
        values++;
    }
}
