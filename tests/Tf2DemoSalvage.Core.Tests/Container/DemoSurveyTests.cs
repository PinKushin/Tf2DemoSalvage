using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// Measuring a demo's real extent, for the case where its header does not state it.
/// </summary>
/// <remarks>
/// **The header's tick count is written last, so a truncated demo does not have one.** The engine
/// records with zeroes in those fields and seeks back to fill them in when recording stops. A
/// recording that ended because the server died never reaches that write, so the file claims zero
/// ticks, zero frames and zero seconds while holding a full match.
///
/// That is not a rare shape. Of 370 real competitive demos measured from an ESEA archive, 159 -
/// forty-three percent - are truncated, and every one of them lies this way.
/// </remarks>
public sealed class DemoSurveyTests
{
    /// <summary>Ticks the fixture's commands actually reach.</summary>
    private const int RealLastTick = 500;

    [Test]
    public void Measure_HeaderDeclaresNothing_ReportsTheTicksTheStreamReaches()
    {
        // The condition: a header that says zero over a stream that plainly is not.
        byte[] demo = Demo(declaredTicks: 0, declaredFrames: 0, lastTick: RealLastTick);

        DemoSurvey survey = DemoSurvey.Measure(demo);

        survey.LastTick.ShouldBe(RealLastTick);
    }

    [Test]
    public void Measure_HeaderDeclaresNothing_ReportsItAsUnstated()
    {
        byte[] demo = Demo(declaredTicks: 0, declaredFrames: 0, lastTick: RealLastTick);

        DemoSurvey.Measure(demo).HeaderStatedLength.ShouldBeFalse();
    }

    [Test]
    public void Measure_HeaderStatesTheLength_TakesTheHeaderWithoutWalking()
    {
        // The control. A complete demo must not be re-measured: the header is authoritative and
        // walking a 39 MB file to confirm it would make opening one feel broken.
        byte[] demo = Demo(declaredTicks: 900, declaredFrames: 3, lastTick: RealLastTick);

        DemoSurvey survey = DemoSurvey.Measure(demo);

        survey.LastTick.ShouldBe(900);
        survey.HeaderStatedLength.ShouldBeTrue();
    }

    [Test]
    public void Measure_TruncatedMidCommand_ReportsTheTicksThatSurvived()
    {
        // The real shape of the archive: the file stops mid-write. Everything before the cut is
        // still a match, and its last COMPLETE tick is what the scrub bar should span.
        //
        // Three bytes is enough to lose the whole final command: a ConsoleCmd is a 5-byte command
        // header, a 4-byte length and then the string, so its payload no longer arrives and the
        // reader stops at it. The surviving extent is therefore the tick before it - predicting
        // RealLastTick here would be predicting that a command which is not in the file was read.
        byte[] demo = Demo(declaredTicks: 0, declaredFrames: 0, lastTick: RealLastTick);
        byte[] cut = demo[..(demo.Length - 3)];

        DemoSurvey survey = DemoSurvey.Measure(cut);

        survey.LastTick.ShouldBe(RealLastTick / 2);
        survey.Truncated.ShouldBeTrue();
    }

    [Test]
    public void Measure_HeaderDeclaresNegativeTicks_IsTreatedAsUnstated()
    {
        // A negative tick count is not a length. Taking it literally would leave the transport
        // disabled for exactly the same reason zero does.
        byte[] demo = Demo(declaredTicks: -4, declaredFrames: 0, lastTick: RealLastTick);

        DemoSurvey.Measure(demo).LastTick.ShouldBe(RealLastTick);
    }

    [Test]
    public void Measure_NoCommandsAtAll_ReportsZeroWithoutFailing()
    {
        byte[] demo = DemoWriter.Write(Header(0, 0), []);

        DemoSurvey survey = DemoSurvey.Measure(demo);

        survey.LastTick.ShouldBe(0);
        survey.HeaderStatedLength.ShouldBeFalse();
    }

    /// <summary>Builds a demo whose commands reach a known tick.</summary>
    private static byte[] Demo(int declaredTicks, int declaredFrames, int lastTick)
    {
        List<DemoCommand> commands =
        [
            Command(DemoCommandType.SyncTick, 0),
            Command(DemoCommandType.ConsoleCmd, lastTick / 2),

            // The highest tick sits on the last command, which is where a truncation removes it.
            Command(DemoCommandType.ConsoleCmd, lastTick),
        ];

        return DemoWriter.Write(Header(declaredTicks, declaredFrames), commands);
    }

    private static DemoCommand Command(DemoCommandType type, int tick) => new(
        type,
        tick,
        type == DemoCommandType.SyncTick ? ReadOnlyMemory<byte>.Empty : ConsoleCommandPayload);

    /// <summary>A null-terminated console command, which is the whole payload of that type.</summary>
    private static ReadOnlyMemory<byte> ConsoleCommandPayload => new byte[] { 0x68, 0x69, 0x00 };

    private static DemoHeader Header(int ticks, int frames) => new()
    {
        DemoProtocol = 3,
        NetworkProtocol = 24,
        ServerName = "survey",
        ClientName = "survey",
        MapName = "cp_process_final",
        GameDirectory = "tf",
        PlaybackTimeSeconds = ticks / 66.667f,
        PlaybackTicks = ticks,
        PlaybackFrames = frames,
        SignonLengthBytes = 0,
    };
}
