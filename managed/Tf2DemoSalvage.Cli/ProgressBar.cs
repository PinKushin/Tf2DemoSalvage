using System;
using System.IO;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Cli;

/// <summary>
/// Draws progress on one line, overwriting itself.
/// </summary>
/// <remarks>
/// Always to standard error, never to standard output, because standard output may be where the
/// trace is going and a bar interleaved with it would corrupt the file.
///
/// **Redraws when the rendered text changes, not on a timer.** A 120,000-command demo reports
/// progress hundreds of times per visible percentage point, and a clock-based throttle would
/// make the output depend on how fast the machine is — the same demo would produce different
/// bytes on different runs, which is exactly the property that makes a test unfalsifiable. Two
/// reports that render identically are indistinguishable to a reader, so dropping the second
/// costs nothing and keeps the behaviour deterministic.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage", "CA2213:Disposable fields should be disposed",
    Justification = "The writer is borrowed, not owned - in practice it is Console.Error, and " +
                    "disposing it would close the process's standard error. Dispose here ends " +
                    "the progress line, which is this type's only resource.")]
public sealed class ProgressBar : IProgress<DumpProgress>, IDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _enabled;
    private string _last = string.Empty;
    private bool _drewSomething;

    /// <summary>Creates a bar.</summary>
    /// <param name="writer">Where to draw, normally standard error.</param>
    /// <param name="enabled">
    /// Whether to draw at all. Pass <c>false</c> when the destination is redirected: carriage
    /// returns aimed at a terminal become junk in a file.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public ProgressBar(TextWriter writer, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(writer);

        _writer = writer;
        _enabled = enabled;
    }

    /// <summary>Creates a bar on standard error, drawing only if it is a terminal.</summary>
    /// <param name="writer">Where to draw.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public ProgressBar(TextWriter writer)
        : this(writer, !Console.IsErrorRedirected)
    {
    }

    /// <inheritdoc />
    public void Report(DumpProgress value)
    {
        if (!_enabled)
        {
            return;
        }

        string bar = value.ToBar();
        if (bar == _last)
        {
            return;
        }

        _last = bar;
        _drewSomething = true;
        _writer.Write('\r');
        _writer.Write(bar);
    }

    /// <summary>Ends the line, if anything was drawn on it.</summary>
    /// <remarks>
    /// Conditional so that a run which drew nothing does not emit a stray blank line into
    /// whatever standard error is attached to.
    /// </remarks>
    public void Finish()
    {
        if (_drewSomething)
        {
            _writer.WriteLine();
            _drewSomething = false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Finish();
}
