using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One command as <c>CDemoPlayer</c>'s view half reads it (B56).</summary>
/// <param name="Type">A signon or packet, or the <c>dem_synctick</c> / <c>dem_stop</c> that ends a parse-ahead.</param>
/// <param name="Tick">The command's tick: for a <c>dem_packet</c>, the recording client's stamp (finding 106).</param>
/// <param name="View">A packet's <c>democmdinfo_t</c>; default for the two markers.</param>
/// <param name="ServerTick">
/// The last <c>net_Tick</c> inside a packet — the server's own tick for its snapshot — or null where it carries none.
/// Read by <c>demo_retimeview</c> only; the engine's reader never looks at it (D238).
/// </param>
internal readonly record struct DemoViewCommand(DemoCommandType Type, int Tick, RecordedView View, int? ServerTick = null)
{
    /// <summary>Whether ReadPacket's default case reads it: a signon or a packet.</summary>
    public bool IsPacket => Type is DemoCommandType.Signon or DemoCommandType.Packet;
}
