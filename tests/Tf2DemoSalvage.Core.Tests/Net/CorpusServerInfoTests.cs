using System;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Xunit.Abstractions;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// <c>svc_ServerInfo</c> against real demos, cross-checked against the file header.
/// </summary>
/// <remarks>
/// The strongest verification available at this layer. The network protocol and map name
/// appear both in the fixed 1072-byte header and inside ServerInfo, written through entirely
/// different mechanisms — a little-endian field at a known offset versus a NUL-terminated
/// string at an arbitrary bit offset. If any field width in ServerInfo were wrong, the map
/// name would be garbage rather than an exact match.
/// </remarks>
public sealed class CorpusServerInfoTests(ITestOutputHelper output)
{
    [Fact]
    public void ServerInfo_AgreesWithTheDemoHeader()
    {
        foreach (string path in Corpus.Files())
        {
            byte[] bytes = File.ReadAllBytes(path);
            DemoHeader header = DemoHeader.Parse(bytes);
            ServerInfoMessage info = FirstServerInfo(bytes)
                .ShouldNotBeNull($"{Path.GetFileName(path)}: no svc_ServerInfo found");

            info.NetworkProtocol.ShouldBe((ushort)header.NetworkProtocol);
            info.Map.ShouldBe(header.MapName);
            info.GameDirectory.ShouldBe(header.GameDirectory);

            output.WriteLine(
                $"{Path.GetFileName(path)}: protocol {info.NetworkProtocol}, map {info.Map}, " +
                $"{info.MaxPlayers} slots, {info.MaxClasses} classes, " +
                $"{info.TickRate:F2} tick, platform {info.Platform}, " +
                $"stv={info.IsSourceTv}, dedicated={info.IsDedicated}, skybox {info.Skybox}");
        }
    }

    [Fact]
    public void ServerInfo_ReportsPlausibleServerSettings()
    {
        foreach (string path in Corpus.Files())
        {
            ServerInfoMessage info = FirstServerInfo(File.ReadAllBytes(path)).ShouldNotBeNull();

            // Bounds a desynchronised decode would blow through immediately.
            info.MaxPlayers.ShouldBeInRange((byte)1, (byte)101);
            info.MaxClasses.ShouldBeGreaterThan((ushort)0);
            info.TickRate.ShouldBeInRange(1f, 1000f);
            info.Skybox.ShouldNotBeNullOrWhiteSpace();
            info.Platform.ShouldBeOneOf('l', 'w');
        }
    }

    [Fact]
    public void ServerInfo_SourceTvFlagMatchesTheDemoKind()
    {
        // A SourceTV demo is recorded by an STV relay, so the flag should say so. The POV
        // demo in the corpus is named for what it is, which makes this checkable.
        foreach (string path in Corpus.Files())
        {
            ServerInfoMessage info = FirstServerInfo(File.ReadAllBytes(path)).ShouldNotBeNull();
            bool isStvDemo = !Path.GetFileName(path).Contains("pov", StringComparison.Ordinal);

            info.IsSourceTv.ShouldBe(isStvDemo, Path.GetFileName(path));
        }
    }

    private static ServerInfoMessage? FirstServerInfo(byte[] bytes)
    {
        NetDecodeState state = new();

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))
            .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet)
            .Take(50))
        {
            NetMessageReadResult result = NetMessageReader.Read(command.Payload.Span, state);
            ServerInfoMessage? info = result.Messages.OfType<ServerInfoMessage>().FirstOrDefault();
            if (info is not null)
            {
                return info;
            }
        }

        return null;
    }
}
