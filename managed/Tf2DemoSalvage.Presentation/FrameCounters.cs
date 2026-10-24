using System;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The camera upload's pieces, summed over every call so far, in stopwatch ticks (B595).</summary>
/// <param name="View">Choosing and building the view camera.</param>
/// <param name="SetCamera">The feeds and the device's <c>SetCamera</c>: world cull, detail sprites.</param>
/// <param name="Wait">Waiting for the frame in flight before the pose.</param>
/// <param name="Pose">Posing the moment.</param>
/// <param name="Particles">Stepping the particle effects and building their quads.</param>
public readonly record struct CameraTotals(long View, long SetCamera, long Wait, long Pose, long Particles);

/// <summary>Running counters read at one frame boundary, so two readings name what ran between them (B595).</summary>
/// <remarks>
/// **Every value is the producer's own running total, differenced** (B243): the JIT's per-thread and process counters
/// (<see cref="System.Runtime.JitInfo"/>), the collector's pause, the camera's timers where they ran, the sound cache's
/// in-frame decode count, the CPU vertex count models are loaded into, and how many particle systems have ever been born.
/// Read at the same instant as the ledger's frame boundary, so a frame's window and its counters' window are one window
/// — the stall that read "mostly between, gc paused 0 ms" had its pause read over a window that excluded between.
/// </remarks>
/// <param name="MainJitMethods">Methods compiled on the reading thread so far.</param>
/// <param name="MainJit">Time spent compiling on the reading thread so far.</param>
/// <param name="AllJit">Time spent compiling on every thread so far.</param>
/// <param name="GcPaused">The collector's total pause so far.</param>
/// <param name="Camera">The camera's pieces so far.</param>
/// <param name="SoundDecodes">Sound decodes inside a frame so far.</param>
/// <param name="ModelVertices">Entity model vertices held on the CPU.</param>
/// <param name="ParticleSystems">Distinct particle systems ever born.</param>
public readonly record struct FrameCounters(
    long MainJitMethods,
    TimeSpan MainJit,
    TimeSpan AllJit,
    TimeSpan GcPaused,
    CameraTotals Camera,
    int SoundDecodes,
    int ModelVertices,
    int ParticleSystems)
{
    /// <summary>What ran between an earlier reading and this one.</summary>
    /// <param name="start">The earlier reading.</param>
    /// <param name="frequency">Stopwatch ticks a second.</param>
    /// <returns>One clause for the ledger.</returns>
    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
    public string Since(in FrameCounters start, long frequency)
    {
        double ms(long ticks) => ticks * 1000d / frequency;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"main jit {MainJitMethods - start.MainJitMethods} methods {(MainJit - start.MainJit).TotalMilliseconds:0.0} ms" +
            $" (other threads {(AllJit - start.AllJit - (MainJit - start.MainJit)).TotalMilliseconds:0.0} ms)" +
            $"; camera view {ms(Camera.View - start.Camera.View):0.0} setcamera {ms(Camera.SetCamera - start.Camera.SetCamera):0.0}" +
            $" wait {ms(Camera.Wait - start.Camera.Wait):0.0} pose {ms(Camera.Pose - start.Camera.Pose):0.0}" +
            $" particles {ms(Camera.Particles - start.Camera.Particles):0.0}" +
            $"; first uses: sound decodes {SoundDecodes - start.SoundDecodes}, model vertices +{ModelVertices - start.ModelVertices}" +
            $", particle systems +{ParticleSystems - start.ParticleSystems}");
    }
}
