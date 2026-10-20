using System;
using System.Globalization;
using System.Runtime;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The JIT's own running counters at one moment: methods compiled and time spent compiling.</summary>
/// <remarks>
/// **Read from the runtime, never estimated** (B243): <see cref="JitInfo.GetCompiledMethodCount"/> and
/// <see cref="JitInfo.GetCompilationTime"/>, every thread. Finding 84 traced a frame-rate ramp over the first seconds
/// of playback to the tiering worker recompiling the render thread's code, which no phase timer could name.
/// </remarks>
/// <param name="Methods">Methods compiled so far.</param>
/// <param name="Compiling">Time spent compiling so far.</param>
public readonly record struct JitReading(long Methods, TimeSpan Compiling)
{
    /// <summary>The counters now.</summary>
    public static JitReading Now() => new(JitInfo.GetCompiledMethodCount(), JitInfo.GetCompilationTime());

    /// <summary>The compiling between an earlier reading and this one.</summary>
    /// <param name="start">The earlier reading.</param>
    /// <returns>One line.</returns>
    public string Since(JitReading start) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"jit: {Methods - start.Methods} methods compiled, {(Compiling - start.Compiling).TotalMilliseconds:0.0} ms compiling");
}
