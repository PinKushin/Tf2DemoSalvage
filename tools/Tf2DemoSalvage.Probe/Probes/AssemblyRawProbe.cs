using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What the assembly writer still carries as bits, per demo and by label (D200).
/// </summary>
/// <remarks>
/// **D200's measure, named.** <c>EveryDemo_CompilesBackToItsOwnBytes</c> reports what is still raw across the whole
/// corpus; this says which demos hold it, so each residue can be taken to its specimen. It runs the production
/// writer, <see cref="DemoAssembly.Write"/>, into a writer that counts lines and keeps none, so a demo whose text runs
/// to a gigabyte costs no memory for it (B445).
///
/// <c>padding</c> — the bits after a packet's last message — is counted but not listed per demo: it is never a
/// message and every packet has it.
///
/// <code>
///   asm-raw demostf-cp_process_f12-2026-08-07
///   asm-raw all
/// </code>
/// </remarks>
public sealed class AssemblyRawProbe : IProbe
{
    /// <summary>The label every packet's trailing bits carry.</summary>
    private const string PaddingLabel = "padding";

    /// <inheritdoc/>
    public string Name => "asm-raw";

    /// <inheritdoc/>
    public string Summary =>
        "what the assembly text still carries as bits, per demo and by label (D200): asm-raw <demo|all> [more demos]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("asm-raw <demo|all> [more demos]");
            return;
        }

        IEnumerable<string> paths = arguments[0] == "all"
            ? DemoCorpus.Files(output)
            : arguments.Select(name => DemoCorpus.Find(name, output)).OfType<string>();

        Dictionary<string, (long Lines, long Bits)> totals = new(StringComparer.Ordinal);

        foreach (string path in paths)
        {
            byte[] bytes = File.ReadAllBytes(path);
            DemoHeader header = DemoHeader.Parse(bytes.AsSpan(0, DemoHeader.SizeBytes));
            List<DemoCommand> commands = [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];

            using RawLineCounter counter = new();
            DemoAssembly.Write(counter, header, commands);

            IEnumerable<KeyValuePair<string, (long Lines, long Bits)>> residue = counter.ByLabel
                .Where(entry => entry.Key != PaddingLabel)
                .OrderByDescending(entry => entry.Value.Bits);

            string listed = string.Join(", ", residue.Select(entry => string.Create(
                CultureInfo.InvariantCulture, $"{entry.Key} {entry.Value.Lines} ({entry.Value.Bits} bits)")));

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)}: {counter.MessageLines:N0} message lines, {counter.RawLines:N0} raw; " +
                $"{(listed.Length == 0 ? "nothing but padding" : listed)}"));

            foreach ((string label, (long lines, long bits)) in counter.ByLabel)
            {
                (long Lines, long Bits) sum = totals.GetValueOrDefault(label);
                totals[label] = (sum.Lines + lines, sum.Bits + bits);
            }
        }

        output.WriteLine("TOTAL still bits, by label:");

        foreach ((string label, (long lines, long bits)) in totals.OrderByDescending(entry => entry.Value.Bits))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    {bits,14:N0} bits  {lines,10:N0} lines  {label}"));
        }
    }

    /// <summary>A text writer that tallies the assembly's lines as they are written and keeps none of them.</summary>
    /// <remarks>
    /// The same reading of a line <c>CorpusAssemblyRoundTripTests</c> makes: a message line is indented, a raw one
    /// starts <c>raw &lt;bits&gt;</c>, and the writer labels each raw line after <c># </c>.
    /// </remarks>
    private sealed class RawLineCounter : TextWriter
    {
        private readonly StringBuilder _line = new();

        /// <summary>Raw lines and their bits, by the label the writer gave them.</summary>
        public Dictionary<string, (long Lines, long Bits)> ByLabel { get; } = new(StringComparer.Ordinal);

        /// <summary>Indented lines: every message, structured or raw.</summary>
        public long MessageLines { get; private set; }

        /// <summary>Indented lines that are raw bits.</summary>
        public long RawLines { get; private set; }

        /// <inheritdoc/>
        public override Encoding Encoding => Encoding.UTF8;

        /// <inheritdoc/>
        public override void Write(char value)
        {
            if (value == '\n')
            {
                Count(_line.ToString());
                _line.Clear();
                return;
            }

            if (value != '\r')
            {
                _line.Append(value);
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing && _line.Length > 0)
            {
                Count(_line.ToString());
                _line.Clear();
            }

            base.Dispose(disposing);
        }

        private void Count(string line)
        {
            if (line.Length == 0 || line[0] != ' ')
            {
                return;
            }

            MessageLines++;

            ReadOnlySpan<char> trimmed = line.AsSpan().Trim();

            if (!trimmed.StartsWith("raw ", StringComparison.Ordinal))
            {
                return;
            }

            RawLines++;

            ReadOnlySpan<char> afterRaw = trimmed[4..].TrimStart(' ');
            int end = afterRaw.IndexOf(' ');
            long width = long.Parse(end < 0 ? afterRaw : afterRaw[..end], CultureInfo.InvariantCulture);

            int marker = trimmed.IndexOf("# ", StringComparison.Ordinal);
            string label = marker < 0 ? "unlabelled" : trimmed[(marker + 2)..].ToString();

            (long Lines, long Bits) sum = ByLabel.GetValueOrDefault(label);
            ByLabel[label] = (sum.Lines + 1, sum.Bits + width);
        }
    }
}
