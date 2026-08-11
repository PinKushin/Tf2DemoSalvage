using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Fuzz;

/// <summary>
/// The D8 property for the container: a demo either parses or is refused, and one that parses
/// survives a round trip through the writer.
/// </summary>
/// <remarks>
/// **This is the target the primitives cannot reach.** <c>BitReader</c> and <c>VarInt</c> are fed
/// arbitrary bytes and asked to stay inside their contract; the container is where lengths,
/// offsets and command types are read *from the data itself* and then used to index it. That is
/// the shape that produces a hang or an out-of-range read rather than a wrong answer.
///
/// Two properties, and the second is the one worth the machine time:
///
/// 1. **Refusal is documented.** Any input either yields commands or throws
///    <see cref="InvalidDataException"/> or <see cref="EndOfStreamException"/>. An
///    <c>IndexOutOfRangeException</c>, an <c>OverflowException</c> or a
///    <c>NullReferenceException</c> means a length taken from the file was used without being
///    checked against the file.
/// 2. **Parsing is stable under rewriting.** Anything that parses is written back out and read
///    again, and the two command lists must agree. This is the round trip the corpus tests
///    already run over ten real demos — pointed at inputs nobody recorded.
///
/// The second property compares *re-read commands*, not bytes. Comparing against the original
/// input would fail for a reason that is not a defect: a stream ending in <c>dem_stop</c> leaves
/// whatever trailing bytes the fuzzer appended, and the writer does not reproduce them.
/// </remarks>
public static class ContainerFuzzTarget
{
    /// <summary>Beyond this, a malformed length has been rejected and the rest is noise.</summary>
    private const int MaxCommands = 4096;

    /// <summary>Reads a demo, then re-reads what the writer produces from it.</summary>
    /// <param name="data">Arbitrary bytes.</param>
    /// <exception cref="FuzzPropertyViolationException">The container broke its contract.</exception>
    public static void Consume(ReadOnlySpan<byte> data) => _ = ConsumeAndCountCommands(data);

    /// <summary>
    /// <see cref="Consume"/>, reporting how many commands it parsed.
    /// </summary>
    /// <remarks>
    /// Exists for the same reason <c>BitReaderFuzzTarget</c>'s counting overload does: a target
    /// that quietly stopped parsing anything would make every property pass vacuously, and a
    /// libFuzzer run that executes nothing still reports green.
    /// </remarks>
    /// <param name="data">Arbitrary bytes.</param>
    /// <returns>Commands parsed, or zero when the input was refused.</returns>
    public static int ConsumeAndCountCommands(ReadOnlySpan<byte> data)
    {
        if (data.Length < DemoHeader.SizeBytes)
        {
            return 0;
        }

        byte[] owned = data.ToArray();
        DemoHeader header;
        List<DemoCommand> commands;

        try
        {
            header = DemoHeader.Parse(owned);
            commands = Read(owned);
        }
        catch (Exception refusal) when (IsDocumentedRefusal(refusal))
        {
            return 0;
        }
        catch (Exception undocumented)
        {
            throw new FuzzPropertyViolationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Reading a {data.Length}-byte demo threw " +
                    $"{undocumented.GetType().Name}, which is neither success nor a documented " +
                    $"refusal. A length or offset from the file was used without being checked " +
                    $"against it."),
                undocumented);
        }

        if (commands.Count == 0)
        {
            return 0;
        }

        RoundTrip(header, commands, data.Length);
        return commands.Count;
    }

    /// <summary>Writes the commands back out and reads them again, requiring agreement.</summary>
    private static void RoundTrip(
        DemoHeader header, List<DemoCommand> commands, int originalLength)
    {
        byte[] rewritten;

        try
        {
            rewritten = DemoWriter.Write(header, commands);
        }
        catch (Exception failure)
        {
            if (IsDocumentedRefusal(failure))
            {
                // The writer refusing a value it cannot represent is correct behaviour, not a
                // violation - a fuzzer can construct a demo whose dem_stop tick exceeds the
                // three bytes the format gives it, and refusing beats truncating.
                return;
            }

            throw new FuzzPropertyViolationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A demo that parsed into {commands.Count} commands could not be written " +
                    $"back: {failure.GetType().Name}. Anything the reader accepts, the writer " +
                    $"must be able to express."),
                failure);
        }

        List<DemoCommand> reread;

        try
        {
            reread = Read(rewritten);
        }
        catch (Exception failure)
        {
            throw new FuzzPropertyViolationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The writer produced {rewritten.Length} bytes that the reader then " +
                    $"refused with {failure.GetType().Name}. The two disagree about the format."),
                failure);
        }

        if (reread.Count != commands.Count)
        {
            throw new FuzzPropertyViolationException(string.Create(
                CultureInfo.InvariantCulture,
                $"A {originalLength}-byte demo read as {commands.Count} commands, was written " +
                $"back, and read again as {reread.Count}."));
        }

        for (int i = 0; i < commands.Count; i++)
        {
            if (reread[i].Type != commands[i].Type || reread[i].Tick != commands[i].Tick)
            {
                throw new FuzzPropertyViolationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Command {i} was {commands[i].Type} at tick {commands[i].Tick} and came " +
                    $"back as {reread[i].Type} at tick {reread[i].Tick}."));
            }

            if (!reread[i].Payload.Span.SequenceEqual(commands[i].Payload.Span))
            {
                throw new FuzzPropertyViolationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Command {i} ({commands[i].Type}) changed payload across a round trip: " +
                    $"{commands[i].Payload.Length} bytes became {reread[i].Payload.Length}."));
            }
        }
    }

    /// <summary>Reads the command stream, stopping at a bound so a bad length cannot spin.</summary>
    private static List<DemoCommand> Read(byte[] file)
    {
        List<DemoCommand> commands = [];

        foreach (DemoCommand command in
            DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes)))
        {
            commands.Add(command);

            if (commands.Count >= MaxCommands)
            {
                break;
            }
        }

        return commands;
    }

    /// <summary>
    /// Whether an exception is the parser refusing malformed input, as opposed to failing.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow. <c>IndexOutOfRangeException</c> and <c>OverflowException</c> are
    /// excluded because they are precisely what an unchecked length from the file produces, and
    /// admitting them here would make this target unable to detect the bug it exists for.
    /// </remarks>
    private static bool IsDocumentedRefusal(Exception error) =>
        error is InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException;
}
