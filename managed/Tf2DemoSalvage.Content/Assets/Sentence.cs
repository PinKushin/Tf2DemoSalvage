using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One runtime phoneme: <c>CBasePhonemeTag</c>'s code, start and end (B513).</summary>
/// <param name="Code">The phoneme code — the index <c>pIndexedSetting</c> looks a viseme up by.</param>
/// <param name="Start">Seconds into the sound.</param>
/// <param name="End">Seconds into the sound.</param>
public readonly record struct SentencePhoneme(int Code, float Start, float End);

/// <summary>
/// A sound's lip-sync data, <c>CSentence</c> as the engine caches it (B513): runtime phonemes and the emphasis curve.
/// </summary>
/// <param name="Phonemes">In file order, which is word order (<c>MakeRuntimeOnly</c>).</param>
/// <param name="Emphasis">(time, value) samples, value 0 to 1.</param>
/// <param name="SampleCount">The sound's sample count, from its cache record.</param>
/// <param name="SampleRate">Its sample rate.</param>
/// <remarks>
/// **TF2's voice lines are MP3s with no <c>VDAT</c> chunk; their sentences live in the VPK's sound cache**, read in
/// disassembly: <c>CAudioSourceCachedInfo::Restore</c> (x64 <c>engine.dll</c> <c>0x180053740</c>) and
/// <c>CSentence::CacheRestoreFromBuffer</c> (<c>0x1801f6100</c>, which matches <c>sentence.cpp:805</c> with the aligned
/// version 4 in place of 2).
/// </remarks>
public sealed record Sentence(
    IReadOnlyList<SentencePhoneme> Phonemes,
    IReadOnlyList<(float Time, float Value)> Emphasis,
    int SampleCount,
    int SampleRate)
{
    /// <summary>The sound's length in seconds: samples over rate. **Interpolated** — the engine's
    /// <c>GetSentenceLength</c> is closed and was not located.</summary>
    public float Length => SampleRate > 0 ? (float)SampleCount / SampleRate : 0f;

    /// <summary><c>CSentence::GetIntensity( time, endtime )</c> (<c>sentence.cpp:1475</c>).</summary>
    /// <param name="time">Seconds into the sound.</param>
    /// <param name="endTime">The sentence length.</param>
    /// <returns>Emphasis, 0 to 1; 0.5 is neutral.</returns>
    public float Intensity(float time, float endTime)
    {
        int count = Emphasis.Count;

        if (count <= 0)
        {
            return 0.5f;
        }

        (float Time, float Value) Bounded(int index)
        {
            if (index < 0)
            {
                return (0f, 0.5f);
            }

            return index >= count ? (endTime, 0.5f) : Emphasis[index];
        }

        int i;

        for (i = -1; i < count; i++)
        {
            (float Time, float Value) s = Bounded(i);
            (float Time, float Value) n = Bounded(i + 1);

            if (time >= s.Time && time <= n.Time)
            {
                break;
            }
        }

        (float Time, float Value) pre = Bounded(Math.Max(-1, i - 1));
        (float Time, float Value) start = Bounded(Math.Max(-1, i));
        (float Time, float Value) end = Bounded(Math.Min(i + 1, count));
        (float Time, float Value) next = Bounded(Math.Min(i + 2, count));

        float dt = Math.Clamp(end.Time - start.Time, 0.01f, 1.0f);
        float f2 = Math.Clamp((time - start.Time) / dt, 0f, 1f);

        // Plain Catmull_Rom_Spline (not NormalizeX), as the SDK calls it.
        (float _, float y) = ChoreoCurve.Interpolate(11, pre, start, end, next, f2);

        return Math.Clamp(y, 0f, 1f);
    }
}

/// <summary>
/// A VPK's <c>.sound.cache</c>: the engine's <c>CUtlCachedFileData&lt;CAudioSourceCachedInfo&gt;</c> (B513).
/// </summary>
/// <remarks>
/// Per entry, as <c>0x180053740</c> restores it after the container's own name and int: <c>info</c> (sample rate in
/// bits 15 and up), a flags byte, data start, data size, loop start, sample count; a sentence when flags bit 0 is set;
/// a short-counted blob for bit 1 and an unsigned-short-counted one for bit 2. The container's header is four ints and
/// the entry count; the field meanings there are not needed and not claimed.
/// </remarks>
public static class SoundCacheFile
{
    /// <summary>Reads every sentence-bearing entry, keyed by its path (backslashes, lower-cased).</summary>
    /// <param name="file">The cache file.</param>
    /// <returns>Sentences by sound path.</returns>
    /// <exception cref="InvalidDataException">An entry runs past the file.</exception>
    public static Dictionary<string, Sentence> Read(ReadOnlySpan<byte> file)
    {
        Dictionary<string, Sentence> sentences = new(StringComparer.OrdinalIgnoreCase);
        Reader reader = new(file);

        reader.Int();
        reader.Int();
        reader.Int();
        int count = reader.Int();

        for (int entry = 0; entry < count; entry++)
        {
            // Each record is prefixed by its own length, measured: the first (`scout_headleft01`) is 0x2bdf bytes and
            // the next name begins exactly there.
            int size = reader.Int();
            int next = reader.At + size;
            string name = reader.String();
            reader.Int();

            int info = reader.Int();
            byte flags = reader.Byte();
            reader.Int();
            reader.Int();
            reader.Int();
            int samples = reader.Int();

            Sentence? sentence = (flags & 1) != 0 ? ReadSentence(ref reader, samples, (int)((uint)info >> 15)) : null;

            // The cached audio and header blobs follow; the record length steps over them.
            reader.Seek(next);

            if (sentence is not null)
            {
                sentences[Normalise(name)] = sentence;
            }
        }

        return sentences;
    }

    /// <summary>A sound path as the cache spells it: backslashes, no leading <c>sound\</c> difference ignored.</summary>
    /// <param name="path">Any spelling.</param>
    /// <returns>The key.</returns>
    public static string Normalise(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Replace('/', '\\');
    }

    /// <summary><c>CSentence::CacheRestoreFromBuffer</c>, both versions (1 packed, 4 aligned in this build).</summary>
    private static Sentence? ReadSentence(ref Reader reader, int samples, int rate)
    {
        byte version = reader.Byte();

        if (version is not (1 or 4))
        {
            return null;
        }

        bool aligned = version == 4;
        int count;

        if (aligned)
        {
            reader.Byte();
            reader.Byte();
            reader.Byte();
            count = (ushort)reader.Int();
        }
        else
        {
            count = (ushort)reader.Short();
        }

        List<SentencePhoneme> phonemes = new(count);

        for (int index = 0; index < count; index++)
        {
            int code = aligned ? reader.Int() : (ushort)reader.Short();
            float start = reader.Float();
            phonemes.Add(new SentencePhoneme(code, start, reader.Float()));
        }

        int samplesOfEmphasis = aligned ? reader.Int() : reader.Short();
        List<(float, float)> emphasis = new(Math.Max(samplesOfEmphasis, 0));

        for (int index = 0; index < samplesOfEmphasis; index++)
        {
            float time = reader.Float();

            // `(float)buf.GetShort() / 32767.0f` in the SDK; the x64 build multiplies by 3.051851e-05.
            float value = aligned ? reader.Float() : reader.Short() * 3.051851e-05f;
            emphasis.Add((time, value));
        }

        if (aligned)
        {
            reader.Int();
        }
        else
        {
            reader.Byte();
        }

        return new Sentence(phonemes, emphasis, samples, rate);
    }

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _at;

        public byte Byte() => Take(1)[0];

        public short Short() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));

        public int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        public float Float() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

        public readonly int At => _at;

        public void Seek(int at)
        {
            if (at < _at || at > _bytes.Length)
            {
                throw new InvalidDataException($"A sound cache record ends at {at}, outside {_at}..{_bytes.Length}.");
            }

            _at = at;
        }

        public string String()
        {
            int end = _bytes[_at..].IndexOf((byte)0);

            if (end < 0)
            {
                throw new InvalidDataException("A sound cache name runs past the file.");
            }

            string text = Encoding.UTF8.GetString(_bytes.Slice(_at, end));
            _at += end + 1;
            return text;
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || _at + count > _bytes.Length)
            {
                throw new InvalidDataException($"A sound cache entry runs past the file at {_at}.");
            }

            ReadOnlySpan<byte> taken = _bytes.Slice(_at, count);
            _at += count;
            return taken;
        }
    }
}
