using System;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>
/// Guards a decode loop against an iteration that consumes nothing.
/// </summary>
/// <remarks>
/// **Every buffer-walking loop in this parser shares one failure mode.** They all exit when a
/// position reaches a length, so an iteration that consumes zero bytes or zero bits leaves the
/// condition unchanged and the loop runs forever. Nothing about that looks like an error: the
/// process sits at full CPU producing no output, which reads as slow work rather than as a hang.
///
/// It is not hypothetical, and it arrives from two directions:
///
/// - **Mutation testing produces it on demand.** Turning a <c>read++</c> into a <c>read--</c> is
///   a standard mutant, and Snappy's literal loop becomes unbounded under exactly that change. A
///   corpus mutation run took 18 hours and reported 1142 timeouts; every hang costs the full
///   per-mutant timeout, which is what made the run unschedulable rather than merely slow.
/// - **A malformed demo reaches the same state without help.** This project's entire purpose is
///   reading files that other parsers reject, so hostile and truncated input is the normal case,
///   not the edge case. A hang on bad input is a worse outcome than a rejection.
///
/// Deliberately a *progress* check rather than an iteration cap or a wall-clock timeout. A cap
/// needs a limit that is either too low for a real demo or too high to help, and both need
/// tuning per loop; a timeout makes decoding non-deterministic, which would undo the property
/// that the same bytes always produce the same trace. "Consumed something" needs no tuning and
/// stays deterministic.
///
/// A struct, and mutable: it lives as a local beside the loop it guards and is never stored.
/// </remarks>
/// <param name="what">Name of the loop, used in the error so a stall says which one it was.</param>
/// <param name="start">Position before the first iteration.</param>
internal struct DecodeProgress(string what, int start)
{
    private readonly string _what = what;
    private int _last = start;

    /// <summary>Records the position after an iteration, requiring it to have moved forward.</summary>
    /// <param name="position">Position after the iteration.</param>
    /// <exception cref="InvalidDataException">
    /// The position did not advance, so the loop would not terminate.
    /// </exception>
    public void Advanced(int position)
    {
        // `<=` rather than `<`: standing still is exactly as unbounded as going backwards, and it
        // is the more likely of the two in real data - a length field of zero, or a message whose
        // body decoder returns without reading.
        if (position <= _last)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Decoding {_what} made no progress at position {_last}: an iteration consumed " +
                $"nothing, so the loop would not terminate. The input is malformed."));
        }

        _last = position;
    }
}
