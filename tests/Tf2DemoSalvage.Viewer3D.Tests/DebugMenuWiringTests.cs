using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>Each debug-view menu item sets the mode it is named after.</summary>
/// <remarks>
/// **Written because one of them did not** (B210). The six items are built in a loop and their
/// shared handler decides which mode to set from a `switch` on the item's name — a `switch` with
/// **five arms for six entries**, whose default set `LeafVis`. So `mat_showlowresimage` was
/// unreachable from the UI, and Ctrl+T silently toggled the leaf-box view instead.
///
/// **Everything around it was tested.** `DebugModes` carries the flag, `WorldRenderer` reads it,
/// `LowResImageRenderTests` renders with it, and `ShortcutCollisionTests` proves Ctrl+T claims a key
/// nothing else has. Only the wiring between the menu item and the mode was wrong, and nothing
/// looked there — the third time this session that a tested component was reached by broken
/// production wiring.
///
/// **The log is the observable**, because the handler already writes the whole `DebugModes` record
/// at `Information` and `_debug` is deliberately not public. A test that needed a new accessor to
/// see this would be widening the API to observe a bug.
/// </remarks>
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class DebugMenuWiringTests
{
    [TestCase("DrawFlat")]
    [TestCase("Luxels")]
    [TestCase("NormalMaps")]
    [TestCase("BumpBasis")]
    [TestCase("LeafVis")]
    [TestCase("ShowLowResImage")]
    public void Check_ADebugMenuItem_SetsTheModeItIsNamedAfter(string mode)
    {
        // **Every mode is a case, rather than only the broken one.** The five that worked are the
        // control: a fix that set the right flag by breaking the others would pass a single-case
        // test and fail here.
        RecordingFactory logs = new();

        using MainForm form = new(logs);

        DebugItem(form, mode).Checked = true;

        string line = logs.Last("debug views:")
            ?? throw new InvalidOperationException("the handler did not run, so nothing was wired");

        line.Contains(mode + " = True", StringComparison.Ordinal).ShouldBeTrue(
            $"checking the {mode} item must set {mode}, not whatever the switch falls through to; "
            + $"the handler logged: {line}");
    }

    [Test]
    public void Check_TheLowResImageItem_LeavesTheLeafBoxAlone()
    {
        // **The half a per-item test cannot see.** The broken `switch` did not merely fail to set
        // `ShowLowResImage` — its default arm set `LeafVis`, so Ctrl+T and Ctrl+L wrote the same
        // field and fought each other. An item that set its own flag AND stamped on a neighbour's
        // would pass every case above.
        RecordingFactory logs = new();

        using MainForm form = new(logs);

        DebugItem(form, "ShowLowResImage").Checked = true;

        string line = logs.Last("debug views:")
            ?? throw new InvalidOperationException("the handler did not run, so nothing was wired");

        line.Contains("LeafVis = False", StringComparison.Ordinal).ShouldBeTrue(
            $"the low-res item must not touch the leaf box; the handler logged: {line}");
    }

    private static ToolStripMenuItem DebugItem(MainForm form, string mode) =>
        Descendants(form.MainMenuStrip)
            .Single(item => item.Name == MainForm.DebugMenuItemId + mode);

    private static IEnumerable<ToolStripMenuItem> Descendants(MenuStrip? strip)
    {
        if (strip is null)
        {
            yield break;
        }

        Stack<ToolStripItem> pending = new(strip.Items.OfType<ToolStripItem>());

        while (pending.Count > 0)
        {
            if (pending.Pop() is not ToolStripMenuItem item)
            {
                continue;
            }

            yield return item;

            foreach (ToolStripItem child in item.DropDownItems)
            {
                pending.Push(child);
            }
        }
    }

    /// <summary>A logger factory that keeps every line, so a handler's effect can be read back.</summary>
    private sealed class RecordingFactory : ILoggerFactory
    {
        private readonly List<string> _lines = [];

        /// <summary>The most recent line containing a fragment, or null.</summary>
        public string? Last(string fragment)
        {
            lock (_lines)
            {
                return _lines.FindLast(line => line.Contains(fragment, StringComparison.Ordinal));
            }
        }

        public ILogger CreateLogger(string categoryName) => new Recorder(_lines);

        public void AddProvider(ILoggerProvider provider)
        {
            // Nothing to add to: this factory is its own sink.
        }

        public void Dispose()
        {
            // Nothing held.
        }

        private sealed class Recorder(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);

                lock (lines)
                {
                    lines.Add(formatter(state, exception));
                }
            }
        }
    }
}
