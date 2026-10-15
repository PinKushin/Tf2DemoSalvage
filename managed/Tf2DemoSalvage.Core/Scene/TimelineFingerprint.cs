using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// Hashes of every value a timeline's frames, prop keyframes and soundscape lookups answer, to prove two timelines
/// equal (D217).
/// </summary>
/// <param name="Frames">Every public value of every frame and player, lists element by element.</param>
/// <param name="Soundscapes">`SoundscapeAt` asked at every frame's tick.</param>
/// <param name="Props">Every track's identity and stored keyframes.</param>
/// <param name="Values">How many values were hashed — the control: a hash over nothing matches anything.</param>
/// <remarks>
/// **One instrument for two readers**: the `timeline-fingerprint` probe compares decoder builds on real demos, and the
/// snapshot-and-resume test compares a resumed build with a straight one. A copy of this in each would be two answers to
/// one question that could disagree.
/// </remarks>
public sealed record TimelineFingerprint(string Frames, string Soundscapes, string Props, long Values)
{
    /// <summary>Fingerprints a timeline.</summary>
    /// <param name="timeline">The timeline.</param>
    /// <returns>Its hashes.</returns>
    public static TimelineFingerprint Of(DemoTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        long values = 0;

        using IncrementalHash frames = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using IncrementalHash soundscapes = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using IncrementalHash props = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (TimelineFrame frame in timeline.Frames)
        {
            Append(frames, frame, ref values);
            Append(soundscapes, timeline.SoundscapeAt(frame.Tick), ref values);
        }

        foreach (ScenePropTrack track in timeline.Props.Concat(timeline.PlayerTracks).Distinct())
        {
            Append(props, track.EntityIndex, ref values);
            Append(props, track.SerialNumber, ref values);
            Append(props, track.EndTick, ref values);

            foreach ((int tick, ScenePose pose) in track.Keyframes)
            {
                Append(props, tick, ref values);
                Append(props, pose, ref values);
            }
        }

        return new TimelineFingerprint(
            Convert.ToHexString(frames.GetHashAndReset()),
            Convert.ToHexString(soundscapes.GetHashAndReset()),
            Convert.ToHexString(props.GetHashAndReset()),
            values);
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
