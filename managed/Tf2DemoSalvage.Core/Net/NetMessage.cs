namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Constants shared by the network message layer.
/// </summary>
public static class NetMessage
{
    /// <summary>
    /// Width of the message type field, in bits. Matches Source's <c>NETMSG_TYPE_BITS</c>.
    /// </summary>
    /// <remarks>
    /// Six bits allows ids up to 63; the highest defined at network protocol 24 is 33.
    ///
    /// **This is not a constant of the format — see <see cref="OldTypeBits"/>.** Read the width
    /// from <see cref="NetDecodeState.MessageTypeBits"/> rather than using either constant
    /// directly, or old demos will desynchronise on their first message.
    /// </remarks>
    public const int TypeBits = 6;

    /// <summary>
    /// Width of the message type field before it widened. Five bits, allowing ids up to 31.
    /// </summary>
    /// <remarks>
    /// Source sizes this field by the rule <c>2^NETMSG_TYPE_BITS &gt; SVC_LASTMSG</c>. In 2009
    /// the highest id was <c>svc_GetCvarValue</c> at 31, which five bits covers exactly;
    /// <c>svc_CmdKeyValues</c> (32) and <c>svc_PaintmapData</c> (33) arrived later and forced
    /// the widening.
    ///
    /// Notably absent from Valve's <c>proto_version.h</c>, which enumerates the other era
    /// differences — so this one cannot be found by reading that file, only by decoding a demo
    /// old enough to have it. The reference implementation `demostf/parser` hardcodes six bits
    /// and cannot read such a demo at all.
    /// </remarks>
    public const int OldTypeBits = 5;
}
