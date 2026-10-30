namespace Tf2DemoSalvage.Probe;

/// <summary>
/// Where a POV demo's stamp-to-<c>net_Tick</c> offset may step without the decoder having lost the stream: the one
/// rule <c>CorpusNetMessageTests</c> and the <c>specimen-triage nettick</c> probe both split runs by.
/// </summary>
/// <remarks>
/// **A POV packet's stamp is the client's wall clock, and the server's tick is not** (finding 106,
/// <c>docs/findings/106-a-pov-packet-is-stamped-on-arrival.md</c>). Two things move the offset legitimately:
///
/// - a recording gap, where both clocks jump — more than <see cref="GapTicks"/> on the stamp;
/// - a long host frame, where the server simulated less than the wall passed. <c>net_Tick</c> carries that frame
///   time, and the server stepped at most about 100 ms of game time per frame (*interpolated*: the clamp is not in the
///   SDK). The 2007 scripted client ran at ~17 fps, so its stamp outran the server inside one packet many times — 73
///   ticks in its first 200 packets with no stamp gap at all.
///
/// The second needs BOTH halves: a frame over <see cref="LongFrameSeconds"/> and the stamp outrunning the server by
/// <see cref="OutranTicks"/> or more in that packet. A long frame alone would split SourceTV runs that never drift; an
/// outrun alone is the usual one-tick jitter.
/// </remarks>
public static class NetTickRuns
{
    /// <summary>A stamp jump this large is a recording gap — half a second, where packets are one to three apart.</summary>
    public const int GapTicks = 32;

    /// <summary>A host frame longer than the ~100 ms the server simulates per frame.</summary>
    public const float LongFrameSeconds = 0.1f;

    /// <summary>The stamp outran the server by more than one tick of jitter within the packet.</summary>
    public const int OutranTicks = 2;

    /// <summary>Whether a packet starts a new unbroken run.</summary>
    /// <param name="stampStep">The demo stamp's step since the previous <c>net_Tick</c> packet.</param>
    /// <param name="serverStep">The server tick's step over the same packets.</param>
    /// <param name="hostFrameSeconds">This packet's <c>net_Tick</c> host frame time.</param>
    /// <returns>True where the offset may legitimately step.</returns>
    public static bool Breaks(int stampStep, int serverStep, float hostFrameSeconds) =>
        stampStep > GapTicks ||
        (hostFrameSeconds > LongFrameSeconds && stampStep - serverStep >= OutranTicks);
}
