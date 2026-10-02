using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// A point-of-view demo's packets and usercmds in stream order: each packet's two sequence numbers, each usercmd's
/// prologue sequence and <c>command_number</c>, and the recorder's networked origin and velocity at that tick.
/// </summary>
/// <remarks>Written to find which usercmds a packet's state already includes — what prediction re-simulates (D205).</remarks>
public sealed class UserCommandAckProbe : IProbe
{
    /// <summary>The <c>demoheader_t</c> before the first command.</summary>
    private const int DemoHeaderBytes = 1072;

    /// <inheritdoc/>
    public string Name => "usercmd-ack";

    /// <inheritdoc/>
    public string Summary => "packet sequence numbers beside usercmd numbers and the recorder's state: usercmd-ack <demo> <fromTick> <toTick>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 3 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("Usage: usercmd-ack <demo> <fromTick> <toTick>");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        int from = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        int to = int.Parse(arguments[2], CultureInfo.InvariantCulture);
        DemoTimeline timeline = DemoTimeline.Build(bytes);

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeaderBytes)))
        {
            if (command.Tick < from || command.Tick > to)
            {
                continue;
            }

            ReadOnlySpan<byte> prologue = command.Prologue.Span;

            if (command.Type == DemoCommandType.Packet && prologue.Length >= RecordedView.SizeBytes + 8)
            {
                int seqIn = BinaryPrimitives.ReadInt32LittleEndian(prologue[RecordedView.SizeBytes..]);
                int seqOut = BinaryPrimitives.ReadInt32LittleEndian(prologue[(RecordedView.SizeBytes + 4)..]);
                ScenePlayer? recorder = timeline.PlayersAt(command.Tick)
                    .Where(each => each.EntityIndex == timeline.RecorderEntityIndex)
                    .Select(each => (ScenePlayer?)each)
                    .FirstOrDefault();

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"packet  {command.Tick} in {seqIn} out {seqOut}  origin {recorder?.X:0.##} {recorder?.Y:0.##} {recorder?.Z:0.##} vel {recorder?.Velocity} flags {recorder?.Flags}"));
            }
            else if (command.Type == DemoCommandType.UserCmd)
            {
                int sequence = prologue.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(prologue) : -1;
                UserCommand input = UserCommand.Decode(command.Payload.Span);

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"usercmd {command.Tick} seq {sequence} cmd {input.CommandNumber} client_tick {input.TickCount} fwd {input.ForwardMove} side {input.SideMove} buttons 0x{input.Buttons:x} yaw {input.Yaw:0.##}"));
            }
        }
    }
}
