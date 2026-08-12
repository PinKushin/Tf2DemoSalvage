using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>
/// How long a demo actually runs, whether or not its header says so.
/// </summary>
/// <param name="LastTick">Highest tick the recording reaches.</param>
/// <param name="HeaderStatedLength">
/// Whether <paramref name="LastTick"/> came from the header. When false it was measured by
/// walking the command stream.
/// </param>
/// <param name="Truncated">Whether the file stops in the middle of a command.</param>
/// <remarks>
/// **The engine writes the header's tick count last, so a truncated demo does not have one.**
/// Recording starts by writing a header full of zeroes, and the real
/// <see cref="DemoHeader.PlaybackTicks"/>, <see cref="DemoHeader.PlaybackFrames"/> and
/// <see cref="DemoHeader.PlaybackTimeSeconds"/> are filled in by seeking back to offset zero when
/// recording stops. A recording that ends because the server died, the map changed or the process
/// was killed never reaches that write, so the file claims to be empty while holding a full match.
///
/// That is not an edge case. Of 370 real competitive demos from an ESEA archive, 159 - forty-three
/// percent - are truncated, and every one of them claims zero ticks.
///
/// Believing the header there is what disabled the viewer's transport: a 110,238-frame demo of
/// <c>cp_process_final</c> opened with no timeline and a dead play button, because it said zero
/// and zero is a length the transport is right to refuse.
///
/// **The measurement is the same "two recordings of one value" trick used elsewhere in this
/// codebase.** The tick count exists twice, by unrelated routes - once as a number the engine
/// wrote at the end, and once as a consequence of the commands themselves. When the cheap copy is
/// missing the expensive one is still there.
/// </remarks>
public readonly record struct DemoSurvey(int LastTick, bool HeaderStatedLength, bool Truncated)
{
    /// <summary>Measures a demo, walking it only if its header does not state a length.</summary>
    /// <param name="demo">The whole file, header included.</param>
    /// <returns>What the demo's real extent is.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="demo"/> is null.</exception>
    /// <exception cref="System.IO.InvalidDataException">The file is not a demo.</exception>
    /// <remarks>
    /// **The walk is skipped when the header is trustworthy, and that is a deliberate cost
    /// decision rather than an optimization.** A complete demo states its own length in the first
    /// 1072 bytes; re-deriving it would mean reading 39 MB to confirm a number already in hand,
    /// on every open, for the majority of files.
    ///
    /// When the walk does happen it reads command headers and steps over payloads - nothing is
    /// decoded, no entity state is built - so it costs a pass over the file and no more.
    /// </remarks>
    public static DemoSurvey Measure(ReadOnlyMemory<byte> demo)
    {
        DemoHeader header = DemoHeader.Parse(demo.Span);

        if (header.PlaybackTicks > 0)
        {
            return new DemoSurvey(header.PlaybackTicks, HeaderStatedLength: true, Truncated: false);
        }

        bool truncated = false;
        int lastTick = 0;

        foreach (DemoCommand command in DemoCommandReader.Read(
                     demo[DemoHeader.SizeBytes..], _ => truncated = true))
        {
            // Max rather than last, because the final command is not required to hold the highest
            // tick: a dem_stop is written at the tick the recording ended, and a demo cut short
            // has whatever command the writer was mid-way through.
            lastTick = Math.Max(lastTick, command.Tick);
        }

        return new DemoSurvey(lastTick, HeaderStatedLength: false, truncated);
    }
}
