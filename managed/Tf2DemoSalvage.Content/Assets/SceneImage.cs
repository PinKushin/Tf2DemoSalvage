using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Text;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// TF2's compiled choreography archive — <c>scenes/scenes.image</c> (B351).
/// </summary>
/// <remarks>
/// **A taunt is a SCENE, and the wire carries only its filename.** `DT_SceneEntity` sends
/// `m_nSceneStringIndex` into the `"Scenes"` network string table
/// (<c>gameinterface.cpp:1448</c>); no resolved sequence is ever networked, and the item alone
/// cannot say which one was chosen because the server picks at random from the item's list
/// (<c>tf_player.cpp:17391</c>). The transmitted filename is the only thing that knows.
///
/// **No loose `.vcd` ships** — checked across the whole install and inside all nine `*_dir.vpk`,
/// with a known-present extension as the control. Everything is in this one file.
///
/// **Both formats are PUBLISHED, which is why this needed no reverse engineering.**
/// `src/public/scenefilecache/SceneImageFile.h` declares the container:
///
/// <code>
/// struct SceneImageHeader_t { int nId, nVersion, nNumScenes, nNumStrings, nSceneEntryOffset; };
/// // then uint32 stringOffsets[nNumStrings], each from the START of the file
/// struct SceneImageEntry_t { CRC32_t crcFilename; int nDataOffset, nDataLength, nSceneSummaryOffset; };
/// </code>
///
/// and `src/game/shared/choreoscene.cpp:3796` declares the compiled VCD inside it, down to the
/// byte. The runtime parser ships only in `scenefilecache.dll`, which does not matter.
///
/// **The directory is sorted by CRC so the engine can binary search it**, and this reader does the
/// same — 9,939 scenes is not a list to walk per taunt.
/// </remarks>
public sealed class SceneImage
{
    /// <summary><c>SCENE_IMAGE_ID</c>, <c>MAKEID('V','S','I','F')</c>.</summary>
    private const int Magic = 0x46495356;

    /// <summary><c>SCENE_IMAGE_VERSION</c>.</summary>
    private const int SupportedVersion = 2;

    /// <summary><c>SCENE_BINARY_TAG</c>, <c>MAKEID('b','v','c','d')</c>.</summary>
    private const int SceneTag = 0x64637662;

    /// <summary><c>SCENE_BINARY_VERSION</c>.</summary>
    private const byte SceneVersion = 0x04;

    /// <summary>Bytes in one <c>SceneImageEntry_t</c>.</summary>
    private const int EntryBytes = 16;

    /// <summary>Bytes in <c>SceneImageHeader_t</c>.</summary>
    private const int HeaderBytes = 20;

    /// <summary><c>CChoreoEvent::GESTURE</c>, from the published enum.</summary>
    private const byte Gesture = 6;

    /// <summary><c>CChoreoEvent::SEQUENCE</c>.</summary>
    private const byte Sequence = 7;

    /// <summary><c>CChoreoEvent::SPEAK</c>, which writes a caption after its flex tracks.</summary>
    private const byte Speak = 5;

    /// <summary><c>CChoreoEvent::LOOP</c>, which writes its count after its flex tracks.</summary>
    private const byte Loop = 12;

    /// <summary>Bytes a <c>SPEAK</c> adds: caption type, pooled token, flags.</summary>
    private const int SpeakBytes = 4;

    /// <summary>Bytes in one flex sample: time, value, curve type.</summary>
    private const int SampleBytes = 7;

    /// <summary><c>NUM_ABS_TAG_TYPES</c> — <c>PLAYBACK</c> and <c>ORIGINAL</c>.</summary>
    private const int AbsoluteTagTypes = 2;

    private readonly ReadOnlyMemory<byte> _file;
    private readonly int _entriesAt;
    private readonly int[] _strings;

    private SceneImage(ReadOnlyMemory<byte> file, int entriesAt, int[] strings, int scenes)
    {
        _file = file;
        _entriesAt = entriesAt;
        _strings = strings;
        Count = scenes;
    }

    /// <summary>How many scenes the archive holds.</summary>
    public int Count { get; }

    /// <summary>Reads the archive's directory.</summary>
    /// <param name="file">The whole <c>scenes.image</c>.</param>
    /// <returns>The archive, or null when this is not one.</returns>
    /// <remarks>
    /// **Null rather than throwing for a file that is not a scene image**, because a missing or
    /// replaced archive costs taunts and nothing else — the same rule the particle definitions
    /// follow. A file that IS one and is malformed still throws, because that is a defect in this
    /// reader until shown otherwise.
    /// </remarks>
    public static SceneImage? Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> span = file.Span;

        if (span.Length < HeaderBytes ||
            BinaryPrimitives.ReadInt32LittleEndian(span) != Magic ||
            BinaryPrimitives.ReadInt32LittleEndian(span[4..]) != SupportedVersion)
        {
            return null;
        }

        int scenes = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
        int stringCount = BinaryPrimitives.ReadInt32LittleEndian(span[12..]);
        int entriesAt = BinaryPrimitives.ReadInt32LittleEndian(span[16..]);

        if (scenes < 0 || stringCount < 0 || entriesAt < HeaderBytes ||
            (long)entriesAt + ((long)scenes * EntryBytes) > span.Length ||
            HeaderBytes + ((long)stringCount * 4) > span.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A scene image declaring {scenes:N0} scenes and {stringCount:N0} strings does not " +
                $"fit in {span.Length:N0} bytes."));
        }

        // The pool is offsets from the START of the file, which is what `String()` does:
        // `(char *)this + pTable[iString]`.
        int[] strings = new int[stringCount];

        for (int index = 0; index < stringCount; index++)
        {
            strings[index] = BinaryPrimitives.ReadInt32LittleEndian(span[(HeaderBytes + (index * 4))..]);
        }

        return new SceneImage(file, entriesAt, strings, scenes);
    }

    /// <summary>The animation sequence a scene plays, or null when it names none.</summary>
    /// <param name="scene">The scene's name as the wire spells it, such as <c>taunt_guitar_riff</c>.</param>
    /// <returns>The sequence name for the model to look up, or null.</returns>
    /// <remarks>
    /// **The name is normalised to `scenes\&lt;name&gt;.vcd` before hashing**, because that is what
    /// the CRC in the directory was taken over — `SceneImageEntry_t` says so in a comment:
    /// *"expected to be normalized as scenes\???.vcd"*. A forward slash or a missing extension
    /// hashes to something that is simply not in the table, and the lookup would report the scene
    /// as absent rather than as misspelled.
    ///
    /// </remarks>
    private (int At, int Length)? Locate(string scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        string path = scene.Replace('/', '\\');

        if (!path.StartsWith("scenes\\", StringComparison.OrdinalIgnoreCase))
        {
            path = "scenes\\" + path;
        }

        if (!path.EndsWith(".vcd", StringComparison.OrdinalIgnoreCase))
        {
            path += ".vcd";
        }

        // **Lowered byte by byte over ASCII, which is what the engine does.** `V_strlower` walks
        // bytes; it is not culture-aware, so a name carrying anything outside ASCII must hash the
        // same here as there rather than being folded by .NET's rules. It also happens to be the
        // only lowering the analyzers accept, and for once the analyzer and the format agree.
        byte[] bytes = Encoding.ASCII.GetBytes(path);

        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] is >= (byte)'A' and <= (byte)'Z')
            {
                bytes[index] += 'a' - 'A';
            }
        }

        // **`Crc32.Hash` returns the digest LITTLE-ENDIAN**, the same way `BspMapChecksum` reads it.
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(Crc32.Hash(bytes));

        return Find(crc);
    }

    /// <summary>The animation sequence a scene plays, or null when it names none.</summary>
    /// <param name="scene">The scene's name as the wire spells it.</param>
    /// <returns>The sequence name for the model to look up, or null.</returns>
    /// <remarks>
    /// **The FIRST gesture or sequence event wins**, which is what
    /// `C_TFPlayer::StartGestureSceneEvent` consumes:
    /// `info->m_nSequence = LookupSequence( event->GetParameters() )`
    /// (<c>c_tf_player.cpp:9456</c>) — the parameter is the sequence's NAME, resolved against the
    /// player's own model rather than against anything in the scene.
    /// </remarks>
    public string? SequenceFor(string scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        return Locate(scene) is not { } entry ? null : SequenceIn(Body(entry), null, out _, out _);
    }

    /// <summary>Every event one scene declares, in the order the compiler wrote them.</summary>
    /// <param name="scene">The scene's name as the wire spells it.</param>
    /// <returns>The events, empty when the scene is absent.</returns>
    /// <remarks>
    /// **The same walk <see cref="SequenceFor"/> uses, collecting instead of stopping** — a second
    /// implementation would agree with whoever wrote it rather than with the archive. It is what
    /// playback needs, because the engine runs the events on the scene's own clock rather than
    /// playing one sequence.
    /// </remarks>
    public IReadOnlyList<SceneEvent> EventsFor(string scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (Locate(scene) is not { } entry)
        {
            return [];
        }

        List<SceneEvent> events = [];

        SequenceIn(Body(entry), events, out _, out _);

        return events;
    }

    /// <summary>What a scene does to the player it animates, resolved once (B351).</summary>
    /// <param name="scene">The scene's name as the wire spells it.</param>
    /// <returns>The plan, or null when the scene is not in the archive.</returns>
    /// <remarks>
    /// **The `LOOP` event's parameter is a TIME, not a name.** `DispatchProcessLoop` reads it as
    /// <c>(float)atof( event-&gt;GetParameters() )</c> and hands it to `SetCurrentTime`
    /// (<c>c_sceneentity.cpp:574</c>) — measured on `taunt_jackhammer_rodeo`, the string is
    /// <c>"2.500000"</c>. So the same field that names a sequence on a gesture is a clock position on
    /// a loop, which is why this cannot be read without knowing the event's type.
    ///
    /// **A scene with no gesture returns an empty plan rather than null**, because "plays no
    /// animation" is most of the archive and must be distinguishable from "not in the archive".
    /// </remarks>
    public SceneTaunt? TauntFor(string scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (Locate(scene) is not { } entry)
        {
            return null;
        }

        List<SceneEvent> events = [];

        SequenceIn(Body(entry), events, out _, out _);

        List<SceneTauntGesture> gestures = [];
        float loopsFrom = -1f;
        float loopsAt = -1f;

        foreach (SceneEvent one in events)
        {
            if (one.Type is Gesture or Sequence && one.Parameters.Length > 0)
            {
                gestures.Add(new SceneTauntGesture(one.Start, one.Parameters));
                continue;
            }

            // **The FIRST loop wins, matching the engine's own scan.** `StopGestureSceneEvent`
            // breaks out of its walk at the first `LOOP` it finds (`c_tf_player.cpp:9500`), and a
            // scene with two of them is not something any shipped taunt does.
            if (one.Type == Loop && loopsAt < 0f &&
                float.TryParse(
                    one.Parameters, NumberStyles.Float, CultureInfo.InvariantCulture, out float back))
            {
                loopsFrom = back;
                loopsAt = one.Start;
            }
        }

        return new SceneTaunt(gestures, loopsFrom, loopsAt);
    }

    /// <summary>Every event one directory slot's scene declares.</summary>
    /// <param name="index">Which slot.</param>
    /// <returns>The events, empty when the slot is out of range.</returns>
    public IReadOnlyList<SceneEvent> EventsAt(int index)
    {
        ReadOnlyMemory<byte> body = BodyAt(index);

        if (body.IsEmpty)
        {
            return [];
        }

        List<SceneEvent> events = [];

        SequenceIn(body, events, out _, out _);

        return events;
    }

    /// <summary>One directory slot's sequence and whether its scene parsed to the end.</summary>
    /// <param name="index">Which slot.</param>
    /// <returns>The sequence name if the scene names one, and whether the walk stayed in bounds.</returns>
    /// <remarks>
    /// **This is how the reader is censused over the whole archive rather than over the taunts.**
    /// 730 of the 9,939 scenes are named by `items_game.txt`, so measuring only those says the walk
    /// works on 7.3% of the population — and a stride that is wrong for a kind of event no taunt uses
    /// would pass every one of them (`docs/memory/the-denominator-decides-what-can-be-lost.md`).
    /// </remarks>
    public SceneWalk SequenceAt(int index)
    {
        ReadOnlyMemory<byte> body = BodyAt(index);

        if (body.IsEmpty)
        {
            return new SceneWalk(null, Complete: false, Stopped: 0, Length: 0);
        }

        string? sequence = SequenceIn(body, null, out bool complete, out int stopped);

        return new SceneWalk(sequence, complete, stopped, body.Length);
    }

    /// <summary>One directory slot's decompressed bytes, so an incomplete walk can be looked at.</summary>
    /// <param name="index">Which slot.</param>
    /// <returns>The bytes, empty when the slot is out of range or the entry is unreadable.</returns>
    public ReadOnlyMemory<byte> BodyAt(int index)
    {
        if (index < 0 || index >= Count)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        ReadOnlySpan<byte> span = _file.Span[(_entriesAt + (index * EntryBytes))..];

        return Body((
            BinaryPrimitives.ReadInt32LittleEndian(span[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(span[8..])));
    }

    /// <summary>One scene's compiled bytes, decompressed — for probing what a parse failure hit.</summary>
    /// <param name="scene">The scene's name.</param>
    /// <returns>The bytes, empty when the scene is absent or the entry is unreadable.</returns>
    /// <remarks>
    /// **Separate from <see cref="SequenceFor"/> so a failure can be located.** A null sequence has
    /// three possible causes — the name is not in the directory, the entry did not decompress, or
    /// the events did not parse — and one method returning null for all three cannot tell them
    /// apart.
    /// </remarks>
    public ReadOnlyMemory<byte> BodyFor(string scene) =>
        Locate(scene) is { } entry ? Body(entry) : ReadOnlyMemory<byte>.Empty;

    /// <summary>Whether a CRC is in the directory — a control for the search itself.</summary>
    /// <param name="crc">The hash to look for.</param>
    /// <returns>Whether the directory holds it.</returns>
    /// <remarks>
    /// **Exists so an absence can be told apart from a broken search.** Every name asked of
    /// <see cref="SequenceFor"/> can come back empty for two quite different reasons — the name is
    /// not what the archive calls that scene, or the lookup is wrong — and a hash read straight out
    /// of the directory separates them (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md#an-empty-search-needs-a-control`).
    /// </remarks>
    public bool Contains(uint crc) => Find(crc) is not null;

    /// <summary>The CRC of one directory slot, so a caller can ask for something that MUST be there.</summary>
    /// <param name="index">Which slot.</param>
    /// <returns>Its hash.</returns>
    public uint CrcAt(int index) =>
        index < 0 || index >= Count
            ? 0u
            : BinaryPrimitives.ReadUInt32LittleEndian(
                _file.Span[(_entriesAt + (index * EntryBytes))..]);

    /// <summary>Binary searches the CRC-sorted directory.</summary>
    private (int At, int Length)? Find(uint crc)
    {
        ReadOnlySpan<byte> span = _file.Span;

        int low = 0;
        int high = Count - 1;

        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int at = _entriesAt + (middle * EntryBytes);

            uint here = BinaryPrimitives.ReadUInt32LittleEndian(span[at..]);

            if (here == crc)
            {
                return (
                    BinaryPrimitives.ReadInt32LittleEndian(span[(at + 4)..]),
                    BinaryPrimitives.ReadInt32LittleEndian(span[(at + 8)..]));
            }

            // **Unsigned comparison, because a CRC is unsigned and the table is sorted that way.**
            // Comparing as signed puts every hash with the top bit set before every one without,
            // so the search walks off into the wrong half for about half of all scenes.
            if (here < crc)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return null;
    }

    /// <summary>One entry's bytes, decompressed when it is compressed.</summary>
    private ReadOnlyMemory<byte> Body((int At, int Length) entry)
    {
        if (entry.At < 0 || entry.Length < 0 ||
            (long)entry.At + entry.Length > _file.Length)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        ReadOnlyMemory<byte> raw = _file.Slice(entry.At, entry.Length);

        // The same `lzma_header_t` a BSP lump carries, and the same reader: 'LZMA', the actual
        // size, the compressed size, then five property bytes.
        if (raw.Length < 17 || !raw.Span[..4].SequenceEqual("LZMA"u8))
        {
            return raw;
        }

        int actual = BinaryPrimitives.ReadInt32LittleEndian(raw.Span[4..]);

        return actual is < 0 or > (64 * 1024 * 1024)
            ? ReadOnlyMemory<byte>.Empty
            : ValveLzma.Decode(raw.Span.Slice(12, 5), raw.Span[17..], actual);
    }

    /// <summary>The pooled string an index names.</summary>
    private string Pooled(int index)
    {
        if (index < 0 || index >= _strings.Length)
        {
            return string.Empty;
        }

        int at = _strings[index];

        if (at < 0 || at >= _file.Length)
        {
            return string.Empty;
        }

        ReadOnlySpan<byte> from = _file.Span[at..];
        int end = from.IndexOf((byte)0);

        return Encoding.UTF8.GetString(end < 0 ? from : from[..end]);
    }

    /// <summary>Walks a compiled VCD and returns the first gesture's sequence.</summary>
    /// <remarks>
    /// **The top-level event list holds only the events with NO actor**, which for a taunt is the
    /// `LOOP` and the `STOPPOINT` rather than the animation:
    /// <c>if ( e-&gt;GetActor() ) continue;</c> (<c>choreoscene.cpp:3714</c>). Everything belonging
    /// to an actor is written afterwards as actor → channel → event
    /// (<c>choreoactor.cpp:243</c>, <c>choreochannel.cpp:522</c>), so a reader that stops at the
    /// top-level list finds a `LOOP` and reports the taunt as having no sequence — which is exactly
    /// what a real `taunt_hi5_start.vcd` did.
    ///
    /// **Every event is parsed in FULL even though only one is wanted**, because the events are
    /// written back to back with no length prefix: reaching the second means having consumed all of
    /// the first, ramps, tags, flex tracks, per-type trailers and all.
    /// </remarks>
    /// <param name="vcd">One scene's decompressed bytes.</param>
    /// <param name="complete">
    /// Whether the walk consumed every declared actor, channel and event without running out of
    /// bytes. **A null sequence and a null sequence with this false are different findings** — the
    /// first is a scene that plays no gesture, which most of the archive is, and the second is this
    /// reader losing its place. Collapsing them is how the actor tree stayed missing.
    /// </param>
    /// <param name="into">Where to collect every event, or null to only look for the sequence.</param>
    /// <param name="stopped">
    /// The cursor the walk finished on, so an incomplete one can be looked at rather than counted.
    /// </param>
    private string? SequenceIn(
        ReadOnlyMemory<byte> vcd, List<SceneEvent>? into, out bool complete, out int stopped)
    {
        ReadOnlySpan<byte> span = vcd.Span;

        complete = false;
        stopped = 0;

        if (span.Length < 11 ||
            BinaryPrimitives.ReadInt32LittleEndian(span) != SceneTag ||
            span[4] != SceneVersion)
        {
            return null;
        }

        // tag, version, then the text version's CRC, which the reader itself skips.
        int at = 9;
        string? found = null;

        complete = Events(span, ref at, CountIn(span, ref at), into, ref found);

        int actors = CountIn(span, ref at);

        for (int actor = 0; actor < actors; actor++)
        {
            at += 2;                                      // the actor's name, pooled

            int channels = CountIn(span, ref at);

            for (int channel = 0; channel < channels; channel++)
            {
                at += 2;                                  // the channel's name, pooled

                complete &= Events(span, ref at, CountIn(span, ref at), into, ref found);

                at += 1;                                  // the channel's active flag
            }

            at += 1;                                      // the actor's active flag
        }

        // **The scene ramp and the rest follow, so landing short is expected** — but landing PAST
        // the end means a stride was wrong somewhere above, and every name found after that point
        // was read from the wrong offset.
        complete &= at <= span.Length;
        stopped = at;

        return found;
    }

    /// <summary>A one-byte count, advancing the cursor and refusing to read past the end.</summary>
    /// <remarks>
    /// **Running out pushes the cursor PAST the end rather than to it**, so every caller's own
    /// bounds check fires and the walk reports itself incomplete instead of quietly reading zeros.
    /// </remarks>
    private static int CountIn(ReadOnlySpan<byte> span, ref int at)
    {
        if (at < 0 || at >= span.Length)
        {
            at = span.Length + 1;

            return 0;
        }

        return span[at++];
    }

    /// <summary>Walks a run of events, keeping the first gesture or sequence parameter it sees.</summary>
    /// <remarks>
    /// **It does not stop at the first gesture**, because a channel's events are consecutive and the
    /// caller still has to reach the next channel and the next actor. Bailing out early would leave
    /// the cursor mid-event, and the actor tree after it unreadable.
    /// </remarks>
    /// <returns>Whether every declared event was consumed without running out of bytes.</returns>
    private bool Events(
        ReadOnlySpan<byte> span, ref int at, int count, List<SceneEvent>? into, ref string? found)
    {
        for (int index = 0; index < count; index++)
        {
            if (at + 19 > span.Length)
            {
                return false;
            }

            int began = at;
            byte type = span[at++];

            at += 2;                                  // name, pooled

            float start = BinaryPrimitives.ReadSingleLittleEndian(span[at..]);
            float end = BinaryPrimitives.ReadSingleLittleEndian(span[(at + 4)..]);

            at += 8;                                  // start and end time
            int parameters = Short(span, ref at);     // the sequence name, for a gesture
            at += 4;                                  // parameters 2 and 3

            Ramp(span, ref at);

            at += 1;                                  // flags
            at += 4;                                  // distance to target

            Tags(span, ref at, wide: false);          // relative
            Tags(span, ref at, wide: false);          // timing

            for (int kind = 0; kind < AbsoluteTagTypes; kind++)
            {
                Tags(span, ref at, wide: true);
            }

            if (type == Gesture)
            {
                at += 4;                              // the gesture's own duration
            }

            if (at < span.Length && span[at++] == 1)
            {
                at += 4;                              // a relative tag's name and wav
            }

            Flex(span, ref at);

            // **The two per-type trailers, and they are why this walk cannot stop early.** A `LOOP`
            // writes its count and a `SPEAK` its caption type, token and flags after the flex
            // tracks (<c>choreoevent.cpp:4213</c>); skipping either leaves the cursor inside the
            // event that follows, which in a taunt is the gesture.
            if (type == Loop)
            {
                at += 1;
            }
            else if (type == Speak)
            {
                at += SpeakBytes;
            }

            if (at > span.Length)
            {
                return false;
            }

            string named = Pooled(parameters);

            into?.Add(new SceneEvent(type, start, end, named, began));

            if (found is null && type is Gesture or Sequence && named.Length > 0)
            {
                found = named;
            }
        }

        return true;
    }

    /// <summary>A pooled string index, advancing the cursor.</summary>
    private static int Short(ReadOnlySpan<byte> span, ref int at)
    {
        if (at + 2 > span.Length)
        {
            at = span.Length + 1;
            return -1;
        }

        int value = BinaryPrimitives.ReadInt16LittleEndian(span[at..]);

        at += 2;

        return value;
    }

    /// <summary>A <c>CCurveData</c>: a count, then a float and a byte each.</summary>
    private static void Ramp(ReadOnlySpan<byte> span, ref int at)
    {
        if (at >= span.Length)
        {
            return;
        }

        at += 1 + (span[at] * 5);
    }

    /// <summary>A tag list: a count, then a pooled name and a percentage each.</summary>
    private static void Tags(ReadOnlySpan<byte> span, ref int at, bool wide)
    {
        if (at >= span.Length)
        {
            return;
        }

        // An absolute tag's percentage is a ushort over 4096; the others are a byte over 255.
        at += 1 + (span[at] * (wide ? 4 : 3));
    }

    /// <summary>The flex animation tracks, which a taunt does not use but must be stepped over.</summary>
    /// <remarks>
    /// **A sample is SEVEN bytes, not five** — a float time, a byte value and an unsigned short
    /// curve type (<c>choreoevent.cpp:4419</c>). The curve type is easy to miss because the ramp's
    /// samples, written by <c>CCurveData::SaveToBuffer</c> a few lines away, carry no such field and
    /// really are five.
    /// </remarks>
    private static void Flex(ReadOnlySpan<byte> span, ref int at)
    {
        if (at >= span.Length)
        {
            return;
        }

        int tracks = span[at++];

        for (int track = 0; track < tracks && at < span.Length; track++)
        {
            at += 2;                                  // name, pooled

            if (at >= span.Length)
            {
                return;
            }

            int flags = span[at++];

            at += 8;                                  // min and max

            int samples = Short(span, ref at);

            at += samples * SampleBytes;

            // **`IsComboType` is bit 1 of the flags**, and a combo track carries a second sample
            // list. Missing it puts the cursor into the middle of the next track.
            if ((flags & 0x02) != 0)
            {
                int more = Short(span, ref at);

                at += more * SampleBytes;
            }
        }
    }
}
